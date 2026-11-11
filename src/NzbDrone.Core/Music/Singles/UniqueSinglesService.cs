using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.Music.Singles
{
    public interface IUniqueSinglesService
    {
        PagingSpec<SingleDecision> Page(string status, int page, int pageSize);
        void Decide(int id, string action, string fingerprint);
    }

    public class UniqueSinglesService : IUniqueSinglesService, IExecute<ScanUniqueSinglesCommand>,
        IHandle<AlbumEditedEvent>, IHandle<AlbumImportedEvent>, IHandle<AlbumDeletedEvent>, IHandle<TrackFileDeletedEvent>
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<int> Changing = new HashSet<int>();
        private readonly IConfigService _config;
        private readonly IArtistService _artists;
        private readonly IAlbumService _albums;
        private readonly IReleaseService _releases;
        private readonly ITrackService _tracks;
        private readonly IMediaFileService _files;
        private readonly IDeleteMediaFiles _delete;
        private readonly IDiskProvider _disk;
        private readonly ISinglesDecisionRepository _decisions;
        private readonly IManageCommandQueue _commands;
        private readonly Logger _logger;

        public UniqueSinglesService(IConfigService config,
            IArtistService artists,
            IAlbumService albums,
            IReleaseService releases,
            ITrackService tracks,
            IMediaFileService files,
            IDeleteMediaFiles delete,
            IDiskProvider disk,
            ISinglesDecisionRepository decisions,
            IManageCommandQueue commands,
            Logger logger)
        {
            _config = config;
            _artists = artists;
            _albums = albums;
            _releases = releases;
            _tracks = tracks;
            _files = files;
            _delete = delete;
            _disk = disk;
            _decisions = decisions;
            _commands = commands;
            _logger = logger;
        }

        public PagingSpec<SingleDecision> Page(string status, int page, int pageSize)
        {
            var spec = new PagingSpec<SingleDecision> { Page = Math.Max(1, page), PageSize = Math.Clamp(pageSize, 1, 100), SortKey = "Updated", SortDirection = SortDirection.Descending };
            if (status != "all")
            {
                spec.FilterExpressions.Add(x => x.Status == status);
            }

            return _decisions.GetPaged(spec);
        }

        private static bool Single(Album album) => string.Equals(album.AlbumType, "Single", StringComparison.OrdinalIgnoreCase);

        private static bool Cover(Album album)
        {
            return album.Monitored && (string.Equals(album.AlbumType, "Album", StringComparison.OrdinalIgnoreCase) || string.Equals(album.AlbumType, "EP", StringComparison.OrdinalIgnoreCase)) &&
                !album.SecondaryTypes.Any(t => t.Name == "Live" || t.Name == "Remix" || t.Name == "DJ-mix" || t.Name == "Demo");
        }

        private class Context
        {
            public Artist Artist { get; set; }
            public List<Album> Albums { get; set; }
            public Dictionary<int, List<Track>> Tracks { get; set; }
            public List<TrackFile> Files { get; set; }
            public List<SingleCoverage.Source> Sources { get; set; }
            public HashSet<int> IncompleteAlbums { get; set; }
        }

        private Context Load(int artistId)
        {
            var artist = _artists.GetArtist(artistId);
            if (artist == null || !artist.Monitored || !_disk.FolderExists(artist.Path))
            {
                throw new InvalidOperationException("Artist is unmonitored or its folder is unavailable; no cleanup or monitoring changes made");
            }

            var albums = _albums.GetAlbumsByArtist(artistId);
            var files = _files.GetFilesByArtist(artistId);
            var fileLookup = files.ToDictionary(f => f.Id);
            var selected = albums.Where(a => Single(a) || Cover(a)).SelectMany(a => _releases.GetReleasesByAlbum(a.Id).Where(r => r.Monitored)).ToDictionary(r => r.Id);
            var allTracks = _tracks.GetTracksByReleases(selected.Keys.ToList());
            var tracks = allTracks.GroupBy(t => selected[t.AlbumReleaseId].AlbumId).ToDictionary(g => g.Key, g => g.ToList());
            var incomplete = selected.Values.Where(r => r.TrackCount > allTracks.Count(t => t.AlbumReleaseId == r.Id)).Select(r => r.AlbumId).ToHashSet();
            var sources = albums.Where(Cover).SelectMany(a => tracks.GetValueOrDefault(a.Id, new List<Track>()).Where(t => t.HasFile).Select(t => new SingleCoverage.Source
            {
                Album = a, Track = t, File = fileLookup.GetValueOrDefault(t.TrackFileId)
            })).Where(s => s.File != null).ToList();
            return new Context { Artist = artist, Albums = albums, Tracks = tracks, Files = files, Sources = sources, IncompleteAlbums = incomplete };
        }

        private SinglesEvidence Evaluate(Context context, Album album)
        {
            var evidence = SingleCoverage.Evaluate(album, context.Artist.Name, context.Tracks.GetValueOrDefault(album.Id, new List<Track>()), context.Files.Where(f => f.AlbumId == album.Id).ToList(), context.Sources, _config.UniqueSinglesTitleDuration, _config.UniqueSinglesToleranceMs, _disk.FileExists);
            evidence.ForeignArtistId = context.Artist.Metadata.Value.ForeignArtistId;
            if (context.IncompleteAlbums.Contains(album.Id))
            {
                evidence.CanCleanup = false;
                evidence.Strong = false;
                evidence.Reason = "Single track metadata is incomplete; refresh its metadata before cleanup";
            }

            return evidence;
        }

        private void Save(SingleDecision row, SinglesEvidence evidence, string status)
        {
            row.Status = status;
            row.Evidence = JsonConvert.SerializeObject(evidence);
            row.Updated = DateTime.UtcNow;
            if (row.Id == 0)
            {
                row.Id = _decisions.Insert(row).Id;
            }
            else
            {
                _decisions.Update(row);
            }
        }

        private void SetMonitored(int id, bool monitored)
        {
            Changing.Add(id);
            try
            {
                _albums.SetAlbumMonitored(id, monitored);
            }
            finally
            {
                Changing.Remove(id);
            }
        }

        private void CheckSources(SinglesEvidence evidence)
        {
            if (!_config.UniqueSinglesEnabled)
            {
                throw new InvalidOperationException("Unique Singles was disabled; cleanup stopped");
            }

            foreach (var track in evidence.Tracks)
            {
                var source = _files.Get(track.SourceFileId);
                var album = _albums.GetAlbum(track.SourceAlbumId);
                if (source == null || album == null || !Cover(album) || source.AlbumId != album.Id ||
                    _tracks.GetTrack(track.SourceTrackId)?.TrackFileId != source.Id || source.Path != track.SourcePath || source.Size != track.SourceSize || source.Modified != track.SourceModified || !_disk.FileExists(source.Path))
                {
                    throw new InvalidOperationException("Covering album/EP files changed or disappeared; cleanup stopped");
                }
            }
        }

        private void Cleanup(SingleDecision row, SinglesEvidence evidence)
        {
            row.AutoUnmonitored = true;
            Save(row, evidence, "cleaning"); // Persist ownership before changing monitoring or deleting files.
            try
            {
                CheckSources(evidence);
                SetMonitored(row.AlbumId, false);
                Changing.Add(row.AlbumId);
                try
                {
                    foreach (var id in evidence.SingleFileIds)
                    {
                        CheckSources(evidence);
                        var file = _files.Get(id);
                        if (file == null || file.AlbumId != row.AlbumId || evidence.Tracks.Any(t => t.SourceFileId == id || t.SourcePath == file.Path))
                        {
                            throw new InvalidOperationException("Single file ownership changed; cleanup stopped");
                        }

                        _delete.DeleteTrackFile(file); // Uses Lidarr's recycle bin and normal database/events path.
                    }
                }
                finally
                {
                    Changing.Remove(row.AlbumId);
                }

                evidence.Reason = "Unmonitored single and removed its redundant files; album/EP coverage retained";
                Save(row, evidence, "cleaned");
                _logger.Info("Unique Singles cleaned album {0}: {1}", row.AlbumId, evidence.Single);
            }
            catch (Exception ex)
            {
                row.AutoUnmonitored = _albums.GetAlbum(row.AlbumId)?.Monitored == false;
                evidence.Reason = "Cleanup failed or was interrupted; inspect remaining files and recheck before retrying";
                Save(row, evidence, "error");
                _logger.Error(ex, "Unique Singles cleanup failed for album {0}", row.AlbumId);
                throw;
            }
        }

        private void ScanArtist(int artistId)
        {
            lock (Gate)
            {
                if (!_config.UniqueSinglesEnabled)
                {
                    return;
                }

                var context = Load(artistId);
                var rows = _decisions.ForArtist(artistId).ToDictionary(r => r.AlbumId);
                foreach (var album in context.Albums.Where(Single))
                {
                    if (!_config.UniqueSinglesEnabled)
                    {
                        return;
                    }

                    var row = rows.GetValueOrDefault(album.Id) ?? new SingleDecision { ArtistId = artistId, AlbumId = album.Id };
                    if (row.Status == "kept" || (!album.Monitored && !row.AutoUnmonitored))
                    {
                        continue;
                    }

                    var evidence = Evaluate(context, album);
                    if (row.AutoUnmonitored)
                    {
                        if (album.Monitored)
                        {
                            row.AutoUnmonitored = false;
                            evidence.Reason = "Single was manually monitored; automatic cleanup suppressed";
                            Save(row, evidence, "kept");
                            continue;
                        }

                        var previous = JsonConvert.DeserializeObject<SinglesEvidence>(row.Evidence);
                        var covered = evidence.CanCleanup && evidence.Tracks.All(t => t.Tier < 3 || previous.Tracks.Any(p => p.TrackId == t.TrackId &&
                            p.SourceFileId == t.SourceFileId && p.SourceTitle == t.SourceTitle && p.SourceDurationMs == t.SourceDurationMs && p.SourceRecordingId == t.SourceRecordingId));
                        if (!covered)
                        {
                            SetMonitored(album.Id, true);
                            row.AutoUnmonitored = false;
                            evidence.Reason = "Album/EP coverage disappeared; single re-monitored for normal download handling";
                            Save(row, evidence, "restored");
                            _logger.Info("Unique Singles restored monitoring for album {0}", album.Id);
                        }

                        continue;
                    }

                    if (evidence.Tracks.Count == 0 || evidence.Tracks.Any(t => t.Tier == 0))
                    {
                        if (row.Id > 0 && row.Status == "pending")
                        {
                            Save(row, evidence, "notCovered");
                        }

                        continue; // Exclusive tracks are kept, not presented as cleanup candidates.
                    }

                    if (evidence.SingleFileIds.Count == 0 && evidence.Tracks.Any(t => t.Tier != 1))
                    {
                        continue; // Undownloaded singles need exact recording-ID coverage.
                    }

                    if (row.Status == "error" || row.Status == "cleaning")
                    {
                        continue; // Never automatically repeat a failed/interrupted deletion.
                    }

                    if (evidence.Strong && row.Status != "pending")
                    {
                        Cleanup(row, evidence);
                    }
                    else
                    {
                        Save(row, evidence, "pending"); // Once queued for review, only explicit approval can clean it.
                    }
                }
            }
        }

        public void Decide(int id, string action, string fingerprint)
        {
            lock (Gate)
            {
                var row = _decisions.Get(id);
                if (action == "keep")
                {
                    row.AutoUnmonitored = false;
                    var saved = JsonConvert.DeserializeObject<SinglesEvidence>(row.Evidence);
                    saved.Reason = "Kept by user; future automatic cleanup suppressed until rechecked";
                    Save(row, saved, "kept");
                    return;
                }

                if (!_config.UniqueSinglesEnabled)
                {
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "Enable Unique Singles before reviewing or rechecking cleanup");
                }

                if (action != "approve" && action != "recheck")
                {
                    throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Unknown Singles Review action");
                }

                var context = Load(row.ArtistId);
                var album = context.Albums.SingleOrDefault(a => a.Id == row.AlbumId && Single(a));
                if (album == null || (!album.Monitored && !row.AutoUnmonitored))
                {
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "Single is missing or manually unmonitored; no cleanup performed");
                }

                var evidence = Evaluate(context, album);
                if (action == "recheck")
                {
                    Save(row, evidence, "pending");
                    return;
                }

                if (row.Status != "pending" || !evidence.CanCleanup || evidence.Fingerprint != fingerprint)
                {
                    Save(row, evidence, "pending");
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "Cleanup evidence changed or is incomplete. Refresh and review the updated comparison");
                }

                Cleanup(row, evidence);
            }
        }

        public void Execute(ScanUniqueSinglesCommand command)
        {
            if (!_config.UniqueSinglesEnabled)
            {
                return;
            }

            var artists = command.ArtistId > 0 ? new List<Artist> { _artists.GetArtist(command.ArtistId) } : _artists.GetAllArtists();
            var failures = 0;
            foreach (var artist in artists.Where(a => a?.Monitored == true))
            {
                if (!_config.UniqueSinglesEnabled)
                {
                    break;
                }

                try
                {
                    _logger.ProgressInfo("Unique Singles scanning {0}", artist.Name);
                    ScanArtist(artist.Id);
                }
                catch (Exception ex)
                {
                    failures++;
                    _logger.Warn(ex, "Unique Singles skipped or failed artist {0}", artist.Id);
                }
            }

            if (failures > 0)
            {
                throw new InvalidOperationException($"Unique Singles could not finish {failures} artists; see logs and Singles Review");
            }
        }

        private void Queue(int artistId)
        {
            if (_config.UniqueSinglesEnabled && artistId > 0)
            {
                _commands.Push(new ScanUniqueSinglesCommand { ArtistId = artistId });
            }
        }

        public void Handle(AlbumImportedEvent message) => Queue(message.Artist?.Id ?? 0);
        public void Handle(AlbumDeletedEvent message) => Queue(message.Album.ArtistId);

        public void Handle(TrackFileDeletedEvent message)
        {
            lock (Gate)
            {
                if (!Changing.Contains(message.TrackFile.AlbumId))
                {
                    Queue(_albums.GetAlbums(new[] { message.TrackFile.AlbumId }).SingleOrDefault()?.ArtistId ?? 0);
                }
            }
        }

        public void Handle(AlbumEditedEvent message)
        {
            lock (Gate)
            {
                if (Changing.Contains(message.Album.Id))
                {
                    return;
                }

                if (Single(message.Album))
                {
                    var row = _decisions.ForAlbum(message.Album.Id);
                    if (row != null && (row.AutoUnmonitored || row.Status == "pending") &&
                        (message.Album.Monitored != message.OldAlbum.Monitored || ReferenceEquals(message.Album, message.OldAlbum)))
                    {
                        row.AutoUnmonitored = false;
                        var evidence = JsonConvert.DeserializeObject<SinglesEvidence>(row.Evidence);
                        evidence.Reason = "Manual monitoring choice preserved; automatic cleanup suppressed until rechecked";
                        Save(row, evidence, "kept");
                    }
                }
                else
                {
                    Queue(message.Album.ArtistId);
                }
            }
        }
    }
}
