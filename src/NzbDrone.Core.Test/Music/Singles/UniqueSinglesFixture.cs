using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Newtonsoft.Json;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Music.Singles;

namespace NzbDrone.Core.Test.MusicTests.Singles
{
    [TestFixture]
    public class UniqueSinglesFixture
    {
        private Mock<IConfigService> _config;
        private Mock<IArtistService> _artists;
        private Mock<IAlbumService> _albums;
        private Mock<IReleaseService> _releases;
        private Mock<ITrackService> _tracks;
        private Mock<IMediaFileService> _files;
        private Mock<IDeleteMediaFiles> _delete;
        private Mock<IDiskProvider> _disk;
        private Mock<ISinglesDecisionRepository> _repository;
        private Mock<IManageCommandQueue> _commands;
        private UniqueSinglesService _service;
        private Album _single;
        private Album _album;
        private Artist _artist;
        private List<Track> _trackData;
        private List<TrackFile> _fileData;
        private List<SingleDecision> _rows;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigService>();
            _config.SetupProperty(x => x.UniqueSinglesEnabled, true);
            _config.SetupProperty(x => x.UniqueSinglesTitleDuration, true);
            _config.SetupProperty(x => x.UniqueSinglesToleranceMs, 3000);
            _artists = new Mock<IArtistService>();
            _albums = new Mock<IAlbumService>();
            _releases = new Mock<IReleaseService>();
            _tracks = new Mock<ITrackService>();
            _files = new Mock<IMediaFileService>();
            _delete = new Mock<IDeleteMediaFiles>();
            _disk = new Mock<IDiskProvider>();
            _repository = new Mock<ISinglesDecisionRepository>();
            _commands = new Mock<IManageCommandQueue>();
            _artist = new Artist { Id = 1, Monitored = true, Path = "/music/artist", Metadata = new ArtistMetadata { Name = "Artist" } };
            _single = new Album { Id = 10, AlbumType = "Single", Title = "Single", Monitored = true, Artist = _artist };
            _album = new Album { Id = 20, AlbumType = "Album", Title = "Album", Monitored = true, Artist = _artist };
            _trackData = new List<Track>
            {
                new Track { Id = 1, AlbumReleaseId = 100, Title = "Song", Duration = 180000, ForeignRecordingId = "recording", TrackFileId = 11 },
                new Track { Id = 2, AlbumReleaseId = 200, Title = "Song", Duration = 180000, ForeignRecordingId = "recording", TrackFileId = 22 }
            };
            _fileData = new List<TrackFile>
            {
                new TrackFile { Id = 11, AlbumId = 10, Path = "/music/artist/single/song.flac", Size = 100 },
                new TrackFile { Id = 22, AlbumId = 20, Path = "/music/artist/album/song.flac", Size = 100 }
            };
            _rows = new List<SingleDecision>();
            _artists.Setup(x => x.GetAllArtists()).Returns(() => new List<Artist> { _artist });
            _artists.Setup(x => x.GetArtist(1)).Returns(_artist);
            _albums.Setup(x => x.GetAlbumsByArtist(1)).Returns(() => new List<Album> { _single, _album });
            _albums.Setup(x => x.GetAlbum(It.IsAny<int>())).Returns<int>(id => id == 10 ? _single : _album);
            _albums.Setup(x => x.SetAlbumMonitored(It.IsAny<int>(), It.IsAny<bool>())).Callback<int, bool>((id, monitored) =>
            {
                var album = id == 10 ? _single : _album;
                album.Monitored = monitored;
                _service.Handle(new AlbumEditedEvent(album, album));
            });
            _releases.Setup(x => x.GetReleasesByAlbum(It.IsAny<int>())).Returns<int>(id => new List<AlbumRelease>
            {
                new AlbumRelease { Id = id * 10, AlbumId = id, Monitored = true, TrackCount = _trackData.Count(t => t.AlbumReleaseId == id * 10) }
            });
            _tracks.Setup(x => x.GetTracksByReleases(It.IsAny<List<int>>())).Returns<List<int>>(ids => _trackData.Where(t => ids.Contains(t.AlbumReleaseId)).ToList());
            _tracks.Setup(x => x.GetTrack(It.IsAny<int>())).Returns<int>(id => _trackData.SingleOrDefault(t => t.Id == id));
            _files.Setup(x => x.GetFilesByArtist(1)).Returns(() => _fileData.ToList());
            _files.Setup(x => x.Get(It.IsAny<int>())).Returns<int>(id => _fileData.SingleOrDefault(f => f.Id == id));
            _disk.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            _disk.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
            _repository.Setup(x => x.ForArtist(1)).Returns(() => _rows.ToList());
            _repository.Setup(x => x.ForAlbum(It.IsAny<int>())).Returns<int>(id => _rows.SingleOrDefault(r => r.AlbumId == id));
            _repository.Setup(x => x.Get(It.IsAny<int>())).Returns<int>(id => _rows.Single(r => r.Id == id));
            _repository.Setup(x => x.Insert(It.IsAny<SingleDecision>())).Returns<SingleDecision>(row =>
            {
                row.Id = _rows.Count + 1;
                _rows.Add(row);
                return row;
            });
            _delete.Setup(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>())).Callback<TrackFile, string>((file, folder) =>
            {
                Assert.That(_single.Monitored, Is.False, "Unmonitor before deleting");
                Assert.That(_rows.Single().AutoUnmonitored, Is.True, "Persist ownership before deleting");
                _fileData.Remove(file);
                _trackData.Where(t => t.TrackFileId == file.Id).ToList().ForEach(t => t.TrackFileId = 0);
            });
            CreateService();
        }

        private void CreateService()
        {
            _service = new UniqueSinglesService(_config.Object, _artists.Object, _albums.Object, _releases.Object, _tracks.Object, _files.Object, _delete.Object, _disk.Object, _repository.Object, _commands.Object, LogManager.GetLogger("SinglesTest"));
        }

        private void Scan() => _service.Execute(new ScanUniqueSinglesCommand());
        private SinglesEvidence Evidence() => JsonConvert.DeserializeObject<SinglesEvidence>(_rows.Single().Evidence);

        [Test]
        public void exact_match_should_unmonitor_and_delete_only_the_single()
        {
            Scan();
            Assert.That(_single.Monitored, Is.False);
            Assert.That(_album.Monitored, Is.True);
            Assert.That(_rows.Single().Status, Is.EqualTo("cleaned"));
            Assert.That(_fileData.Select(f => f.Id), Is.EqualTo(new[] { 22 }));
        }

        [Test]
        public void off_should_not_scan_delete_or_restore_monitoring()
        {
            Scan();
            _config.Object.UniqueSinglesEnabled = false;
            _fileData.Clear();
            CreateService();
            Scan();
            Assert.That(_single.Monitored, Is.False);
            _artists.Verify(x => x.GetAllArtists(), Times.Once());
        }

        [Test]
        public void exclusive_b_side_should_protect_the_whole_single()
        {
            _trackData.Add(new Track { Id = 3, AlbumReleaseId = 100, Title = "Exclusive B side", Duration = 160000, TrackFileId = 33 });
            _fileData.Add(new TrackFile { Id = 33, AlbumId = 10, Path = "/music/artist/single/b.flac" });
            Scan();
            Assert.That(_rows, Is.Empty);
            Assert.That(_single.Monitored, Is.True);
            _delete.Verify(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase("title-only")]
        [TestCase("version-conflict")]
        [TestCase("duration-conflict")]
        [TestCase("strict-recording-id")]
        public void uncertain_matches_should_enter_persistent_review_without_deleting(string kind)
        {
            if (kind == "title-only")
            {
                _trackData[0].ForeignRecordingId = "other";
                _trackData[0].Duration = 0;
            }
            else if (kind == "version-conflict")
            {
                _trackData[0].Title = "Song (Radio Edit)";
            }
            else if (kind == "duration-conflict")
            {
                _trackData[0].Duration = 300000;
            }
            else
            {
                _trackData[0].ForeignRecordingId = "other";
                _config.Object.UniqueSinglesTitleDuration = false;
            }

            Scan();
            CreateService();
            Scan();
            Assert.That(_rows.Single().Status, Is.EqualTo("pending"));
            Assert.That(_single.Monitored, Is.True);
            Assert.That(Evidence().CanCleanup, Is.True);
            _delete.Verify(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void title_duration_should_support_plugin_compatible_cleanup()
        {
            _trackData[0].ForeignRecordingId = "other";
            _trackData[0].Duration += 2500;
            Scan();
            Assert.That(_rows.Single().Status, Is.EqualTo("cleaned"));
            Assert.That(Evidence().Tracks.Single().Tier, Is.EqualTo(2));
        }

        private void Review()
        {
            _trackData[0].ForeignRecordingId = "other";
            _trackData[0].Duration = 0;
            Scan();
        }

        [Test]
        public void approval_should_revalidate_and_cleanup_an_uncertain_match()
        {
            Review();
            var row = _rows.Single();
            _service.Decide(row.Id, "approve", Evidence().Fingerprint);
            Assert.That(row.Status, Is.EqualTo("cleaned"));
            Assert.That(_single.Monitored, Is.False);
        }

        [Test]
        public void changed_evidence_should_reject_stale_approval()
        {
            Review();
            var fingerprint = Evidence().Fingerprint;
            _fileData[0].Size++;
            Assert.Throws<NzbDroneClientException>(() => _service.Decide(_rows.Single().Id, "approve", fingerprint));
            Assert.That(_single.Monitored, Is.True);
            _delete.Verify(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void keep_should_survive_scans_and_restart()
        {
            Review();
            _service.Decide(_rows.Single().Id, "keep", null);
            _trackData[0].ForeignRecordingId = "recording";
            CreateService();
            Scan();
            Assert.That(_rows.Single().Status, Is.EqualTo("kept"));
            Assert.That(_single.Monitored, Is.True);
        }

        [TestCase("files")]
        [TestCase("monitoring")]
        public void lost_coverage_should_restore_only_owned_monitoring(string loss)
        {
            Scan();
            if (loss == "files")
            {
                _fileData.Clear();
            }
            else
            {
                _album.Monitored = false;
            }

            CreateService();
            Scan();
            Assert.That(_single.Monitored, Is.True);
            Assert.That(_rows.Single().Status, Is.EqualTo("restored"));
            Assert.That(_rows.Single().AutoUnmonitored, Is.False);
        }

        [Test]
        public void manual_monitoring_choice_should_relinquish_automatic_ownership()
        {
            Scan();
            _service.Handle(new AlbumEditedEvent(_single, _single));
            _fileData.Clear();
            Scan();
            Assert.That(_rows.Single().Status, Is.EqualTo("kept"));
            Assert.That(_single.Monitored, Is.False);
        }

        [Test]
        public void unmonitored_single_must_not_be_taken_over()
        {
            _single.Monitored = false;
            Scan();
            Assert.That(_rows, Is.Empty);
            _delete.Verify(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void missing_physical_cover_file_should_not_authorize_cleanup()
        {
            _disk.Setup(x => x.FileExists(_fileData[1].Path)).Returns(false);
            Scan();
            Assert.That(_single.Monitored, Is.True);
            Assert.That(_rows, Is.Empty);
        }

        [Test]
        public void unmapped_files_should_block_approval()
        {
            _fileData.Add(new TrackFile { Id = 33, AlbumId = 10, Path = "/music/artist/single/unknown.flac" });
            Scan();
            Assert.That(_rows.Single().Status, Is.EqualTo("pending"));
            Assert.That(Evidence().CanCleanup, Is.False);
            Assert.Throws<NzbDroneClientException>(() => _service.Decide(_rows.Single().Id, "approve", Evidence().Fingerprint));
        }

        [Test]
        public void deleting_the_covering_album_should_queue_a_check_and_restore_monitoring()
        {
            Scan();
            _albums.Setup(x => x.GetAlbumsByArtist(1)).Returns(new List<Album> { _single });
            _service.Handle(new NzbDrone.Core.Music.Events.AlbumDeletedEvent(_album, true, false));
            _commands.Verify(x => x.Push(It.Is<ScanUniqueSinglesCommand>(c => c.ArtistId == 1), CommandPriority.Normal, CommandTrigger.Unspecified), Times.Once());
            Scan();
            Assert.That(_single.Monitored, Is.True);
            Assert.That(_rows.Single().Status, Is.EqualTo("restored"));
        }

        [Test]
        public void another_downloaded_album_should_continue_covering_the_single()
        {
            Scan();
            _album.Id = 30;
            _trackData[1].AlbumReleaseId = 300;
            _trackData[1].TrackFileId = 44;
            _fileData[0].Id = 44;
            _fileData[0].AlbumId = 30;
            _fileData[0].Path = "/music/artist/other-album/song.flac";
            Scan();
            Assert.That(_single.Monitored, Is.False);
            Assert.That(_rows.Single().AutoUnmonitored, Is.True);
        }

        [Test]
        public void disabled_feature_should_reject_review_approval()
        {
            Review();
            _config.Object.UniqueSinglesEnabled = false;
            Assert.Throws<NzbDroneClientException>(() => _service.Decide(_rows.Single().Id, "approve", Evidence().Fingerprint));
            _delete.Verify(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void live_album_must_not_authorize_single_cleanup()
        {
            _album.SecondaryTypes.Add(SecondaryAlbumType.Live);
            Scan();
            Assert.That(_rows, Is.Empty);
            Assert.That(_single.Monitored, Is.True);
        }

        [Test]
        public void shared_physical_path_should_block_cleanup()
        {
            _fileData[0].Path = _fileData[1].Path;
            Scan();
            Assert.That(_rows.Single().Status, Is.EqualTo("pending"));
            Assert.That(Evidence().CanCleanup, Is.False);
        }

        [Test]
        public void failed_delete_should_remain_visible_and_not_repeat_automatically()
        {
            _delete.Setup(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>())).Throws(new InvalidOperationException("Test deletion failure"));
            Assert.Throws<InvalidOperationException>(Scan);
            Assert.That(_rows.Single().Status, Is.EqualTo("error"));
            Scan();
            _delete.Verify(x => x.DeleteTrackFile(It.IsAny<TrackFile>(), It.IsAny<string>()), Times.Once());
        }
    }
}
