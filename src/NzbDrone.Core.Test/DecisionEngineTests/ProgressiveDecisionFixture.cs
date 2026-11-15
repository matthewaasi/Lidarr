using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class ProgressiveDecisionFixture
    {
        private DownloadDecisionMaker _service;
        private Mock<IDecisionEngineSpecification> _specification;
        private AlbumSearchCriteria _criteria;
        private List<ReleaseInfo> _reports;

        [SetUp]
        public void Setup()
        {
            var parsing = new Mock<IParsingService>();
            parsing.Setup(x => x.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<SearchCriteriaBase>()))
                .Returns((ParsedAlbumInfo info, SearchCriteriaBase criteria) => new RemoteAlbum
                {
                    ParsedAlbumInfo = info, Artist = new Artist { Id = 1 }, Albums = new List<Album> { new Album { Id = 1 } }
                });
            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(x => x.ParseCustomFormat(It.IsAny<RemoteAlbum>(), It.IsAny<long>())).Returns(new List<CustomFormat>());
            _specification = new Mock<IDecisionEngineSpecification>();
            _specification.Setup(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>()))
                .Returns((RemoteAlbum album, SearchCriteriaBase criteria) => int.Parse(album.Release.Guid) % 3 == 0 ? Decision.Reject("Same matching rejection") : Decision.Accept());
            _service = new DownloadDecisionMaker(new[] { _specification.Object }, parsing.Object, formats.Object, Mock.Of<IRemoteAlbumAggregationService>(), LogManager.GetCurrentClassLogger());
            _criteria = new AlbumSearchCriteria { Artist = new Artist { Name = "Coldplay" }, Albums = new List<Album> { new Album { Title = "A Head Full Of Dreams" } }, InteractiveSearch = true };
            _reports = Enumerable.Range(1, 15000).Select(id => new ReleaseInfo { Guid = id.ToString(), Title = id % 2 == 0 ? "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" : "Coldplay-A Head Full Of Dreams-CD-MP3-2015-PERFECT" }).ToList();
        }

        [Test]
        public async Task fifteen_thousand_reports_keep_identical_decisions_and_order()
        {
            var baseline = _service.GetSearchDecision(_reports, _criteria);
            var streamed = new List<DownloadDecision>();
            var progressCounts = new List<int>();
            var result = await _service.GetSearchDecisionProgressive(_reports, _criteria, CancellationToken.None, (count, batch) =>
            {
                Assert.That(batch.Count, Is.LessThanOrEqualTo(100));
                streamed.AddRange(batch);
                progressCounts.Add(count);
                return Task.CompletedTask;
            });
            string Snapshot(DownloadDecision decision) => string.Join("|", decision.RemoteAlbum.Release.Guid, decision.Approved, decision.RemoteAlbum.ParsedAlbumInfo.Quality, decision.RemoteAlbum.CustomFormatScore, decision.RemoteAlbum.ReleaseSource, string.Join(",", decision.Rejections.Select(x => x.Reason)));
            Assert.That(result.Select(Snapshot), Is.EqualTo(baseline.Select(Snapshot)));
            Assert.That(streamed, Is.EqualTo(result));
            Assert.That(progressCounts.First(), Is.LessThan(15000));
            Assert.That(progressCounts.Last(), Is.EqualTo(15000));
            Assert.That(result.Count, Is.EqualTo(15000));
        }

        [Test]
        public void cancellation_after_first_batch_stops_further_evaluation()
        {
            using var cancellation = new CancellationTokenSource();
            var count = 0;
            Assert.ThrowsAsync<OperationCanceledException>(async () => await _service.GetSearchDecisionProgressive(_reports, _criteria, cancellation.Token, (processed, batch) =>
            {
                count = processed;
                cancellation.Cancel();
                return Task.CompletedTask;
            }));
            Assert.That(count, Is.InRange(1, 100));
            _specification.Verify(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), _criteria), Times.Exactly(count));
        }

        [Test]
        public void cancelled_before_processing_does_not_evaluate_reports()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await _service.GetSearchDecisionProgressive(_reports, _criteria, cancellation.Token, (_, _) => Task.CompletedTask));
            _specification.VerifyNoOtherCalls();
        }

        [Test]
        public async Task empty_search_reports_zero_processed()
        {
            var calls = 0;
            var result = await _service.GetSearchDecisionProgressive(new List<ReleaseInfo>(), _criteria, CancellationToken.None, (count, batch) =>
            {
                Assert.That(count, Is.Zero);
                Assert.That(batch, Is.Empty);
                calls++;
                return Task.CompletedTask;
            });
            Assert.That(result, Is.Empty);
            Assert.That(calls, Is.EqualTo(1));
        }
    }
}
