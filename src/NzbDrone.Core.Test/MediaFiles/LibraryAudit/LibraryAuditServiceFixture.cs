using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using Newtonsoft.Json;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.LibraryAudit;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFilesTests.LibraryAudit
{
    [TestFixture]
    public class LibraryAuditServiceFixture
    {
        private Mock<IMediaFileRepository> _files;
        private Mock<IDeleteMediaFiles> _deletion;
        private Mock<ITrackService> _tracks;
        private Mock<IAudioTagService> _tags;
        private Mock<IDiskProvider> _disk;
        private Mock<ILibraryAuditRepository> _repository;
        private Mock<IFingerprintingService> _fingerprints;
        private List<TrackFile> _library;
        private List<LibraryAuditFinding> _rows;
        private LibraryAuditService _service;
        private List<string> _paths;

        [SetUp]
        public void Setup()
        {
            _files = new Mock<IMediaFileRepository>();
            _deletion = new Mock<IDeleteMediaFiles>();
            _tracks = new Mock<ITrackService>();
            _tags = new Mock<IAudioTagService>();
            _disk = new Mock<IDiskProvider>();
            _repository = new Mock<ILibraryAuditRepository>();
            _fingerprints = new Mock<IFingerprintingService>();
            _library = new List<TrackFile>();
            _rows = new List<LibraryAuditFinding>();
            _paths = new List<string>();
            _files.Setup(x => x.Find(It.IsAny<int>())).Returns((int id) => _library.SingleOrDefault(x => x.Id == id));
            _files.Setup(x => x.All()).Returns(() => _library.ToList());
            _repository.Setup(x => x.All()).Returns(() => _rows.ToList());
            _repository.Setup(x => x.Get(It.IsAny<int>())).Returns((int id) => _rows.Single(x => x.Id == id));
            _repository.Setup(x => x.Insert(It.IsAny<LibraryAuditFinding>())).Returns((LibraryAuditFinding row) =>
            {
                row.Id = _rows.Count + 1;
                _rows.Add(row);
                return row;
            });
            _repository.Setup(x => x.Update(It.IsAny<LibraryAuditFinding>())).Returns((LibraryAuditFinding row) => row);
            _disk.Setup(x => x.FileExists(It.IsAny<string>())).Returns((string path) => File.Exists(path));
            _disk.Setup(x => x.GetFileSize(It.IsAny<string>())).Returns((string path) => new FileInfo(path).Length);
            _disk.Setup(x => x.FileGetLastWrite(It.IsAny<string>())).Returns((string path) => File.GetLastWriteTimeUtc(path));
            _disk.Setup(x => x.OpenReadStream(It.IsAny<string>())).Returns((string path) => File.OpenRead(path));
            _tracks.Setup(x => x.GetTracksByArtist(It.IsAny<int>())).Returns(new List<Track>());
            _tags.Setup(x => x.ReadTags(It.IsAny<string>())).Returns(new ParsedTrackInfo { Title = "Song (Radio Edit)", Duration = TimeSpan.FromSeconds(192) });
            _service = new LibraryAuditService(_files.Object, _tracks.Object, _tags.Object, _disk.Object, _repository.Object, _fingerprints.Object, _deletion.Object, LogManager.GetCurrentClassLogger());
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var path in _paths)
            {
                File.Delete(path);
            }
        }

        [Test]
        public void deletion_targets_only_the_audit_repository()
        {
            _service.DeleteFindings(new[] { 1, 2, 1 });
            _repository.Verify(x => x.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2 }))), Times.Once);
            _service.DeleteAllFindings();
            _repository.Verify(x => x.Purge(false), Times.Once);
            _deletion.VerifyNoOtherCalls();
            _files.VerifyNoOtherCalls();
            _tracks.VerifyNoOtherCalls();
            _tags.VerifyNoOtherCalls();
            _disk.VerifyNoOtherCalls();
        }

        [TestCase(null)]
        [TestCase(new int[0])]
        [TestCase(new[] { 0 })]
        [TestCase(new[] { 1, -1 })]
        public void deletion_rejects_invalid_selection(int[] ids)
        {
            Assert.Throws<BadRequestException>(() => _service.DeleteFindings(ids));
            _repository.VerifyNoOtherCalls();
        }

        private AuditFileDeletion SelectDuplicateFiles(params int[] fileIds)
        {
            _service.Execute(new ScanLibraryAuditCommand { Versions = false });
            var finding = _rows.Single();
            var selection = new AuditFileDeletion
            {
                Findings = new List<AuditFindingSelection> { new AuditFindingSelection { Id = finding.Id, EvidenceKey = finding.EvidenceKey } },
                FileIds = fileIds
            };
            if (fileIds.Length > 0)
            {
                selection.PreviewKey = _service.PreviewFindingFileDeletion(selection).PreviewKey;
            }

            return selection;
        }

        [Test]
        public void file_deletion_removes_only_explicit_copies_once()
        {
            var keep = Add();
            var remove = Add();
            var selection = SelectDuplicateFiles(remove.Id, remove.Id);
            _service.DeleteFindingFiles(selection);
            _deletion.Verify(x => x.DeleteTrackFile(remove, "Unmapped_Files"), Times.Once);
            _deletion.VerifyNoOtherCalls();
            _repository.Verify(x => x.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1 }))), Times.Once);
            Assert.That(File.Exists(keep.Path), Is.True);
        }

        [Test]
        public void mapped_file_deletion_uses_lidarr_artist_deletion_service()
        {
            Add();
            var remove = Add();
            remove.AlbumId = 12;
            var selection = SelectDuplicateFiles(remove.Id);
            _service.DeleteFindingFiles(selection);
            _deletion.Verify(x => x.DeleteTrackFile(remove.Artist.Value, remove), Times.Once);
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void stale_finding_cannot_delete_files()
        {
            Add();
            Add();
            var selection = SelectDuplicateFiles(1);
            selection.Findings[0].EvidenceKey = "stale";
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
            _repository.Verify(x => x.DeleteMany(It.IsAny<IEnumerable<int>>()), Times.Never);
        }

        [Test]
        public void unrelated_file_cannot_be_deleted_from_an_audit_finding()
        {
            Add();
            Add();
            var unrelated = Add("different content");
            var selection = SelectDuplicateFiles(1);
            selection.FileIds = new[] { 1, unrelated.Id };
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void changed_file_prevents_the_entire_deletion_selection()
        {
            Add();
            var changed = Add();
            var selection = SelectDuplicateFiles(1, 2);
            File.AppendAllText(changed.Path, "changed");
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void missing_or_moved_file_cannot_be_deleted_using_old_evidence()
        {
            Add();
            var moved = Add();
            var selection = SelectDuplicateFiles(moved.Id);
            moved.Path += ".moved";
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _library.Remove(moved);
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void failed_file_deletion_keeps_findings_and_stops_the_batch()
        {
            var first = Add();
            Add();
            var selection = SelectDuplicateFiles(1, 2);
            selection.ConfirmAllCopies = true;
            _deletion.Setup(x => x.DeleteTrackFile(first, "Unmapped_Files")).Throws(new IOException("Access denied"));
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.Verify(x => x.DeleteTrackFile(first, "Unmapped_Files"), Times.Once);
            _deletion.VerifyNoOtherCalls();
            _repository.Verify(x => x.DeleteMany(It.IsAny<IEnumerable<int>>()), Times.Never);
        }

        [Test]
        public void partial_file_deletion_failure_retains_findings_for_review()
        {
            var first = Add();
            var second = Add();
            var selection = SelectDuplicateFiles(1, 2);
            selection.ConfirmAllCopies = true;
            _deletion.Setup(x => x.DeleteTrackFile(second, "Unmapped_Files")).Throws(new IOException("Access denied"));
            var error = Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            Assert.That(error.Message, Does.Contain("Some selected files may already have been deleted"));
            _deletion.Verify(x => x.DeleteTrackFile(first, "Unmapped_Files"), Times.Once);
            _deletion.Verify(x => x.DeleteTrackFile(second, "Unmapped_Files"), Times.Once);
            _repository.Verify(x => x.DeleteMany(It.IsAny<IEnumerable<int>>()), Times.Never);
        }

        [Test]
        public void resolved_finding_cannot_delete_files()
        {
            Add();
            Add();
            var selection = SelectDuplicateFiles(1);
            _rows.Single().Status = "resolved";
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void file_deletion_requires_an_explicit_nonempty_selection()
        {
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(null));
            Add();
            Add();
            var selection = SelectDuplicateFiles();
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void preview_counts_unique_files_and_total_size_without_deleting()
        {
            Add();
            var remove = Add();
            var selection = SelectDuplicateFiles(remove.Id, remove.Id);
            var preview = _service.PreviewFindingFileDeletion(selection);
            Assert.That(preview.Files.Select(x => x.Path), Is.EqualTo(new[] { remove.Path }));
            Assert.That(preview.TotalBytes, Is.EqualTo(new FileInfo(remove.Path).Length));
            Assert.That(preview.DuplicateGroups, Is.Empty);
            _deletion.VerifyNoOtherCalls();
            _repository.Verify(x => x.DeleteMany(It.IsAny<IEnumerable<int>>()), Times.Never);
        }

        [Test]
        public void deleting_every_duplicate_requires_separate_confirmation()
        {
            Add();
            Add();
            var selection = SelectDuplicateFiles(1, 2);
            var preview = _service.PreviewFindingFileDeletion(selection);
            Assert.That(preview.DuplicateGroups.Count, Is.EqualTo(1));
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
            selection.ConfirmAllCopies = true;
            _service.DeleteFindingFiles(selection);
            _deletion.Verify(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), "Unmapped_Files"), Times.Exactly(2));
        }

        [Test]
        public void duplicate_protection_includes_findings_outside_the_selection()
        {
            Add();
            Add();
            var selection = SelectDuplicateFiles(1, 2);
            var duplicate = _rows.Single();
            duplicate.Status = "ignored";
            var version = new LibraryAuditFinding { Id = 2, EvidenceKey = "version-key", Kind = "version", Status = "pending", Evidence = duplicate.Evidence };
            _rows.Add(version);
            selection.Findings = new List<AuditFindingSelection> { new AuditFindingSelection { Id = version.Id, EvidenceKey = version.EvidenceKey } };
            var preview = _service.PreviewFindingFileDeletion(selection);
            Assert.That(preview.DuplicateGroups.Single().FindingId, Is.EqualTo(duplicate.Id));
            selection.PreviewKey = preview.PreviewKey;
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void file_deletion_requires_a_matching_preview()
        {
            Add();
            Add();
            var selection = SelectDuplicateFiles(1);
            selection.PreviewKey = null;
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            selection.PreviewKey = _service.PreviewFindingFileDeletion(selection).PreviewKey;
            selection.FileIds = new[] { 2 };
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void changed_duplicate_group_invalidates_preview_even_when_override_was_confirmed()
        {
            Add();
            Add();
            var selection = SelectDuplicateFiles(1, 2);
            selection.ConfirmAllCopies = true;
            _rows.Single().EvidenceKey = "new-group-evidence";
            selection.Findings[0].EvidenceKey = "new-group-evidence";
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void deleting_last_remaining_audited_copy_requires_confirmation()
        {
            var missing = Add();
            Add();
            var selection = SelectDuplicateFiles(2);
            File.Delete(missing.Path);
            var preview = _service.PreviewFindingFileDeletion(selection);
            Assert.That(preview.DuplicateGroups.Single().Files.Select(x => x.Id), Is.EqualTo(new[] { 2 }));
            selection.PreviewKey = preview.PreviewKey;
            Assert.Throws<BadRequestException>(() => _service.DeleteFindingFiles(selection));
            _deletion.VerifyNoOtherCalls();
        }

        [Test]
        public void cancelled_scan_does_not_publish_or_resolve_previous_findings()
        {
            Add();
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            var previous = _rows.Single();
            var updated = previous.Updated;
            _repository.Invocations.Clear();
            var command = new ScanLibraryAuditCommand { Duplicates = false };
            _tags.Setup(x => x.ReadTags(It.IsAny<string>())).Callback(() => command.RequestCancellation())
                .Returns(new ParsedTrackInfo { Title = "Song (Original Mix)", Duration = TimeSpan.FromSeconds(100) });
            Assert.Throws<OperationCanceledException>(() => _service.Execute(command));
            Assert.That(_rows.Single(), Is.SameAs(previous));
            Assert.That(previous.Updated, Is.EqualTo(updated));
            Assert.That(previous.Status, Is.EqualTo("pending"));
            _repository.Verify(x => x.Update(It.IsAny<LibraryAuditFinding>()), Times.Never);
            _repository.Verify(x => x.Insert(It.IsAny<LibraryAuditFinding>()), Times.Never);
        }

        [Test]
        public void cancelled_audit_before_start_does_not_read_library_files()
        {
            var command = new ScanLibraryAuditCommand();
            command.RequestCancellation();
            Assert.Throws<OperationCanceledException>(() => _service.Execute(command));
            _files.Verify(x => x.All(), Times.Never);
            _disk.VerifyNoOtherCalls();
        }

        private sealed class CancellingReadStream : FileStream
        {
            private readonly ScanLibraryAuditCommand _command;
            public int BytesRead { get; private set; }

            public CancellingReadStream(string path, ScanLibraryAuditCommand command)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            {
                _command = command;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = base.Read(buffer, offset, count);
                BytesRead += read;
                _command.RequestCancellation();
                return read;
            }
        }

        [Test]
        public void cancellation_during_hashing_stops_between_chunks_and_does_not_publish()
        {
            var first = Add(new string('a', 512 * 1024));
            var second = Add(new string('a', 512 * 1024));
            var command = new ScanLibraryAuditCommand { Versions = false };
            using var stream = new CancellingReadStream(first.Path, command);
            _disk.Setup(x => x.OpenReadStream(first.Path)).Returns(stream);
            Assert.Throws<OperationCanceledException>(() => _service.Execute(command));
            Assert.That(stream.BytesRead, Is.EqualTo(128 * 1024));
            _disk.Verify(x => x.OpenReadStream(second.Path), Times.Never);
            _repository.Verify(x => x.Insert(It.IsAny<LibraryAuditFinding>()), Times.Never);
        }

        private TrackFile Add(string content = "same bytes")
        {
            var path = Path.Combine(Path.GetTempPath(), $"lidarr-audit-{Guid.NewGuid():N}.flac");
            File.WriteAllText(path, content);
            _paths.Add(path);
            var file = new TrackFile
            {
                Id = _library.Count + 1,
                Path = path,
                Artist = new Artist { Id = 1 },
                Tracks = new List<Track> { new Track { Title = "Song (Extended Mix)", Duration = 348000, ForeignRecordingId = "extended" } }
            };
            _library.Add(file);
            return file;
        }

        private static AuditEvidence Evidence(LibraryAuditFinding finding) => JsonConvert.DeserializeObject<AuditEvidence>(finding.Evidence);

        [Test]
        public void duplicates_require_identical_bytes_not_just_size()
        {
            Add("abcd");
            Add("abcd");
            Add("wxyz");
            _service.Execute(new ScanLibraryAuditCommand { Versions = false });
            var duplicate = _rows.Single(x => x.Kind == "duplicate");
            Assert.That(Evidence(duplicate).Files.Select(x => x.Id), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Evidence(duplicate).Confidence, Is.EqualTo("exact"));
            _tags.Verify(x => x.ReadTags(It.IsAny<string>()), Times.Never);
            _files.Verify(x => x.Update(It.IsAny<TrackFile>()), Times.Never);
            _files.Verify(x => x.Delete(It.IsAny<TrackFile>()), Times.Never);
        }

        [Test]
        public void unique_sizes_are_not_hashed()
        {
            Add("a");
            Add("bb");
            _service.Execute(new ScanLibraryAuditCommand { Versions = false });
            Assert.That(_rows, Is.Empty);
            _disk.Verify(x => x.OpenReadStream(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void ignored_duplicate_stays_ignored_when_scan_options_change()
        {
            Add();
            Add();
            _service.Execute(new ScanLibraryAuditCommand { Versions = false });
            var duplicate = _rows.Single();
            _service.Decide(duplicate.Id, duplicate.EvidenceKey, "ignore");
            _service.Execute(new ScanLibraryAuditCommand());
            Assert.That(_rows.Count(x => x.Kind == "duplicate"), Is.EqualTo(1));
            Assert.That(duplicate.Status, Is.EqualTo("ignored"));
            _service.Decide(duplicate.Id, duplicate.EvidenceKey, "restore");
            Assert.That(duplicate.Status, Is.EqualTo("pending"));
        }

        [Test]
        public void changed_evidence_returns_to_review_and_old_finding_resolves()
        {
            Add();
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            var previous = _rows.Single();
            _service.Decide(previous.Id, previous.EvidenceKey, "ignore");
            File.AppendAllText(_library[0].Path, "changed");
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            Assert.That(previous.Status, Is.EqualTo("resolved"));
            Assert.That(_rows.Count(x => x.Status == "pending"), Is.EqualTo(1));
        }

        [Test]
        public void partial_scan_does_not_resolve_unselected_findings()
        {
            Add();
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            var version = _rows.Single();
            _service.Execute(new ScanLibraryAuditCommand { Versions = false });
            Assert.That(version.Status, Is.EqualTo("pending"));
        }

        [Test]
        public void unreadable_file_preserves_previous_evidence_and_reports_error()
        {
            Add();
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            var previous = _rows.Single();
            File.Delete(_library[0].Path);
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            Assert.That(previous.Status, Is.EqualTo("pending"));
            Assert.That(_rows.Any(x => x.Kind == "versionError"), Is.True);
        }

        [Test]
        public void unreadable_tags_do_not_prevent_exact_duplicate_scan()
        {
            Add();
            Add();
            _tags.Setup(x => x.ReadTags(It.IsAny<string>())).Throws(new IOException("Corrupt tags"));
            _service.Execute(new ScanLibraryAuditCommand());
            Assert.That(_rows.Count(x => x.Kind == "versionError"), Is.EqualTo(2));
            Assert.That(_rows.Count(x => x.Kind == "duplicate"), Is.EqualTo(1));
        }

        [Test]
        public void changed_file_during_read_is_an_error_not_a_version_finding()
        {
            var file = Add();
            _tags.Setup(x => x.ReadTags(file.Path)).Returns(() =>
            {
                File.AppendAllText(file.Path, "changed");
                return new ParsedTrackInfo { Title = "Song (Radio Edit)", Duration = TimeSpan.FromSeconds(192) };
            });
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            Assert.That(_rows.Single().Kind, Is.EqualTo("versionError"));
        }

        [Test]
        public void stale_decisions_and_unknown_actions_are_rejected()
        {
            Add();
            _service.Execute(new ScanLibraryAuditCommand());
            var row = _rows.Single();
            Assert.Throws<BadRequestException>(() => _service.Decide(row.Id, "outdated", "ignore"));
            Assert.Throws<BadRequestException>(() => _service.Decide(row.Id, row.EvidenceKey, "delete"));
            Assert.That(row.Status, Is.EqualTo("pending"));
        }

        [Test]
        public void no_scan_types_and_unavailable_fpcalc_are_rejected_before_publication()
        {
            Assert.Throws<BadRequestException>(() => _service.Execute(new ScanLibraryAuditCommand { Versions = false, Duplicates = false }));
            Assert.Throws<BadRequestException>(() => _service.Execute(new ScanLibraryAuditCommand { Fingerprints = true }));
            Assert.That(_rows, Is.Empty);
        }

        [Test]
        public void deep_scan_reports_missing_fingerprint_evidence()
        {
            Add();
            _fingerprints.Setup(x => x.IsSetup()).Returns(true);
            _service.Execute(new ScanLibraryAuditCommand { Fingerprints = true });
            Assert.That(_rows.Any(x => x.Kind == "fingerprintError"), Is.True);
        }

        [Test]
        public void local_scan_does_not_resolve_previous_fingerprint_supported_finding()
        {
            Add();
            _rows.Add(new LibraryAuditFinding
            {
                Id = 1, Kind = "version", Status = "pending", EvidenceKey = "audio-evidence",
                Evidence = JsonConvert.SerializeObject(new AuditEvidence { Confidence = "supported" })
            });
            _service.Execute(new ScanLibraryAuditCommand { Duplicates = false });
            Assert.That(_rows.Single(x => x.EvidenceKey == "audio-evidence").Status, Is.EqualTo("pending"));
        }

        [Test]
        public void failed_snapshot_preserves_previous_duplicate_finding()
        {
            Add();
            var second = Add();
            _service.Execute(new ScanLibraryAuditCommand { Versions = false });
            var previous = _rows.Single();
            File.Delete(second.Path);
            _service.Execute(new ScanLibraryAuditCommand { Versions = false });
            Assert.That(previous.Status, Is.EqualTo("pending"));
            Assert.That(_rows.Any(x => x.Kind == "duplicateError"), Is.True);
        }

        [Test]
        public void broken_fingerprint_process_does_not_skip_local_version_checks()
        {
            Add();
            _fingerprints.Setup(x => x.IsSetup()).Returns(true);
            _fingerprints.Setup(x => x.Lookup(It.IsAny<List<LocalTrack>>(), It.IsAny<double>())).Throws(new IOException("Cannot launch fpcalc"));
            _service.Execute(new ScanLibraryAuditCommand { Fingerprints = true });
            Assert.That(_rows.Any(x => x.Kind == "fingerprintError"), Is.True);
            Assert.That(_rows.Any(x => x.Kind == "version"), Is.True);
        }
    }
}
