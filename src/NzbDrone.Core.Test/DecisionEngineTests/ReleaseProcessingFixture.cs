using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.Search;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class ReleaseProcessingFixture
    {
        private static DownloadDecisionMaker Create(IEnumerable<IDecisionEngineSpecification> specifications)
        {
            var parsing = new Mock<IParsingService>();
            parsing.Setup(x => x.Map(It.IsAny<ParsedAlbumInfo>(), It.IsAny<SearchCriteriaBase>()))
                .Returns((ParsedAlbumInfo info, SearchCriteriaBase _) => new RemoteAlbum
                {
                    ParsedAlbumInfo = info,
                    Artist = new Artist { Id = 1 },
                    Albums = new List<Album> { new Album { Id = 1 } }
                });
            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(x => x.ParseCustomFormat(It.IsAny<RemoteAlbum>(), It.IsAny<long>())).Returns(new List<CustomFormat>());
            return new DownloadDecisionMaker(specifications, parsing.Object, formats.Object, Mock.Of<IRemoteAlbumAggregationService>(), LogManager.GetCurrentClassLogger());
        }

        private static List<ReleaseInfo> Reports(int count)
        {
            return Enumerable.Range(1, count).Select(id => new ReleaseInfo
            {
                Guid = id.ToString(),
                Title = id % 2 == 0 ? "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" : "Coldplay-A Head Full Of Dreams-CD-MP3-2015-PERFECT"
            }).ToList();
        }

        private static AlbumSearchCriteria Criteria(bool interactive)
        {
            return new AlbumSearchCriteria { Artist = new Artist { Id = 1 }, Albums = new List<Album> { new Album { Id = 1 } }, InteractiveSearch = interactive };
        }

        [Test]
        public void automatic_search_runs_cheap_wrong_album_gate_before_expensive_rules()
        {
            var expensive = new Mock<IDecisionEngineSpecification>();
            expensive.Setup(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>())).Returns(Decision.Accept());
            var requested = new AlbumRequestedSpecification(LogManager.GetCurrentClassLogger());
            var criteria = Criteria(false);
            criteria.Albums[0].Id = 999;
            var result = Create(new[] { expensive.Object, requested }).GetSearchDecision(Reports(1), criteria);
            Assert.That(result.Single().Rejected, Is.True);
            expensive.Verify(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>()), Times.Never());
        }

        [Test]
        public void interactive_search_retains_all_rejections_and_original_rule_order()
        {
            var first = new Mock<IDecisionEngineSpecification>();
            first.Setup(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>())).Returns(Decision.Reject("first"));
            var second = new Mock<IDecisionEngineSpecification>();
            second.Setup(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>())).Returns(Decision.Reject("second"));
            var result = Create(new[] { first.Object, second.Object }).GetSearchDecision(Reports(1), Criteria(true));
            Assert.That(result.Single().Rejections.Select(x => x.Reason), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void automatic_temporary_rejection_does_not_hide_permanent_rejection()
        {
            var temporary = new Mock<IDecisionEngineSpecification>();
            temporary.SetupGet(x => x.Type).Returns(RejectionType.Temporary);
            temporary.Setup(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>())).Returns(Decision.Reject("temporary"));
            var permanent = new Mock<IDecisionEngineSpecification>();
            permanent.SetupGet(x => x.Type).Returns(RejectionType.Permanent);
            permanent.Setup(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>())).Returns(Decision.Reject("permanent"));
            var result = Create(new[] { temporary.Object, permanent.Object }).GetSearchDecision(Reports(1), Criteria(false));
            Assert.That(result.Single().TemporarilyRejected, Is.False);
            Assert.That(result.Single().Rejected, Is.True);
        }

        [Test]
        public async Task failed_progress_callback_does_not_leak_search_cache()
        {
            var service = Create(Array.Empty<IDecisionEngineSpecification>());
            Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSearchDecisionProgressive(Reports(1), Criteria(true), CancellationToken.None, (_, _) => throw new InvalidOperationException("disconnected")));
            Assert.That(ReleaseEvaluationContext.Read("test", 1, () => "outside"), Is.EqualTo("outside"));
            var result = await service.GetSearchDecisionProgressive(Reports(1), Criteria(true), CancellationToken.None, (_, _) => Task.CompletedTask);
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        [Category("ReleaseBenchmark")]
        public void ten_thousand_reports_preserve_decisions_and_coalesce_album_reads()
        {
            var repository = new Mock<IMediaFileRepository>();
            repository.Setup(x => x.GetFilesByAlbum(1)).Returns(new List<TrackFile> { new TrackFile { Id = 1 } });
            var files = new MediaFileService(repository.Object, Mock.Of<IEventAggregator>(), LogManager.GetCurrentClassLogger());
            var spec = new Mock<IDecisionEngineSpecification>();
            spec.Setup(x => x.IsSatisfiedBy(It.IsAny<RemoteAlbum>(), It.IsAny<SearchCriteriaBase>()))
                .Returns((RemoteAlbum album, SearchCriteriaBase _) => files.GetFilesByAlbum(1).Count == 1 && int.Parse(album.Release.Guid) % 3 != 0 ? Decision.Accept() : Decision.Reject("same rejection"));
            var service = Create(new[] { spec.Object });
            var reports = Reports(10000);
            var criteria = Criteria(true);
            var legacy = typeof(DownloadDecisionMaker).GetMethod("GetAlbumDecisions", BindingFlags.Instance | BindingFlags.NonPublic);
            var timer = Stopwatch.StartNew();
            var baseline = ((IEnumerable<DownloadDecision>)legacy.Invoke(service, new object[] { reports, false, criteria, CancellationToken.None, null, true })).ToList();
            var baselineSeconds = timer.Elapsed.TotalSeconds;
            repository.Verify(x => x.GetFilesByAlbum(1), Times.Exactly(10000));
            repository.Invocations.Clear();
            timer.Restart();
            var optimized = service.GetSearchDecision(reports, criteria);
            var optimizedSeconds = timer.Elapsed.TotalSeconds;
            repository.Verify(x => x.GetFilesByAlbum(1), Times.Once());
            string Snapshot(DownloadDecision decision) => $"{decision.RemoteAlbum.Release.Guid}|{decision.Approved}|{decision.RemoteAlbum.ParsedAlbumInfo.Quality}|{decision.RemoteAlbum.CustomFormatScore}|{decision.RemoteAlbum.ReleaseSource}|{string.Join(",", decision.Rejections.Select(x => x.Reason))}";
            Assert.That(optimized.Select(Snapshot), Is.EqualTo(baseline.Select(Snapshot)));
            TestContext.Out.WriteLine($"SYNTHETIC_BENCHMARK reports=10000 serial_seconds={baselineSeconds:F3} optimized_seconds={optimizedSeconds:F3} optimized_per_second={10000 / optimizedSeconds:F1} album_reads=10000->1; mocked database/mapping, not production throughput");
        }
    }
}
