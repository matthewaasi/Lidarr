using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class CloseAlbumMatchSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private readonly IConfigService _config;
        private double AlbumThreshold => (100 - (_config.OriginalAlbumMatchPercent > 0 ? _config.OriginalAlbumMatchPercent : 80)) / 100.0;
        private double TrackThreshold => (100 - (_config.OriginalTrackMatchPercent > 0 ? _config.OriginalTrackMatchPercent : 60)) / 100.0;
        private readonly Logger _logger;

        public CloseAlbumMatchSpecification(IConfigService config, Logger logger)
        {
            _logger = logger;
            _config = config;
        }

        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            if (item.AiMatch != null)
            {
                return item.AiMatch.Accepted ? Decision.Accept() : Decision.Reject("AI manual review: " + item.AiMatch.Reason);
            }

            double dist;
            string reasons;

            // strict when a new download
            if (item.NewDownload)
            {
                dist = item.Distance.NormalizedDistance();
                reasons = item.Distance.Reasons;
                if (dist > AlbumThreshold + 1e-9)
                {
                    _logger.Debug($"Album match is not close enough: {dist} vs {AlbumThreshold} {reasons}. Skipping {item}");
                    return Decision.Reject($"Album match is not close enough: {1 - dist:P1} vs {1 - AlbumThreshold:P0} {reasons}");
                }

                var worstTrackMatch = item.LocalTracks.Where(x => x.Distance != null).MaxBy(x => x.Distance.NormalizedDistance());
                if (worstTrackMatch == null)
                {
                    _logger.Debug($"No tracks matched");
                    return Decision.Reject("No tracks matched");
                }
                else
                {
                    var maxTrackDist = worstTrackMatch.Distance.NormalizedDistance();
                    var trackReasons = worstTrackMatch.Distance.Reasons;
                    if (maxTrackDist > TrackThreshold + 1e-9)
                    {
                        _logger.Debug($"Worst track match: {maxTrackDist} vs {TrackThreshold} {trackReasons}. Skipping {item}");
                        return Decision.Reject($"Worst track match: {1 - maxTrackDist:P1} vs {1 - TrackThreshold:P0} {trackReasons}");
                    }
                }
            }

            // otherwise importing existing files in library
            else
            {
                // get album distance ignoring whether tracks are missing
                dist = item.Distance.NormalizedDistanceExcluding(new List<string> { "missing_tracks", "unmatched_tracks" });
                reasons = item.Distance.Reasons;
                if (dist > AlbumThreshold + 1e-9)
                {
                    _logger.Debug($"Album match is not close enough: {dist} vs {AlbumThreshold} {reasons}. Skipping {item}");
                    return Decision.Reject($"Album match is not close enough: {1 - dist:P1} vs {1 - AlbumThreshold:P0} {reasons}");
                }
            }

            _logger.Debug($"Accepting release {item}: dist {dist} vs {AlbumThreshold} {reasons}");
            return Decision.Accept();
        }
    }
}
