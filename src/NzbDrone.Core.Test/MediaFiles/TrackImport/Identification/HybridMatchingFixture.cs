using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Aggregation;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class HybridMatchingFixture
    {
        private Mock<IConfigService> _config;
        private Mock<ILmStudioMatcher> _lm;
        private Mock<ICandidateService> _candidates;
        private IdentificationService _service;
        private LocalTrack _file;
        private LocalAlbumRelease _release;
        private AlbumRelease _albumRelease;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigService>();
            _config.SetupGet(c => c.MatchingEngine).Returns("Hybrid");
            _config.SetupGet(c => c.AllowFingerprinting).Returns(AllowFingerprinting.Never);
            _config.SetupGet(c => c.OriginalAlbumMatchPercent).Returns(80);
            _config.SetupGet(c => c.OriginalTrackMatchPercent).Returns(60);
            _lm = new Mock<ILmStudioMatcher>();
            var artist = new Artist { Metadata = new ArtistMetadata { Name = "Artist", ForeignArtistId = "artist" } };
            var album = new Album { Id = 10, Title = "Album", Artist = artist, ArtistMetadata = artist.Metadata };
            var track = new Track { Id = 1, Title = "Song", Duration = 180000, AlbumReleaseId = 20, TrackNumber = "1", AbsoluteTrackNumber = 1, MediumNumber = 1 };
            _albumRelease = new AlbumRelease { Id = 20, AlbumId = 10, Title = "Album", Album = album, TrackCount = 1, Tracks = new List<Track> { track } };
            _file = new LocalTrack { Path = "album/song.flac", FileTrackInfo = new ParsedTrackInfo { Title = "Song", CleanTitle = "Song", AlbumTitle = "Album", ArtistTitle = "Artist", Duration = TimeSpan.FromSeconds(180), TrackNumbers = new[] { 1 }, DiscNumber = 1 } };
            _release = new LocalAlbumRelease(new List<LocalTrack> { _file });
            _candidates = new Mock<ICandidateService>();
            _candidates.Setup(c => c.GetDbCandidatesFromTags(It.IsAny<LocalAlbumRelease>(), It.IsAny<IdentificationOverrides>(), false))
                .Returns(new List<CandidateAlbumRelease> { new CandidateAlbumRelease(_albumRelease) });
            var tracks = new Mock<ITrackService>();
            tracks.Setup(t => t.GetTracksByReleases(It.IsAny<List<int>>())).Returns(new List<Track>());
            _service = new IdentificationService(tracks.Object, new Mock<ITrackGroupingService>().Object, new Mock<IFingerprintingService>().Object,
                new Mock<IAudioTagService>().Object, new Mock<IAugmentingService>().Object, _candidates.Object, _config.Object, _lm.Object, LogManager.GetLogger("hybrid-test"));
        }

        private void GivenPolicyMatch(double albumDistance = 0, double trackDistance = 0)
        {
            var distance = new Distance();
            distance.Add("album", albumDistance);
            var trackScore = new Distance();
            trackScore.Add("track_title", trackDistance);
            _release.AlbumRelease = _albumRelease;
            _release.Distance = distance;
            _release.TrackMapping = new TrackMapping { LocalExtra = new List<LocalTrack>(), MBExtra = new List<Track>() };
            _release.TrackMapping.Mapping[_file] = Tuple.Create(_albumRelease.Tracks.Value[0], trackScore);
        }

        [TestCase(0.20, 0.40, true)]
        [TestCase(0.200001, 0.10, false)]
        [TestCase(0.21, 0.10, false)]
        [TestCase(0.10, 0.41, false)]
        public void policy_obeys_album_and_track_boundaries(double albumDistance, double trackDistance, bool accepted)
        {
            GivenPolicyMatch(albumDistance, trackDistance);
            Assert.That(NativeMatchPolicy.IsAccepted(_release, true, _config.Object, LogManager.GetLogger("test")), Is.EqualTo(accepted));
        }

        [Test]
        public void policy_uses_configured_native_thresholds()
        {
            GivenPolicyMatch(0.25, 0.1);
            _config.SetupGet(c => c.OriginalAlbumMatchPercent).Returns(75);
            Assert.That(NativeMatchPolicy.IsAccepted(_release, true, _config.Object, LogManager.GetLogger("test")), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void missing_tracks_fail_for_new_downloads_but_allow_existing_partial_libraries(bool newDownload)
        {
            GivenPolicyMatch();
            _release.TrackMapping.MBExtra.Add(new Track { Id = 2 });
            Assert.That(NativeMatchPolicy.IsAccepted(_release, newDownload, _config.Object, LogManager.GetLogger("test")), Is.EqualTo(!newDownload));
        }

        [Test]
        public void unmatched_input_file_fails_without_mutating_existing_assignments()
        {
            GivenPolicyMatch();
            _release.LocalTracks.Add(new LocalTrack { Path = "extra.flac" });
            Assert.That(NativeMatchPolicy.IsAccepted(_release, true, _config.Object, LogManager.GetLogger("test")), Is.False);
            Assert.That(_file.Release, Is.Null);
            Assert.That(_file.AiMatch, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void strong_native_match_never_calls_ai_even_in_preview(bool preview)
        {
            var result = _service.Identify(new List<LocalTrack> { _file }, new IdentificationOverrides(), new ImportDecisionMakerConfig { SingleRelease = true, NewDownload = true, UseCachedIdentificationOnly = preview });
            _lm.Verify(l => l.Identify(It.IsAny<LocalAlbumRelease>(), It.IsAny<List<CandidateAlbumRelease>>(), It.IsAny<List<LocalTrack>>()), Times.Never());
            Assert.That(result[0].AlbumRelease.Id, Is.EqualTo(20));
            Assert.That(_file.Tracks.Single().Id, Is.EqualTo(1));
            Assert.That(_file.AiMatch, Is.Null);
        }

        [Test]
        public void native_fingerprinting_can_resolve_a_release_without_ai()
        {
            _config.SetupGet(c => c.AllowFingerprinting).Returns(AllowFingerprinting.AllFiles);
            _candidates.Setup(c => c.GetDbCandidatesFromTags(It.IsAny<LocalAlbumRelease>(), It.IsAny<IdentificationOverrides>(), false)).Returns(new List<CandidateAlbumRelease>());
            _candidates.Setup(c => c.GetDbCandidatesFromFingerprint(It.IsAny<LocalAlbumRelease>(), It.IsAny<IdentificationOverrides>(), false))
                .Returns(new List<CandidateAlbumRelease> { new CandidateAlbumRelease(_albumRelease) });
            _service.Identify(new List<LocalTrack> { _file }, new IdentificationOverrides(), new ImportDecisionMakerConfig { SingleRelease = true, NewDownload = true });
            _lm.Verify(l => l.Identify(It.IsAny<LocalAlbumRelease>(), It.IsAny<List<CandidateAlbumRelease>>(), It.IsAny<List<LocalTrack>>()), Times.Never());
            Assert.That(_file.Tracks.Single().Id, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void absent_candidates_fall_back_only_on_fresh_matching(bool preview)
        {
            _candidates.Setup(c => c.GetDbCandidatesFromTags(It.IsAny<LocalAlbumRelease>(), It.IsAny<IdentificationOverrides>(), false)).Returns(new List<CandidateAlbumRelease>());
            _service.Identify(new List<LocalTrack> { _file }, new IdentificationOverrides(), new ImportDecisionMakerConfig { SingleRelease = true, NewDownload = true, UseCachedIdentificationOnly = preview });
            _lm.Verify(l => l.Identify(It.IsAny<LocalAlbumRelease>(), It.IsAny<List<CandidateAlbumRelease>>(), It.IsAny<List<LocalTrack>>()), preview ? Times.Never() : Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void incomplete_release_sends_whole_input_to_ai_only_on_fresh_matching(bool preview)
        {
            _albumRelease.Tracks.Value.Add(new Track { Id = 2, AlbumReleaseId = 20, Title = "B-side", Duration = 180000 });
            _albumRelease.TrackCount = 2;
            _service.Identify(new List<LocalTrack> { _file }, new IdentificationOverrides(), new ImportDecisionMakerConfig { SingleRelease = true, NewDownload = true, UseCachedIdentificationOnly = preview });
            _lm.Verify(l => l.Identify(It.Is<LocalAlbumRelease>(r => r.LocalTracks.Count == 1 && r.LocalTracks[0] == _file), It.IsAny<List<CandidateAlbumRelease>>(), It.IsAny<List<LocalTrack>>()), preview ? Times.Never() : Times.Once());
        }
    }
}
