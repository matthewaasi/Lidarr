using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Api.V1.Indexers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Api.Test.Indexers
{
    [TestFixture]
    public class ProgressiveReleaseControllerFixture
    {
        private Mock<ISearchForReleases> _search;
        private Mock<IPrioritizeDownloadDecision> _prioritize;
        private Mock<IDownloadService> _downloads;
        private ReleaseController _controller;
        private List<DownloadDecision> _decisions;
        private MemoryStream _body;

        [SetUp]
        public void Setup()
        {
            _search = new Mock<ISearchForReleases>();
            _prioritize = new Mock<IPrioritizeDownloadDecision>();
            _prioritize.Setup(x => x.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>())).Returns((List<DownloadDecision> decisions) => decisions.AsEnumerable().Reverse().ToList());
            _downloads = new Mock<IDownloadService>();
            var cache = new Mock<ICacheManager>();
            cache.Setup(x => x.GetCache<RemoteAlbum>(It.IsAny<Type>(), "remoteAlbums")).Returns(new Cached<RemoteAlbum>());
            var profiles = new Mock<IQualityProfileService>();
            profiles.Setup(x => x.GetDefaultProfile(string.Empty, null, It.IsAny<Quality[]>())).Returns(new QualityProfile { Items = new List<QualityProfileQualityItem>() });
            _controller = new ReleaseController(Mock.Of<IAlbumService>(), Mock.Of<IArtistService>(), Mock.Of<IFetchAndParseRss>(), _search.Object, Mock.Of<IMakeDownloadDecision>(), _prioritize.Object, Mock.Of<IParsingService>(), _downloads.Object, cache.Object, profiles.Object, LogManager.GetCurrentClassLogger());
            _body = new MemoryStream();
            _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            _controller.Response.Body = _body;
            _decisions = new List<DownloadDecision> { Make("a", 1), Make("b", 2, "Rejected") };
            _search.Setup(x => x.AlbumSearch(1, true, true, true)).ReturnsAsync(_decisions);
            _search.Setup(x => x.AlbumSearchProgressive(1, It.IsAny<CancellationToken>(), It.IsAny<Func<int, int, List<DownloadDecision>, Task>>()))
                .Returns(async (int albumId, CancellationToken token, Func<int, int, List<DownloadDecision>, Task> progress) =>
                {
                    await progress(1, 2, new List<DownloadDecision> { _decisions[0] });
                    Assert.That(_body.Length, Is.GreaterThan(0));
                    await progress(2, 2, new List<DownloadDecision> { _decisions[1] });
                    return _decisions;
                });
        }

        private static DownloadDecision Make(string guid, int indexer, params string[] rejections)
        {
            var album = new RemoteAlbum
            {
                Release = new ReleaseInfo { Guid = guid, IndexerId = indexer, Title = "Artist-Album", PublishDate = DateTime.UtcNow.AddHours(-1) },
                ParsedAlbumInfo = new ParsedAlbumInfo { Quality = new QualityModel() }, Artist = new Artist { Id = 1 }
            };
            return new DownloadDecision(album, rejections.Select(x => new Rejection(x)).ToArray());
        }

        private List<JsonElement> Messages() => Encoding.UTF8.GetString(_body.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();

        [Test]
        public async Task checked_batches_arrive_before_same_final_order_and_rejections_as_original_endpoint()
        {
            _controller.Request.QueryString = new QueryString("?albumId=1");
            var baseline = await _controller.GetReleases(1, null);
            await _controller.StreamReleases(1, null);
            var messages = Messages();
            Assert.That(messages.First().GetProperty("type").GetString(), Is.EqualTo("searching"));
            Assert.That(messages.Count(x => x.GetProperty("type").GetString() == "batch"), Is.EqualTo(2));
            var final = messages.Last();
            Assert.That(final.GetProperty("type").GetString(), Is.EqualTo("complete"));
            var rows = final.GetProperty("records").EnumerateArray().ToList();
            Assert.That(rows.Select(x => x.GetProperty("guid").GetString()), Is.EqualTo(baseline.Select(x => x.Guid)));
            Assert.That(rows.Select(x => x.GetProperty("releaseWeight").GetInt32()), Is.EqualTo(baseline.Select(x => x.ReleaseWeight)));
            Assert.That(rows[0].GetProperty("rejections").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "Rejected" }));
            Assert.That(_controller.Response.ContentType, Is.EqualTo("application/x-ndjson"));
            Assert.That(_controller.Response.Headers["X-Accel-Buffering"].ToString(), Is.EqualTo("no"));
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public async Task closing_the_request_does_not_sort_complete_or_start_downloads()
        {
            using var cancellation = new CancellationTokenSource();
            _controller.HttpContext.RequestAborted = cancellation.Token;
            _search.Setup(x => x.AlbumSearchProgressive(1, cancellation.Token, It.IsAny<Func<int, int, List<DownloadDecision>, Task>>()))
                .Returns(async (int id, CancellationToken token, Func<int, int, List<DownloadDecision>, Task> progress) =>
                {
                    await progress(1, 2, new List<DownloadDecision> { _decisions[0] });
                    cancellation.Cancel();
                    return _decisions;
                });
            await _controller.StreamReleases(1, null);
            Assert.That(Messages().Any(x => x.GetProperty("type").GetString() == "complete"), Is.False);
            _prioritize.Verify(x => x.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()), Times.Never);
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public async Task stream_error_is_explicit_and_never_marks_partial_results_complete()
        {
            _search.Setup(x => x.AlbumSearchProgressive(1, It.IsAny<CancellationToken>(), It.IsAny<Func<int, int, List<DownloadDecision>, Task>>())).ThrowsAsync(new InvalidOperationException("Plugin failed"));
            await _controller.StreamReleases(1, null);
            Assert.That(Messages().Last().GetProperty("type").GetString(), Is.EqualTo("error"));
            Assert.That(Messages().Any(x => x.GetProperty("type").GetString() == "complete"), Is.False);
        }

        [Test]
        public async Task invalid_scope_never_starts_search()
        {
            await _controller.StreamReleases(1, 1);
            Assert.That(_controller.Response.StatusCode, Is.EqualTo(400));
            _search.VerifyNoOtherCalls();
        }
    }
}
