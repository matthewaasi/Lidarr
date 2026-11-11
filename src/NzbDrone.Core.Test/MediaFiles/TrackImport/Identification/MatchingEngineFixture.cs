using System;
using System.Collections.Generic;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Aggregation;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class MatchingEngineFixture
    {
        [TestCase(80, false)]
        [TestCase(75, true)]
        [TestCase(70, true)]
        public void original_album_threshold_should_be_configurable(int minimum, bool accepted)
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(x => x.OriginalAlbumMatchPercent).Returns(minimum);
            config.SetupGet(x => x.OriginalTrackMatchPercent).Returns(60);
            var distance = new Distance();
            distance.Add("album", 0.25);
            var local = new LocalAlbumRelease(new List<LocalTrack> { new LocalTrack { Distance = new Distance() } }) { Distance = distance, NewDownload = true };
            Assert.That(new CloseAlbumMatchSpecification(config.Object, LogManager.GetLogger("test")).IsSatisfiedBy(local, null).Accepted, Is.EqualTo(accepted));
        }

        [TestCase(60, true)]
        [TestCase(70, true)]
        [TestCase(80, false)]
        public void original_track_threshold_should_be_configurable(int minimum, bool accepted)
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(x => x.OriginalTrackMatchPercent).Returns(minimum);
            var distance = new Distance();
            distance.Add("track_title", 0.30);
            Assert.That(new CloseTrackMatchSpecification(config.Object, LogManager.GetLogger("test")).IsSatisfiedBy(new LocalTrack { Distance = distance }, null).Accepted, Is.EqualTo(accepted));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void original_engine_must_not_call_lm_studio_even_with_no_candidates(bool noCandidates)
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(x => x.MatchingEngine).Returns("Original");
            config.SetupGet(x => x.AllowFingerprinting).Returns(AllowFingerprinting.Never);
            var artist = new Artist { Metadata = new ArtistMetadata { Name = "Artist", ForeignArtistId = "artist" } };
            var album = new Album { Id = 10, Title = "Album", Artist = artist, ArtistMetadata = artist.Metadata };
            var track = new Track { Id = 1, Title = "Song", Duration = 180000, AlbumReleaseId = 20 };
            var release = new AlbumRelease { Id = 20, AlbumId = 10, Title = "Album", Album = album, TrackCount = 1, Tracks = new List<Track> { track } };
            var candidates = noCandidates ? new List<CandidateAlbumRelease>() : new List<CandidateAlbumRelease> { new CandidateAlbumRelease(release) };
            var candidateService = new Mock<ICandidateService>();
            candidateService.Setup(x => x.GetDbCandidatesFromTags(It.IsAny<LocalAlbumRelease>(), It.IsAny<IdentificationOverrides>(), false)).Returns(candidates);
            var tracks = new Mock<ITrackService>();
            tracks.Setup(x => x.GetTracksByReleases(It.IsAny<List<int>>())).Returns(new List<Track>());
            var lm = new Mock<ILmStudioMatcher>(MockBehavior.Strict);
            var service = new IdentificationService(tracks.Object, new Mock<ITrackGroupingService>().Object, new Mock<IFingerprintingService>().Object, new Mock<IAudioTagService>().Object, new Mock<IAugmentingService>().Object, candidateService.Object, config.Object, lm.Object, LogManager.GetLogger("test"));
            var local = new LocalTrack
            {
                Path = "album/song.flac",
                AiMatch = new AiMatchResult { Accepted = true },
                FileTrackInfo = new ParsedTrackInfo
                {
                    Title = "Song", CleanTitle = "Song", AlbumTitle = "Album", ArtistTitle = "Artist", Duration = TimeSpan.FromSeconds(180)
                }
            };
            var result = service.Identify(new List<LocalTrack> { local }, new IdentificationOverrides(), new ImportDecisionMakerConfig { SingleRelease = true });
            lm.Verify(x => x.Identify(It.IsAny<LocalAlbumRelease>(), It.IsAny<List<CandidateAlbumRelease>>(), It.IsAny<List<LocalTrack>>()), Times.Never());
            Assert.That(local.AiMatch, Is.Null);
            if (!noCandidates)
            {
                Assert.That(result[0].AlbumRelease.Id, Is.EqualTo(20));
                Assert.That(local.Tracks[0].Id, Is.EqualTo(1));
            }
        }

        [Test]
        public void saved_lm_studio_settings_should_override_environment_defaults()
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(x => x.LmStudioBaseUrl).Returns("http://localhost:1234/v1");
            config.SetupGet(x => x.LmStudioModel).Returns("selected-model");
            config.SetupGet(x => x.LmStudioMode).Returns("Automatic");
            config.SetupGet(x => x.LmStudioConfidencePercent).Returns(97);
            config.Setup(x => x.IsDefined("LmStudioConfidencePercent")).Returns(true);
            var settings = new LmStudioSettingsProvider(config.Object).GetSettings();
            Assert.That(settings.BaseUrl, Is.EqualTo("http://localhost:1234/v1"));
            Assert.That(settings.Model, Is.EqualTo("selected-model"));
            Assert.That(settings.Automatic, Is.True);
            Assert.That(settings.MinimumConfidence, Is.EqualTo(0.97));
        }
    }
}
