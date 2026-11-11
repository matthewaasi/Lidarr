using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class NoMissingOrUnmatchedTracksSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private readonly Logger _logger;

        public NoMissingOrUnmatchedTracksSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            if (item.NewDownload && item.TrackMapping.MBExtra.Count > 0)
            {
                var missing = item.TrackMapping.MBExtra;
                var expected = item.TrackMapping.Mapping.Count + missing.Count;
                var titles = string.Join(", ", missing.Take(10).Select(t => t.Title));
                if (item.AiMatch != null && item.TrackCount >= expected && !item.LocalTracks.Any(f => f.AiMatch?.Status == "no_match"))
                {
                    return Decision.Reject($"Expected tracks could not all be identified; manual review required. Unresolved: {titles}");
                }

                return Decision.Reject($"Missing tracks: {missing.Count} of {expected} expected tracks are missing. Missing: {titles}");
            }

            if (item.AiMatch?.Accepted == true)
            {
                // Extra unmatched files remain per-file decisions, but incomplete releases cannot auto-import.
                return Decision.Accept();
            }

            if (item.NewDownload && item.TrackMapping.LocalExtra.Count > 0)
            {
                _logger.Debug("This release has track files that have not been matched. Skipping {0}", item);
                return Decision.Reject("Has unmatched tracks");
            }

            return Decision.Accept();
        }
    }
}
