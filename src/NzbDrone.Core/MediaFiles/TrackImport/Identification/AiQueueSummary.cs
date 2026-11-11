using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public static class AiQueueSummary
    {
        public static string Get(IEnumerable<AiMatchResult> results)
        {
            var matches = results.ToList();
            if (matches.Any(m => m?.Status == "no_match"))
            {
                return "notAMatch";
            }

            if (matches.Count == 0 || matches.All(m => m == null || (m.Status == null && !m.Proposed && !m.Accepted && !m.IsError)))
            {
                return null;
            }

            return matches.All(m => m != null && (m.Proposed || m.Accepted) && !m.IsError) ? "approved" : "issuesFound";
        }
    }
}
