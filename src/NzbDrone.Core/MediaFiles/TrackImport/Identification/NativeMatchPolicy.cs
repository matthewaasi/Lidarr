using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.TrackImport.Specifications;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public static class NativeMatchPolicy
    {
        public static bool IsAccepted(LocalAlbumRelease release, bool newDownload, IConfigService config, Logger logger)
        {
            var mapping = release.TrackMapping;
            if (release.AlbumRelease == null || release.Distance == null || mapping?.LocalExtra == null || mapping.MBExtra == null ||
                release.LocalTracks.Count == 0 || release.LocalTracks.Any(t => !mapping.Mapping.ContainsKey(t)) ||
                !double.IsFinite(release.Distance.NormalizedDistance()) ||
                mapping.Mapping.Values.Any(m => m.Item2 == null || !double.IsFinite(m.Item2.NormalizedDistance())))
            {
                return false;
            }

            // Evaluate only identification rules. Quality, paths and duplicate-file rules run later.
            // Probe mapping distances without committing native assignments before an AI fallback.
            var probe = new LocalAlbumRelease(mapping.Mapping.Select(m => new LocalTrack
            {
                Path = m.Key.Path,
                Distance = m.Value.Item2
            }).ToList())
            {
                AlbumRelease = release.AlbumRelease,
                Distance = release.Distance,
                TrackMapping = mapping,
                NewDownload = newDownload
            };
            var trackSpec = new CloseTrackMatchSpecification(config, logger);
            return new CloseAlbumMatchSpecification(config, logger).IsSatisfiedBy(probe, null).Accepted &&
                new NoMissingOrUnmatchedTracksSpecification(logger).IsSatisfiedBy(probe, null).Accepted &&
                probe.LocalTracks.All(t => trackSpec.IsSatisfiedBy(t, null).Accepted);
        }
    }
}
