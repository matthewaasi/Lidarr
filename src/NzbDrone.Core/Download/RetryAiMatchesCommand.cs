using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download
{
    public class RetryAiMatchesCommand : Command
    {
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
        public override bool IsTypeExclusive => true;
        public override string CompletionMessage => "Matching retry finished";
    }
}
