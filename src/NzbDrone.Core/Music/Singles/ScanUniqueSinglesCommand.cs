using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Music.Singles
{
    public class ScanUniqueSinglesCommand : Command
    {
        public int ArtistId { get; set; }
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override bool IsTypeExclusive => true;
        public override string CompletionMessage => "Unique Singles scan finished";
    }
}
