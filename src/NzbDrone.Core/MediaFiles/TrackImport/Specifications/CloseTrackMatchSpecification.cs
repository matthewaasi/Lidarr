using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class CloseTrackMatchSpecification : IImportDecisionEngineSpecification<LocalTrack>
    {
        private readonly IConfigService _config;
        private double Threshold => (100 - (_config.OriginalTrackMatchPercent > 0 ? _config.OriginalTrackMatchPercent : 60)) / 100.0;
        private readonly Logger _logger;

        public CloseTrackMatchSpecification(IConfigService config, Logger logger)
        {
            _logger = logger;
            _config = config;
        }

        public Decision IsSatisfiedBy(LocalTrack item, DownloadClientItem downloadClientItem)
        {
            if (item.AiMatch != null)
            {
                return item.AiMatch.Accepted ? Decision.Accept() : Decision.Reject("AI manual review: " + item.AiMatch.Reason);
            }

            var dist = item.Distance.NormalizedDistance();
            var reasons = item.Distance.Reasons;

            if (dist > Threshold)
            {
                _logger.Debug($"Track match is not close enough: {dist} vs {Threshold} {reasons}. Skipping {item}");
                return Decision.Reject($"Track match is not close enough: {1 - dist:P1} vs {1 - Threshold:P0} {reasons}");
            }

            _logger.Debug($"Track accepted: {dist} vs {Threshold} {reasons}.");
            return Decision.Accept();
        }
    }
}
