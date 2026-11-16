using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class ProgressiveReleaseSearchFixture
    {
        private Mock<IIndexer> _indexer;
        private Mock<IMakeDownloadDecision> _decision;
        private ReleaseSearchService _service;
        private List<DownloadDecision> _decisions;

        [SetUp]
        public void Setup()
        {
            _indexer = new Mock<IIndexer>();
            _indexer.SetupGet(x => x.Definition).Returns(new IndexerDefinition());
            _indexer.Setup(x => x.Fetch(It.IsAny<AlbumSearchCriteria>())).ReturnsAsync(new List<ReleaseInfo>());
            _indexer.Setup(x => x.Fetch(It.IsAny<ArtistSearchCriteria>())).ReturnsAsync(new List<ReleaseInfo>());
            var factory = new Mock<IIndexerFactory>();
            factory.Setup(x => x.InteractiveSearchEnabled(true)).Returns(new List<IIndexer> { _indexer.Object });
            var albums = new Mock<IAlbumService>();
            albums.Setup(x => x.GetAlbum(1)).Returns(new Album { Id = 1, ArtistId = 1, Title = "Album" });
            albums.Setup(x => x.GetAlbumsByArtist(1)).Returns(new List<Album> { new Album { Id = 1, Monitored = true }, new Album { Id = 2, Monitored = false } });
            var artists = new Mock<IArtistService>();
            artists.Setup(x => x.GetArtist(1)).Returns(new Artist { Id = 1 });
            _decision = new Mock<IMakeDownloadDecision>();
            _decisions = new List<DownloadDecision>
            {
                Make("same", 1, "rejected"), Make("other", 1), Make("same", 50), Make("same", 10)
            };
            _decision.Setup(x => x.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<SearchCriteriaBase>())).Returns(() => _decisions);
            _decision.Setup(x => x.GetSearchDecisionProgressive(It.IsAny<List<ReleaseInfo>>(), It.IsAny<SearchCriteriaBase>(), It.IsAny<CancellationToken>(), It.IsAny<Func<int, List<DownloadDecision>, Task>>()))
                .Returns(async (List<ReleaseInfo> reports, SearchCriteriaBase criteria, CancellationToken token, Func<int, List<DownloadDecision>, Task> progress) =>
                {
                    await progress(4, _decisions);
                    return _decisions;
                });
            _service = new ReleaseSearchService(factory.Object, albums.Object, artists.Object, _decision.Object, LogManager.GetCurrentClassLogger());
        }

        private static DownloadDecision Make(string guid, int priority, params string[] reasons) => new DownloadDecision(new RemoteAlbum { Release = new ReleaseInfo { Guid = guid, IndexerPriority = priority } }, reasons.Select(x => new Rejection(x)).ToArray());

        [Test]
        public async Task waiting_status_lists_only_indexers_still_running_and_preserves_fetch_order()
        {
            var first = new TaskCompletionSource<IList<ReleaseInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<IList<ReleaseInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _indexer.SetupGet(x => x.Definition).Returns(new IndexerDefinition { Id = 1, Name = "Soulseek" });
            _indexer.Setup(x => x.Fetch(It.IsAny<AlbumSearchCriteria>())).Returns(first.Task);
            var other = new Mock<IIndexer>();
            other.SetupGet(x => x.Definition).Returns(new IndexerDefinition { Id = 2, Name = "Other indexer" });
            other.Setup(x => x.Fetch(It.IsAny<AlbumSearchCriteria>())).Returns(second.Task);
            var factory = new Mock<IIndexerFactory>();
            factory.Setup(x => x.InteractiveSearchEnabled(true)).Returns(new List<IIndexer> { _indexer.Object, other.Object });
            var albums = new Mock<IAlbumService>();
            albums.Setup(x => x.GetAlbum(1)).Returns(new Album { Id = 1, ArtistId = 1, Title = "Album" });
            var artists = new Mock<IArtistService>();
            artists.Setup(x => x.GetArtist(1)).Returns(new Artist { Id = 1 });
            var service = new ReleaseSearchService(factory.Object, albums.Object, artists.Object, _decision.Object, LogManager.GetCurrentClassLogger());
            var statuses = new List<string[]>();
            var partial = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var search = service.AlbumSearchProgressive(1, CancellationToken.None, (_, _, _) => Task.CompletedTask, waiting =>
            {
                statuses.Add(waiting.Select(x => x.Name).ToArray());
                if (waiting.Count == 1 && waiting[0].Id == 1)
                {
                    partial.TrySetResult(true);
                }

                return Task.CompletedTask;
            });
            second.SetResult(new List<ReleaseInfo> { new ReleaseInfo { Guid = "second" } });
            await partial.Task.WaitAsync(TimeSpan.FromSeconds(3));
            first.SetResult(new List<ReleaseInfo> { new ReleaseInfo { Guid = "first" } });
            await search;
            Assert.That(statuses.First(), Is.EqualTo(new[] { "Soulseek", "Other indexer" }));
            Assert.That(statuses.Last(), Is.Empty);
            _decision.Verify(x => x.GetSearchDecisionProgressive(It.Is<List<ReleaseInfo>>(reports => reports[0].Guid == "first" && reports[1].Guid == "second"), It.IsAny<SearchCriteriaBase>(), It.IsAny<CancellationToken>(), It.IsAny<Func<int, List<DownloadDecision>, Task>>()), Times.Once);
        }

        [Test]
        public async Task completion_preserves_duplicate_winners_and_source_order()
        {
            var baseline = await _service.AlbumSearch(1, true, true, true);
            var result = await _service.AlbumSearchProgressive(1, CancellationToken.None, (_, _, _) => Task.CompletedTask);
            Assert.That(result, Is.EqualTo(baseline));
            Assert.That(result.Select(x => x.RemoteAlbum.Release.Guid), Is.EqualTo(new[] { "same", "other" }));
            Assert.That(result[0].RemoteAlbum.Release.IndexerPriority, Is.EqualTo(10));
        }

        [Test]
        public async Task artist_search_retains_monitored_album_scope()
        {
            await _service.ArtistSearchProgressive(1, CancellationToken.None, (_, _, _) => Task.CompletedTask);
            _indexer.Verify(x => x.Fetch(It.Is<ArtistSearchCriteria>(criteria => criteria.InteractiveSearch && criteria.UserInvokedSearch && criteria.Albums.Count == 1 && criteria.Albums[0].Id == 1)), Times.Once);
        }

        [Test]
        public async Task closing_during_plugin_fetch_returns_without_waiting_for_plugin_or_processing_results()
        {
            var plugin = new TaskCompletionSource<IList<ReleaseInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _indexer.Setup(x => x.Fetch(It.IsAny<AlbumSearchCriteria>())).Returns(plugin.Task);
            using var cancellation = new CancellationTokenSource();
            var search = _service.AlbumSearchProgressive(1, cancellation.Token, (_, _, _) => Task.CompletedTask);
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await search.WaitAsync(TimeSpan.FromSeconds(3)));
            plugin.SetResult(new List<ReleaseInfo>());
            await Task.Yield();
            _decision.VerifyNoOtherCalls();
        }
    }
}
