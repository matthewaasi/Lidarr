using System;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.LibraryAudit
{
    public class ScanLibraryAuditCommand : Command
    {
        private readonly object _progressGate = new object();
        private AuditScanProgress _progress;

        protected override object GetProgress() => GetProgressSnapshot();

        internal AuditScanProgress GetProgressSnapshot()
        {
            lock (_progressGate)
            {
                return _progress ?? new AuditScanProgress("queued", 0, null, 0, Duplicates ? 3 : 2, null);
            }
        }

        internal void ReportProgress(string phase, int processed, int? total, string activity = null)
        {
            var stages = Duplicates ? 3 : 2;
            var stage = phase == "duplicates" ? 2 : phase == "saving" || phase == "completed" ? stages : 1;
            lock (_progressGate)
            {
                _progress = new AuditScanProgress(phase, processed, total, stage, stages, activity);
            }
        }

        public override bool SupportsCancellation => true;

        public bool Versions { get; set; } = true;
        public bool Duplicates { get; set; } = true;
        public bool Fingerprints { get; set; }
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override bool IsTypeExclusive => true;
        public override string CompletionMessage => "Library audit finished";
    }

    public class AuditScanProgress
    {
        public string Phase { get; }
        public int Processed { get; }
        public int? Total { get; }
        public int Stage { get; }
        public int Stages { get; }
        public string Activity { get; }
        public double? Percentage { get; }
        public DateTime Updated { get; }

        public AuditScanProgress(string phase, int processed, int? total, int stage, int stages, string activity)
        {
            Phase = phase;
            Total = total.HasValue ? Math.Max(0, total.Value) : null;
            Processed = Math.Clamp(processed, 0, Total ?? int.MaxValue);
            Stage = stage;
            Stages = stages;
            Activity = activity;
            Percentage = Total.HasValue ? Total == 0 ? 100.0 : Math.Round(Processed * 100.0 / Total.Value, 1) : null;
            Updated = DateTime.UtcNow;
        }
    }
}
