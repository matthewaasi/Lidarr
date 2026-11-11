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
            _tracks = new Mock<ITrackService>();
            _tags = new Mock<IAudioTagService>();
            _disk = new Mock<IDiskProvider>();
            _repository = new Mock<ILibraryAuditRepository>();
            _fingerprints = new Mock<IFingerprintingService>();
            _library = new List<TrackFile>();
            _rows = new List<LibraryAuditFinding>();
            _paths = new List<string>();
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
            _disk.Setup(x => x.GetFileSize(It.IsAny<string>())).Returns((string path) => new FileInfo(path).Length);
            _disk.Setup(x => x.FileGetLastWrite(It.IsAny<string>())).Returns((string path) => File.GetLastWriteTimeUtc(path));
            _disk.Setup(x => x.OpenReadStream(It.IsAny<string>())).Returns((string path) => File.OpenRead(path));
            _tracks.Setup(x => x.GetTracksByArtist(It.IsAny<int>())).Returns(new List<Track>());
            _tags.Setup(x => x.ReadTags(It.IsAny<string>())).Returns(new ParsedTrackInfo { Title = "Song (Radio Edit)", Duration = TimeSpan.FromSeconds(192) });
            _service = new LibraryAuditService(_files.Object, _tracks.Object, _tags.Object, _disk.Object, _repository.Object, _fingerprints.Object, LogManager.GetCurrentClassLogger());
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var path in _paths)
            {
                File.Delete(path);
            }
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
