using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class ArtistUnsatisfiedSearchCommand : Command
    {
        public override bool SupportsCancellation => true;

        public int ArtistId { get; set; }

        public string AlbumType { get; set; }

        public override bool SendUpdatesToClient => true;
    }
}
