using System.Collections.Generic;
using Lidarr.Api.V1.Albums;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ArtistStats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Music;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Albums
{
    [TestFixture]
    public class AlbumStatisticsMappingFixture
    {
        private sealed class TestController : AlbumControllerWithSignalR
        {
            public TestController(IArtistStatisticsService statistics)
                : base(Mock.Of<IAlbumService>(), statistics, Mock.Of<IMapCoversToLocal>(), Mock.Of<IUpgradableSpecification>(), Mock.Of<IBroadcastSignalRMessage>())
            {
            }

            public List<AlbumResource> Map(List<Album> albums) => MapToResource(albums, false);
        }

        private static Album Album(int id, int artistId) => new Album
        {
            Id = id, Artist = new Artist { Id = artistId }, AlbumReleases = new List<AlbumRelease>()
        };

        [Test]
        public void artist_album_page_requests_only_that_artists_statistics_once()
        {
            var stats = new Mock<IArtistStatisticsService>();
            stats.Setup(x => x.ArtistStatistics(1)).Returns(new ArtistStatistics
            {
                ArtistId = 1, AlbumStatistics = new List<AlbumStatistics> { new AlbumStatistics { AlbumId = 10, TrackFileCount = 8, SizeOnDisk = 123 } }
            });
            var result = new TestController(stats.Object).Map(new List<Album> { Album(10, 1), Album(11, 1) });
            Assert.That(result[0].Statistics.TrackFileCount, Is.EqualTo(8));
            Assert.That(result[0].Statistics.SizeOnDisk, Is.EqualTo(123));
            Assert.That(result[1].Statistics, Is.Null);
            stats.Verify(x => x.ArtistStatistics(1), Times.Once);
            stats.Verify(x => x.ArtistStatistics(), Times.Never);
        }

        [Test]
        public void mixed_artist_album_requests_keep_each_artists_statistics_separate()
        {
            var stats = new Mock<IArtistStatisticsService>();
            stats.Setup(x => x.ArtistStatistics()).Returns(new List<ArtistStatistics>
            {
                new ArtistStatistics { ArtistId = 1, AlbumStatistics = new List<AlbumStatistics> { new AlbumStatistics { AlbumId = 10, SizeOnDisk = 100 } } },
                new ArtistStatistics { ArtistId = 2, AlbumStatistics = new List<AlbumStatistics> { new AlbumStatistics { AlbumId = 20, SizeOnDisk = 200 } } }
            });
            var result = new TestController(stats.Object).Map(new List<Album> { Album(10, 1), Album(20, 2) });
            Assert.That(result[0].Statistics.SizeOnDisk, Is.EqualTo(100));
            Assert.That(result[1].Statistics.SizeOnDisk, Is.EqualTo(200));
            stats.Verify(x => x.ArtistStatistics(), Times.Once);
            stats.Verify(x => x.ArtistStatistics(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void empty_album_requests_do_not_load_library_statistics()
        {
            var stats = new Mock<IArtistStatisticsService>();
            Assert.That(new TestController(stats.Object).Map(new List<Album>()), Is.Empty);
            stats.VerifyNoOtherCalls();
        }
    }
}
