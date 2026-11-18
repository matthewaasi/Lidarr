using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.Search;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine
{
    public interface IMakeDownloadDecision
    {
        List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false);
        List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, SearchCriteriaBase searchCriteriaBase);
        Task<List<DownloadDecision>> GetSearchDecisionProgressive(List<ReleaseInfo> reports, SearchCriteriaBase criteria, CancellationToken cancellationToken, Func<int, List<DownloadDecision>, Task> progress);
    }

    public class DownloadDecisionMaker : IMakeDownloadDecision
    {
        private readonly IDecisionEngineSpecification[][] _specifications;
        private readonly IDecisionEngineSpecification[][] _automaticSpecifications;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IParsingService _parsingService;
        private readonly IRemoteAlbumAggregationService _aggregationService;
        private readonly Logger _logger;

        public DownloadDecisionMaker(IEnumerable<IDecisionEngineSpecification> specifications,
            IParsingService parsingService,
            ICustomFormatCalculationService formatService,
            IRemoteAlbumAggregationService aggregationService,
            Logger logger)
        {
            _specifications = specifications.GroupBy(x => x.Priority).OrderBy(x => x.Key).Select(x => x.ToArray()).ToArray();
            _automaticSpecifications = _specifications.Select(group => group.OrderBy(spec =>
                spec is QualityAllowedByProfileSpecification || spec is AlbumRequestedSpecification || spec is SingleAlbumSearchMatchSpecification ? 0 : 1).ToArray()).ToArray();
            _parsingService = parsingService;
            _formatCalculator = formatService;
            _aggregationService = aggregationService;
            _logger = logger;
        }

        public List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false)
        {
            return GetAlbumDecisions(reports).ToList();
        }

        public List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, SearchCriteriaBase searchCriteriaBase)
        {
            return GetSearchDecisionProgressive(reports, searchCriteriaBase, CancellationToken.None, (_, _) => Task.CompletedTask).GetAwaiter().GetResult();
        }

        public async Task<List<DownloadDecision>> GetSearchDecisionProgressive(List<ReleaseInfo> reports, SearchCriteriaBase criteria, CancellationToken cancellationToken, Func<int, List<DownloadDecision>, Task> progress)
        {
            using var context = new ReleaseEvaluationContext();
            var totalTimer = Stopwatch.StartNew();
            var decisions = new List<DownloadDecision>(reports.Count);
            var workers = SearchWorkers();
            var windowSize = workers;
            _logger.ProgressInfo("Processing {0} releases with {1} workers", reports.Count, workers);

            // Bounded windows keep memory, cancellation and progressive output predictable.
            for (var offset = 0; offset < reports.Count;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var windowTimer = Stopwatch.StartNew();
                var count = Math.Min(windowSize, reports.Count - offset);
                var results = new DownloadDecision[count];
                var windowStart = offset;
                await Task.Run(
                    () => Parallel.For(
                        0,
                        count,
                        new ParallelOptions
                        {
                            CancellationToken = cancellationToken,
                            MaxDegreeOfParallelism = workers
                        },
                        index =>
                        {
                            results[index] = GetAlbumDecisions(new List<ReleaseInfo> { reports[windowStart + index] }, false, criteria, cancellationToken, logSummary: false).SingleOrDefault();
                        }),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                var batch = results.Where(x => x != null).ToList();
                decisions.AddRange(batch);
                windowSize = Math.Clamp((int)(count * 250.0 / Math.Max(1, windowTimer.Elapsed.TotalMilliseconds)), workers, 100);
                await progress(offset + count, batch);
                offset += count;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (reports.Count == 0)
            {
                await progress(0, new List<DownloadDecision>());
            }

            _logger.Info(
                "Release processing completed: {0} reports in {1:F3}s ({2:F1}/sec), {3} workers; {4}",
                reports.Count,
                totalTimer.Elapsed.TotalSeconds,
                reports.Count / Math.Max(0.001, totalTimer.Elapsed.TotalSeconds),
                workers,
                context.Summary());
            return decisions;
        }

        private static int SearchWorkers()
        {
            return int.TryParse(Environment.GetEnvironmentVariable("LIDARR_RELEASE_WORKERS"), out var configured)
                ? Math.Clamp(configured, 1, 16)
                : Math.Clamp(Environment.ProcessorCount, 1, 4);
        }

        private IEnumerable<DownloadDecision> GetAlbumDecisions(List<ReleaseInfo> reports, bool pushedRelease = false, SearchCriteriaBase searchCriteria = null, CancellationToken cancellationToken = default, Action<int> processed = null, bool logSummary = true)
        {
            if (logSummary && reports.Any())
            {
                _logger.ProgressInfo("Processing {0} releases", reports.Count);
            }
            else if (logSummary)
            {
                _logger.ProgressInfo("No results found");
            }

            var reportNumber = 1;

            foreach (var report in reports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadDecision decision = null;
                if (logSummary)
                {
                    _logger.ProgressTrace("Processing release {0}/{1}", reportNumber, reports.Count);
                }

                _logger.Debug("Processing release '{0}' from '{1}'", report.Title, report.Indexer);

                try
                {
                    var parsedAlbumInfo = ReleaseEvaluationContext.Measure("parse", () => Parser.Parser.ParseAlbumTitle(report.Title));

                    if (parsedAlbumInfo == null && searchCriteria != null)
                    {
                        parsedAlbumInfo = Parser.Parser.ParseAlbumTitleWithSearchCriteria(report.Title,
                            searchCriteria.Artist,
                            searchCriteria.Albums);
                    }

                    if (parsedAlbumInfo != null && !parsedAlbumInfo.ArtistName.IsNullOrWhiteSpace())
                    {
                        var remoteAlbum = ReleaseEvaluationContext.Measure("map", () => _parsingService.Map(parsedAlbumInfo, searchCriteria));
                        remoteAlbum.Release = report;

                        _aggregationService.Augment(remoteAlbum);

                        // try parsing again using the search criteria, in case it parsed but parsed incorrectly
                        if ((remoteAlbum.Artist == null || remoteAlbum.Albums.Empty()) && searchCriteria != null)
                        {
                            _logger.Debug("Artist/Album null for {0}, reparsing with search criteria", report.Title);
                            var parsedAlbumInfoWithCriteria = Parser.Parser.ParseAlbumTitleWithSearchCriteria(report.Title,
                                                                                                                searchCriteria.Artist,
                                                                                                                searchCriteria.Albums);

                            if (parsedAlbumInfoWithCriteria != null && parsedAlbumInfoWithCriteria.ArtistName.IsNotNullOrWhiteSpace())
                            {
                                remoteAlbum = _parsingService.Map(parsedAlbumInfoWithCriteria, searchCriteria);
                            }
                        }

                        remoteAlbum.Release = report;

                        if (remoteAlbum.Artist == null)
                        {
                            decision = new DownloadDecision(remoteAlbum, new Rejection("Unknown Artist"));

                            // shove in the searched artist in case of forced download in interactive search
                            if (searchCriteria != null)
                            {
                                remoteAlbum.Artist = searchCriteria.Artist;
                                remoteAlbum.Albums = searchCriteria.Albums;
                            }
                        }
                        else if (remoteAlbum.Albums.Empty())
                        {
                            decision = new DownloadDecision(remoteAlbum, new Rejection("Unable to parse albums from release name"));
                            if (searchCriteria != null)
                            {
                                remoteAlbum.Albums = searchCriteria.Albums;
                            }
                        }
                        else
                        {
                            _aggregationService.Augment(remoteAlbum);

                            remoteAlbum.CustomFormats = ReleaseEvaluationContext.Measure("formats", () => _formatCalculator.ParseCustomFormat(remoteAlbum, remoteAlbum.Release.Size));
                            remoteAlbum.CustomFormatScore = remoteAlbum?.Artist?.QualityProfile?.Value.CalculateCustomFormatScore(remoteAlbum.CustomFormats) ?? 0;

                            remoteAlbum.DownloadAllowed = remoteAlbum.Albums.Any();
                            decision = GetDecisionForReport(remoteAlbum, searchCriteria);
                        }
                    }

                    if (searchCriteria != null)
                    {
                        if (parsedAlbumInfo == null)
                        {
                            parsedAlbumInfo = new ParsedAlbumInfo
                            {
                                Quality = QualityParser.ParseQuality(report.Title, null, 0)
                            };
                        }

                        if (parsedAlbumInfo.ArtistName.IsNullOrWhiteSpace())
                        {
                            var remoteAlbum = new RemoteAlbum
                            {
                                Release = report,
                                ParsedAlbumInfo = parsedAlbumInfo
                            };

                            decision = new DownloadDecision(remoteAlbum, new Rejection("Unable to parse release"));
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't process release.");

                    var remoteAlbum = new RemoteAlbum { Release = report };
                    decision = new DownloadDecision(remoteAlbum, new Rejection("Unexpected error processing release"));
                }

                cancellationToken.ThrowIfCancellationRequested();
                processed?.Invoke(reportNumber);
                reportNumber++;

                if (decision != null)
                {
                    var source = pushedRelease ? ReleaseSourceType.ReleasePush : ReleaseSourceType.Rss;

                    if (searchCriteria != null)
                    {
                        if (searchCriteria.InteractiveSearch)
                        {
                            source = ReleaseSourceType.InteractiveSearch;
                        }
                        else if (searchCriteria.UserInvokedSearch)
                        {
                            source = ReleaseSourceType.UserInvokedSearch;
                        }
                        else
                        {
                            source = ReleaseSourceType.Search;
                        }
                    }

                    decision.RemoteAlbum.ReleaseSource = source;

                    if (decision.Rejections.Any())
                    {
                        _logger.Debug("Release rejected for the following reasons: {0}", string.Join(", ", decision.Rejections));
                    }
                    else
                    {
                        _logger.Debug("Release accepted");
                    }

                    yield return decision;
                }
            }
        }

        private DownloadDecision GetDecisionForReport(RemoteAlbum remoteAlbum, SearchCriteriaBase searchCriteria = null)
        {
            var reasons = Array.Empty<Rejection>();

            var groups = searchCriteria != null && !searchCriteria.InteractiveSearch ? _automaticSpecifications : _specifications;
            foreach (var specifications in groups)
            {
                var evaluated = new List<Rejection>();
                foreach (var specification in specifications)
                {
                    var rejection = EvaluateSpec(specification, remoteAlbum, searchCriteria);
                    if (rejection != null)
                    {
                        evaluated.Add(rejection);
                        if (searchCriteria != null && !searchCriteria.InteractiveSearch && rejection.Type == RejectionType.Permanent)
                        {
                            break;
                        }
                    }
                }

                reasons = evaluated.ToArray();

                if (reasons.Any())
                {
                    break;
                }
            }

            return new DownloadDecision(remoteAlbum, reasons.ToArray());
        }

        private Rejection EvaluateSpec(IDecisionEngineSpecification spec, RemoteAlbum remoteAlbum, SearchCriteriaBase searchCriteriaBase = null)
        {
            try
            {
                var result = ReleaseEvaluationContext.Measure("rule." + spec.GetType().Name, () => spec.IsSatisfiedBy(remoteAlbum, searchCriteriaBase));

                if (!result.Accepted)
                {
                    return new Rejection(result.Reason, spec.Type);
                }
            }
            catch (NotImplementedException)
            {
                _logger.Trace("Spec " + spec.GetType().Name + " not implemented.");
            }
            catch (Exception e)
            {
                e.Data.Add("report", remoteAlbum.Release.ToJson());
                e.Data.Add("parsed", remoteAlbum.ParsedAlbumInfo.ToJson());
                _logger.Error(e, "Couldn't evaluate decision on {0}", remoteAlbum.Release.Title);
                return new Rejection($"{spec.GetType().Name}: {e.Message}");
            }

            return null;
        }
    }
}
