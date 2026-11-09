using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Music.Singles
{
    public class SingleDecision : ModelBase
    {
        public int AlbumId { get; set; }
        public int ArtistId { get; set; }
        public string Status { get; set; }
        public bool AutoUnmonitored { get; set; }
        public string Evidence { get; set; }
        public DateTime Updated { get; set; }
    }

    public class SinglesEvidence
    {
        public string Artist { get; set; }
        public string Single { get; set; }
        public string ForeignArtistId { get; set; }
        public string ForeignAlbumId { get; set; }
        public string Reason { get; set; }
        public string Fingerprint { get; set; }
        public bool CanCleanup { get; set; }
        public bool Strong { get; set; }
        public List<SingleTrackEvidence> Tracks { get; set; } = new List<SingleTrackEvidence>();
        public List<int> SingleFileIds { get; set; } = new List<int>();
    }

    public class SingleTrackEvidence
    {
        public int TrackId { get; set; }
        public string Title { get; set; }
        public int DurationMs { get; set; }
        public string RecordingId { get; set; }
        public int SourceTrackId { get; set; }
        public int SourceAlbumId { get; set; }
        public string SourceAlbum { get; set; }
        public string SourceForeignAlbumId { get; set; }
        public string SourceTitle { get; set; }
        public int SourceDurationMs { get; set; }
        public string SourceRecordingId { get; set; }
        public int SourceFileId { get; set; }
        public string SourcePath { get; set; }
        public long SourceSize { get; set; }
        public DateTime SourceModified { get; set; }
        public int Tier { get; set; }
        public string Reason { get; set; }
    }

    public interface ISinglesDecisionRepository : IBasicRepository<SingleDecision>
    {
        SingleDecision ForAlbum(int albumId);
        List<SingleDecision> ForArtist(int artistId);
    }

    public class SinglesDecisionRepository : BasicRepository<SingleDecision>, ISinglesDecisionRepository
    {
        public SinglesDecisionRepository(IMainDatabase database, IEventAggregator events)
            : base(database, events)
        {
        }

        public SingleDecision ForAlbum(int albumId) => Query(x => x.AlbumId == albumId).SingleOrDefault();
        public List<SingleDecision> ForArtist(int artistId) => Query(x => x.ArtistId == artistId);
    }
}
