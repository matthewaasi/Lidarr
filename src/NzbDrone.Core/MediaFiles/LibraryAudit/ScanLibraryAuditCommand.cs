using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.LibraryAudit
{
    public class ScanLibraryAuditCommand : Command
    {
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
}
