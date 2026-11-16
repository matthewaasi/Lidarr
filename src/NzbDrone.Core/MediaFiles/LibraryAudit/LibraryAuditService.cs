using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.LibraryAudit
{
    public interface ILibraryAuditService
    {
        List<LibraryAuditFinding> Findings();
        void Decide(int id, string evidenceKey, string action);
        void DeleteFindings(int[] ids);
        void DeleteAllFindings();
        void DeleteFindingFiles(AuditFileDeletion selection);
        AuditDeletionPreview PreviewFindingFileDeletion(AuditFileDeletion selection);
    }

    public class LibraryAuditService : ILibraryAuditService, IExecute<ScanLibraryAuditCommand>
    {
        private readonly IMediaFileRepository _files;
        private readonly IDeleteMediaFiles _deletion;
        private readonly ITrackService _tracks;
        private readonly IAudioTagService _tags;
        private readonly IDiskProvider _disk;
        private readonly ILibraryAuditRepository _repository;
        private readonly Logger _logger;
        private readonly IFingerprintingService _fingerprints;
        private readonly object _gate = new object();

        public LibraryAuditService(IMediaFileRepository files, ITrackService tracks, IAudioTagService tags, IDiskProvider disk, ILibraryAuditRepository repository, IFingerprintingService fingerprints, IDeleteMediaFiles deletion, Logger logger)
        {
            _files = files;
            _deletion = deletion;
            _tracks = tracks;
            _tags = tags;
            _disk = disk;
            _repository = repository;
            _logger = logger;
            _fingerprints = fingerprints;
        }

        public List<LibraryAuditFinding> Findings() => _repository.All().OrderByDescending(x => x.Updated).ThenBy(x => x.Id).ToList();

        public void DeleteFindings(int[] ids)
        {
            if (ids == null || ids.Length == 0 || ids.Any(id => id <= 0))
            {
                throw new BadRequestException("Select at least one valid audit finding.");
            }

            lock (_gate)
            {
                _repository.DeleteMany(ids.Distinct());
            }
        }

        public void DeleteAllFindings()
        {
            lock (_gate)
            {
                _repository.Purge();
            }
        }

        public AuditDeletionPreview PreviewFindingFileDeletion(AuditFileDeletion selection)
        {
            lock (_gate)
            {
                return BuildDeletionPreview(selection, out _);
            }
        }

        private AuditDeletionPreview BuildDeletionPreview(AuditFileDeletion selection, out List<TrackFile> files)
        {
            if (selection?.Findings == null || selection.Findings.Count == 0 ||
                selection.Findings.Any(x => x == null || x.Id <= 0 || string.IsNullOrWhiteSpace(x.EvidenceKey)) ||
                selection.FileIds == null || selection.FileIds.Length == 0 || selection.FileIds.Any(id => id <= 0))
            {
                throw new BadRequestException("Select current audit findings and the specific files to delete.");
            }

            var evidenceFiles = new List<AuditFile>();
            foreach (var selected in selection.Findings)
            {
                var row = _repository.Get(selected.Id);
                if (row.EvidenceKey != selected.EvidenceKey || row.Status == "resolved")
                {
                    throw new BadRequestException("This finding has changed. Refresh and rescan before deleting files.");
                }

                evidenceFiles.AddRange(JsonConvert.DeserializeObject<AuditEvidence>(row.Evidence).Files);
            }

            files = new List<TrackFile>();
            foreach (var id in selection.FileIds.Distinct())
            {
                var snapshots = evidenceFiles.Where(x => x.Id == id).ToList();
                var file = _files.Find(id);
                if (snapshots.Count == 0 || file == null || snapshots.Any(x => x.Path != file.Path))
                {
                    throw new BadRequestException("A selected file is missing, moved or outside these findings. Refresh and rescan before deleting files.");
                }

                try
                {
                    foreach (var snapshot in snapshots)
                    {
                        EnsureUnchanged(snapshot);
                    }
                }
                catch (IOException)
                {
                    throw new BadRequestException("A selected file changed or is unavailable. Rescan before deleting files.");
                }

                files.Add(file);
            }

            var ids = selection.FileIds.ToHashSet();
            var snapshotsById = evidenceFiles.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
            var preview = new AuditDeletionPreview
            {
                Files = files.OrderBy(x => x.Id).Select(x => snapshotsById[x.Id]).ToList(),
                TotalBytes = files.Sum(x => snapshotsById[x.Id].Size),
                DuplicateGroups = _repository.All().Where(x => x.Kind == "duplicate" && x.Status != "resolved")
                    .OrderBy(x => x.Id).Select(x => new AuditDuplicateDeletionGroup
                    {
                        FindingId = x.Id,
                        EvidenceKey = x.EvidenceKey,
                        Files = JsonConvert.DeserializeObject<AuditEvidence>(x.Evidence).Files
                    }).Where(x => x.Files.Any(file => ids.Contains(file.Id)))
                    .Select(x => new AuditDuplicateDeletionGroup
                    {
                        FindingId = x.FindingId,
                        EvidenceKey = x.EvidenceKey,
                        Files = x.Files.Where(file => ids.Contains(file.Id) ||
                            _disk.FileExists(_files.Find(file.Id)?.Path ?? file.Path)).ToList()
                    }).Where(x => x.Files.Count > 0 && x.Files.All(file => ids.Contains(file.Id))).ToList()
            };
            preview.PreviewKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { Findings = selection.Findings.OrderBy(x => x.Id), Preview = preview }))));
            return preview;
        }

        public void DeleteFindingFiles(AuditFileDeletion selection)
        {
            lock (_gate)
            {
                var preview = BuildDeletionPreview(selection, out var files);
                if (selection.PreviewKey != preview.PreviewKey)
                {
                    throw new BadRequestException("Review a fresh deletion preview before deleting files. The selection or audit evidence may have changed.");
                }

                if (preview.DuplicateGroups.Count > 0 && !selection.ConfirmAllCopies)
                {
                    throw new BadRequestException("This selection removes every copy in a duplicate group. Explicitly confirm deletion of all copies or keep at least one copy.");
                }

                // Validate the entire selection before deleting anything. Only explicit file IDs are removed.
                try
                {
                    foreach (var file in files)
                    {
                        if (file.AlbumId > 0 && file.Artist?.Value != null)
                        {
                            _deletion.DeleteTrackFile(file.Artist.Value, file);
                        }
                        else
                        {
                            _deletion.DeleteTrackFile(file, "Unmapped_Files");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Audit file deletion stopped before completing the selection");
                    throw new BadRequestException("File deletion stopped. Some selected files may already have been deleted. Findings were retained; refresh and rescan before retrying.");
                }

                _repository.DeleteMany(selection.Findings.Select(x => x.Id).Distinct());
            }
        }

        public void Decide(int id, string evidenceKey, string action)
        {
            if (action != "ignore" && action != "restore")
            {
                throw new BadRequestException("Choose ignore or restore.");
            }

            lock (_gate)
            {
                var row = _repository.Get(id);
                if (row.EvidenceKey != evidenceKey || row.Status == "resolved")
                {
                    throw new BadRequestException("This finding has changed. Refresh the audit before reviewing it.");
                }

                row.Status = action == "ignore" ? "ignored" : "pending";
                _repository.Update(row);
            }
        }

        public void Execute(ScanLibraryAuditCommand message)
        {
            var cancellationToken = message.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (!message.Versions && !message.Duplicates)
            {
                throw new BadRequestException("Select at least one audit scan type.");
            }

            if (message.Fingerprints && (!message.Versions || !_fingerprints.IsSetup()))
            {
                throw new BadRequestException("Fingerprint checks require Version audit and a working fpcalc installation.");
            }

            var progressClock = Stopwatch.StartNew();
            string lastPhase = null;
            void Report(string phase, int processed, int? total, string activity = null)
            {
                message.ReportProgress(phase, processed, total, activity);
                if (phase != lastPhase || progressClock.ElapsedMilliseconds >= 2000 || (total.HasValue && processed == total && activity == null))
                {
                    var progress = message.GetProgressSnapshot();
                    _logger.ProgressInfo("Library audit: {0} {1}/{2} {3}", phase, processed, total?.ToString() ?? "unknown", activity ?? (progress.Percentage.HasValue ? progress.Percentage.Value + "%" : string.Empty));
                    lastPhase = phase;
                    progressClock.Restart();
                }
            }

            Report("loading", 0, null, "Loading library files");
            var findings = new List<LibraryAuditFinding>();
            var files = _files.All().OrderBy(x => x.Id).ToList();
            var snapshots = new Dictionary<int, AuditFile>();
            var catalog = new Dictionary<int, List<Track>>();
            var versionFailures = new HashSet<int>();
            var duplicateFailures = new HashSet<int>();
            _logger.ProgressInfo("Library audit: inspecting {0} files", files.Count);
            var inspected = 0;
            Report("inspection", 0, files.Count);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report("inspection", inspected, files.Count, $"Inspecting file {inspected + 1} of {files.Count}");
                var snapshot = Describe(file);
                try
                {
                    snapshot.Size = _disk.GetFileSize(file.Path);
                    snapshot.Modified = _disk.FileGetLastWrite(file.Path).ToUniversalTime();
                    var duplicateSnapshot = Describe(file);
                    duplicateSnapshot.Size = snapshot.Size;
                    duplicateSnapshot.Modified = snapshot.Modified;
                    snapshots.Add(file.Id, duplicateSnapshot);
                    if (!message.Versions)
                    {
                        continue;
                    }

                    var tags = _tags.ReadTags(file.Path);
                    if (tags == null)
                    {
                        throw new IOException("No readable audio metadata was returned.");
                    }

                    snapshot.TagTitle = tags.Title;
                    snapshot.DurationSeconds = tags.Duration.TotalSeconds;
                    var artistId = file.Artist?.Value?.Id ?? 0;
                    if (!catalog.TryGetValue(artistId, out var artistTracks))
                    {
                        artistTracks = artistId > 0 ? _tracks.GetTracksByArtist(artistId) : new List<Track>();
                        catalog.Add(artistId, artistTracks);
                    }

                    // Multi-track files do not have a single comparable catalog duration/title.
                    if (file.Tracks?.Value?.Count == 1)
                    {
                        if (message.Fingerprints)
                        {
                            Report("inspection", inspected, files.Count, $"Fingerprint lookup for file {inspected + 1} of {files.Count}");
                            _logger.ProgressInfo("Library audit: fingerprinting file {0} of {1}", snapshots.Count, files.Count);
                            var local = new LocalTrack { Path = file.Path, ExistingFile = true };
                            try
                            {
                                _fingerprints.Lookup(new List<LocalTrack> { local }, 0.9);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug(ex, "Library audit fingerprint lookup failed for {0}", file.Path);
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                            snapshot.AudioRecordingIds = (local.AcoustIdResults ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList();
                            snapshot.FingerprintStatus = snapshot.AudioRecordingIds.Count > 0 ? "Recording associations found" : "No confident result / lookup unavailable";
                            if (snapshot.AudioRecordingIds.Count == 0)
                            {
                                versionFailures.Add(file.Id);
                                findings.Add(Error("fingerprintError", snapshot, new IOException("No confident fingerprint result was returned. The recording may be absent from AcoustID, fpcalc may have failed, or the lookup may be unavailable. Local version checks still ran.")));
                            }
                        }

                        EnsureUnchanged(snapshot);
                        var evidence = VersionAudit.Evaluate(snapshot, artistTracks);
                        if (evidence != null)
                        {
                            findings.Add(Finding("version", evidence));
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    versionFailures.Add(file.Id);
                    try
                    {
                        EnsureUnchanged(snapshot);
                    }
                    catch (Exception)
                    {
                        snapshots.Remove(file.Id);
                    }

                    if (!snapshots.ContainsKey(file.Id))
                    {
                        duplicateFailures.Add(file.Id);
                    }

                    findings.Add(Error(message.Versions ? "versionError" : "duplicateError", snapshot, ex));
                    _logger.Debug(ex, "Library audit could not inspect {0}", file.Path);
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Report("inspection", ++inspected, files.Count);
                    }
                }
            }

            if (message.Duplicates)
            {
                // Equal length is required for byte-identical files. Avoid reading unique sizes.
                var groups = snapshots.Values.Where(x => x.Size > 0).GroupBy(x => x.Size).Where(x => x.Count() > 1).ToList();
                var processed = 0;
                var duplicateFiles = groups.Sum(group => group.Count());
                var hashed = 0;
                Report("duplicates", 0, duplicateFiles);
                foreach (var group in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hashes = new Dictionary<string, List<AuditFile>>();
                    foreach (var file in group)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Report("duplicates", hashed, duplicateFiles, $"Hashing file {hashed + 1} of {duplicateFiles}; group {processed + 1} of {groups.Count}");
                        try
                        {
                            EnsureUnchanged(file);
                            using var stream = _disk.OpenReadStream(file.Path);
                            var hash = HashFile(stream, cancellationToken);
                            EnsureUnchanged(file);
                            if (!hashes.TryGetValue(hash, out var matches))
                            {
                                matches = new List<AuditFile>();
                                hashes.Add(hash, matches);
                            }

                            matches.Add(file);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            duplicateFailures.Add(file.Id);
                            findings.Add(Error("duplicateError", file, ex));
                            _logger.Debug(ex, "Library audit could not hash {0}", file.Path);
                        }
                        finally
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                Report("duplicates", ++hashed, duplicateFiles);
                            }
                        }
                    }

                    Report("duplicates", hashed, duplicateFiles, $"Verifying duplicate group {processed + 1} of {groups.Count}");
                    foreach (var match in hashes.Where(x => x.Value.Count > 1))
                    {
                        var verified = new List<AuditFile>();
                        foreach (var file in match.Value)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                EnsureUnchanged(file);
                                verified.Add(file);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                duplicateFailures.Add(file.Id);
                                findings.Add(Error("duplicateError", file, ex));
                            }
                        }

                        if (verified.Count < 2)
                        {
                            continue;
                        }

                        findings.Add(Finding("duplicate", new AuditEvidence
                        {
                            Confidence = "exact",
                            Reason = $"These files have identical complete-file SHA-256 hashes ({match.Key}). This proves identical file bytes, including tags; it does not identify the correct mix label.",
                            SuggestedAction = "Review the album links before choosing which copies to retain. Scans never remove files or change monitoring. File deletion requires explicit selection and confirmation.",
                            Files = verified.OrderBy(x => x.Id).ToList()
                        }));
                    }

                    _logger.ProgressInfo("Library audit: checked {0} of {1} duplicate size groups", ++processed, groups.Count);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Publish only after the scan completes. A failed scan must not resolve previous findings.
            lock (_gate)
            {
                message.BeginFinalization();
                Report("saving", 0, null, "Saving findings; cancellation is unavailable during this phase");
                _logger.ProgressInfo("Saving completed library audit findings");
                var existing = _repository.All().ToDictionary(x => x.EvidenceKey);
                var seen = new HashSet<string>();
                foreach (var finding in findings)
                {
                    if (!seen.Add(finding.EvidenceKey))
                    {
                        continue;
                    }

                    if (existing.TryGetValue(finding.EvidenceKey, out var previous))
                    {
                        previous.Status = previous.Status == "ignored" ? "ignored" : "pending";
                        previous.Updated = finding.Updated;
                        _repository.Update(previous);
                    }
                    else
                    {
                        _repository.Insert(finding);
                    }
                }

                foreach (var old in existing.Values.Where(x => !seen.Contains(x.EvidenceKey) &&
                    ((message.Versions && (x.Kind == "version" || x.Kind == "versionError" || (message.Fingerprints && x.Kind == "fingerprintError"))) ||
                     (message.Duplicates && (x.Kind == "duplicate" || x.Kind == "duplicateError")))))
                {
                    var evidence = JsonConvert.DeserializeObject<AuditEvidence>(old.Evidence);
                    if (!message.Fingerprints && old.Kind == "version" && evidence.Confidence == "supported")
                    {
                        continue;
                    }

                    var failures = old.Kind == "version" || old.Kind == "versionError" || old.Kind == "fingerprintError" ? versionFailures : duplicateFailures;
                    if (evidence.Files.Any(x => failures.Contains(x.Id)))
                    {
                        continue;
                    }

                    old.Status = "resolved";
                    _repository.Update(old);
                }
            }

            _logger.ProgressInfo("Library audit finished: {0} findings. Review them under Library → Audit.", findings.Count);
            Report("completed", 1, 1, $"Audit finished: {findings.Count} findings");
        }

        private static string HashFile(Stream stream, CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            try
            {
                int length;
                while ((length = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, length);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return Convert.ToHexString(hash.GetHashAndReset());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void EnsureUnchanged(AuditFile file)
        {
            if (_disk.GetFileSize(file.Path) != file.Size || _disk.FileGetLastWrite(file.Path).ToUniversalTime() != file.Modified)
            {
                throw new IOException("File changed during the scan; run the audit again.");
            }
        }

        private static AuditFile Describe(TrackFile file)
        {
            var track = file.Tracks?.Value?.Count == 1 ? file.Tracks.Value[0] : null;
            return new AuditFile
            {
                Id = file.Id,
                Path = file.Path,
                Size = file.Size,
                Modified = file.Modified,
                Artist = file.Artist?.Value?.Name,
                ForeignArtistId = file.Artist?.Value?.ForeignArtistId,
                ForeignAlbumId = file.Album?.Value?.ForeignAlbumId,
                CatalogTitle = track?.Title,
                CatalogDurationSeconds = (track?.Duration ?? 0) / 1000.0,
                RecordingId = track?.ForeignRecordingId
            };
        }

        private static LibraryAuditFinding Error(string kind, AuditFile file, Exception error) => Finding(kind, new AuditEvidence
        {
            Confidence = "unavailable",
            Reason = $"Could not inspect this file: {error.Message}",
            SuggestedAction = "Check that the file exists, permissions allow reading, and the audio is readable; then rescan.",
            Files = new List<AuditFile> { file }
        });

        private static LibraryAuditFinding Finding(string kind, AuditEvidence evidence)
        {
            var json = JsonConvert.SerializeObject(evidence);
            return new LibraryAuditFinding
            {
                EvidenceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + json))),
                Kind = kind,
                Status = "pending",
                Evidence = json,
                Updated = DateTime.UtcNow
            };
        }
    }
}
