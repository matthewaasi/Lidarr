using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public class AiMatchResult
    {
        public bool Accepted { get; set; }
        public bool Proposed { get; set; }
        public bool IsCached { get; set; }
        public bool IsError { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public double Confidence { get; set; }
        public string Model { get; set; }
        public string CandidateId { get; set; }
        public string TrackId { get; set; }
        public List<string> Candidates { get; set; } = new List<string>();
    }
}
