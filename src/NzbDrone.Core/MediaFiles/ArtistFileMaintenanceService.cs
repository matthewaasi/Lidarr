using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MediaFiles
{
    public class ArtistFilePreview
    {
        public string Key { get; set; }
        public List<RenameTrackFilePreview> Renames { get; set; } = new ();
        public List<RetagTrackFilePreview> Retags { get; set; } = new ();
        public List<string> Skipped { get; set; } = new ();

        public IEnumerable<int> FileIds => Renames.Select(x => x.TrackFileId).Concat(Retags.Select(x => x.TrackFileId));
    }

    public class ApplyArtistFilePreviewsCommand : Command
    {
        public ArtistFileOperation Operation { get; set; }
        public Dictionary<int, string> Reviews { get; set; } = new ();

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsTypeExclusive => true;
        public override bool SupportsCancellation => true;
        public override bool IsLongRunning => true;
    }

    public interface IArtistFileMaintenanceService
    {
        ArtistFilePreview Preview(int artistId, ArtistFileOperation operation, bool record = true);
    }

    public class ArtistFileMaintenanceService : IArtistFileMaintenanceService, IExecute<ApplyArtistFilePreviewsCommand>, IHandleAsync<CommandExecutedEvent>
    {
        private readonly IRenameTrackFileService _rename;
        private readonly IAudioTagService _retag;
        private readonly IExecute<RenameFilesCommand> _renameFiles;
        private readonly IExecute<RetagFilesCommand> _retagFiles;
        private readonly IArtistFileStatusService _status;
        private readonly IMediaFileService _media;
        private readonly IArtistService _artists;
        private readonly IDiskProvider _disk;
        private readonly Logger _logger;

        public ArtistFileMaintenanceService(IRenameTrackFileService rename,
                                            IAudioTagService retag,
                                            IExecute<RenameFilesCommand> renameFiles,
                                            IExecute<RetagFilesCommand> retagFiles,
                                            IArtistFileStatusService status,
                                            IMediaFileService media,
                                            IArtistService artists,
                                            IDiskProvider disk,
                                            Logger logger)
        {
            _rename = rename;
            _retag = retag;
            _renameFiles = renameFiles;
            _retagFiles = retagFiles;
            _status = status;
            _media = media;
            _artists = artists;
            _disk = disk;
            _logger = logger;
        }

        public ArtistFilePreview Preview(int artistId, ArtistFileOperation operation, bool record = true)
        {
            _artists.GetArtist(artistId);
            var settings = _status.GetSettings(operation);
            var preview = new ArtistFilePreview();

            try
            {
                var files = _media.GetFilesByArtist(artistId).OrderBy(x => x.Id).ToList();
                var blocked = files.Where(x => !_disk.FileExists(x.Path) ||
                    (operation == ArtistFileOperation.Retag && x.Tracks.Value.Count != 1)).ToList();
                var blockedIds = blocked.Select(x => x.Id).ToHashSet();
                preview.Skipped.AddRange(blocked.Select(x => x.Path));

                if (operation == ArtistFileOperation.Rename)
                {
                    var renames = _rename.GetRenamePreviews(artistId);
                    var duplicatePaths = renames.GroupBy(x => x.NewPath, StringComparer.Ordinal)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
                    foreach (var rename in renames.Where(x => !blockedIds.Contains(x.TrackFileId)).OrderBy(x => x.TrackFileId))
                    {
                        var samePathIgnoringCase = string.Equals(rename.ExistingPath, rename.NewPath, StringComparison.OrdinalIgnoreCase);
                        if (duplicatePaths.Contains(rename.NewPath) ||
                            (!samePathIgnoringCase && _disk.FileExists(rename.NewPath)))
                        {
                            preview.Skipped.Add($"{rename.ExistingPath} → {rename.NewPath} (destination conflict)");
                        }
                        else
                        {
                            preview.Renames.Add(rename);
                        }
                    }
                }
                else
                {
                    preview.Retags = _retag.GetRetagPreviewsForFiles(files.Where(x => !blockedIds.Contains(x.Id)).ToList(), preview.Skipped.Add);
                }

                var evidence = STJson.ToJson(new
                {
                    artistId,
                    operation,
                    settings,
                    files = files.Select(x => new { x.Id, x.Path, x.Size, x.Modified }),
                    preview
                });
                preview.Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to preview {0} for artist {1}", operation, artistId);
                preview = new ArtistFilePreview { Skipped = new List<string> { "Preview failed. See the Lidarr log for details." } };
            }

            if (record)
            {
                _status.Record(artistId, operation, settings, preview.FileIds.Count(), preview.Skipped.Count > 0);
            }

            return preview;
        }

        public void HandleAsync(CommandExecutedEvent message)
        {
            if (message.Command.Status != CommandStatus.Completed)
            {
                return;
            }

            switch (message.Command.Body)
            {
                case RenameFilesCommand rename:
                    VerifyIndividual(rename.ArtistId, ArtistFileOperation.Rename);
                    break;
                case RetagFilesCommand retag:
                    VerifyIndividual(retag.ArtistId, ArtistFileOperation.Retag);
                    break;
                case RenameArtistCommand rename:
                    foreach (var artistId in rename.ArtistIds)
                    {
                        VerifyIndividual(artistId, ArtistFileOperation.Rename);
                    }

                    break;
                case RetagArtistCommand retag:
                    foreach (var artistId in retag.ArtistIds)
                    {
                        VerifyIndividual(artistId, ArtistFileOperation.Retag);
                    }

                    break;
            }
        }

        private void VerifyIndividual(int artistId, ArtistFileOperation operation)
        {
            var settings = _status.GetSettings(operation);
            try
            {
                var preview = Preview(artistId, operation, false);
                _status.Record(artistId, operation, settings, preview.FileIds.Count(), preview.Skipped.Count > 0, applied: true);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to verify {0} for artist {1}", operation, artistId);
            }
        }

        public void Execute(ApplyArtistFilePreviewsCommand message)
        {
            if (!Enum.IsDefined(message.Operation) || message.Reviews == null)
            {
                throw new InvalidOperationException("Invalid artist file operation");
            }

            var incomplete = 0;
            var processed = 0;
            foreach (var (artistId, key) in message.Reviews)
            {
                message.CancellationToken.ThrowIfCancellationRequested();
                _logger.ProgressInfo("{0}: artist {1} of {2}", message.Operation, ++processed, message.Reviews.Count);
                var settings = _status.GetSettings(message.Operation);

                try
                {
                    var preview = Preview(artistId, message.Operation, false);
                    var ids = preview.FileIds.ToList();
                    if (string.IsNullOrEmpty(key) || preview.Key != key || ids.Count == 0)
                    {
                        ++incomplete;
                        _status.Record(artistId, message.Operation, settings, ids.Count, true);
                        continue;
                    }

                    if (message.Operation == ArtistFileOperation.Rename)
                    {
                        _renameFiles.Execute(new RenameFilesCommand(artistId, ids));
                    }
                    else
                    {
                        _retagFiles.Execute(new RetagFilesCommand(artistId, ids));
                    }

                    var verified = Preview(artistId, message.Operation, false);
                    var pending = verified.FileIds.Count();
                    var failed = verified.Skipped.Count > 0 || pending > 0 ||
                        settings != _status.GetSettings(message.Operation);
                    _status.Record(artistId, message.Operation, settings, pending, failed, applied: true);
                    if (failed)
                    {
                        ++incomplete;
                    }
                }
                catch (Exception ex)
                {
                    ++incomplete;
                    _logger.Warn(ex, "Unable to apply {0} for artist {1}", message.Operation, artistId);
                    _status.Record(artistId, message.Operation, settings, 0, true);
                }
            }

            _logger.ProgressInfo("{0}: {1} verified, {2} skipped or incomplete", message.Operation, processed - incomplete, incomplete);
        }
    }
}
