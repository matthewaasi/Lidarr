using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.MediaFiles
{
    public enum ArtistFileOperation
    {
        Rename,
        Retag
    }

    public class ArtistFileStatus : ModelBase
    {
        public int ArtistId { get; set; }
        public ArtistFileOperation Operation { get; set; }
        public string State { get; set; }
        public string Settings { get; set; }
        public int Pending { get; set; }
        public DateTime? LastChecked { get; set; }
        public DateTime? LastApplied { get; set; }
    }

    public interface IArtistFileStatusRepository : IBasicRepository<ArtistFileStatus>
    {
        ArtistFileStatus Find(int artistId, ArtistFileOperation operation);
    }

    public class ArtistFileStatusRepository : BasicRepository<ArtistFileStatus>, IArtistFileStatusRepository
    {
        public ArtistFileStatusRepository(IMainDatabase database, IEventAggregator events)
            : base(database, events)
        {
        }

        public ArtistFileStatus Find(int artistId, ArtistFileOperation operation)
        {
            return Query(x => x.ArtistId == artistId && x.Operation == operation).SingleOrDefault();
        }
    }

    public interface IArtistFileStatusService
    {
        List<ArtistFileStatus> All();
        string GetSettings(ArtistFileOperation operation);
        void Record(int artistId, ArtistFileOperation operation, string settings, int pending, bool failed = false, bool applied = false);
    }

    public class ArtistFileStatusService : IArtistFileStatusService,
        IHandle<TrackFileAddedEvent>,
        IHandle<TrackFileDeletedEvent>,
        IHandle<TrackFileRenamedEvent>,
        IHandle<TrackFileRetaggedEvent>,
        IHandle<ArtistUpdatedEvent>,
        IHandle<ArtistEditedEvent>,
        IHandle<ArtistMovedEvent>,
        IHandle<AlbumUpdatedEvent>,
        IHandle<AlbumEditedEvent>,
        IHandle<AlbumDeletedEvent>,
        IHandle<AlbumInfoRefreshedEvent>,
        IHandle<ArtistsDeletedEvent>,
        IHandle<ModelEvent<TrackFile>>
    {
        private static readonly object Gate = new object();
        private readonly IArtistFileStatusRepository _repository;
        private readonly INamingConfigService _naming;
        private readonly IConfigService _config;
        private readonly IAlbumRepository _albums;

        public ArtistFileStatusService(IArtistFileStatusRepository repository, INamingConfigService naming, IConfigService config, IAlbumRepository albums)
        {
            _repository = repository;
            _naming = naming;
            _config = config;
            _albums = albums;
        }

        public List<ArtistFileStatus> All()
        {
            var renameSettings = GetSettings(ArtistFileOperation.Rename);
            var retagSettings = GetSettings(ArtistFileOperation.Retag);
            var records = _repository.All().ToList();
            foreach (var record in records)
            {
                if (record.Settings != (record.Operation == ArtistFileOperation.Rename ? renameSettings : retagSettings))
                {
                    record.State = "needsRecheck";
                }
            }

            return records;
        }

        public string GetSettings(ArtistFileOperation operation)
        {
            return operation == ArtistFileOperation.Rename
                ? STJson.ToJson(_naming.GetConfig())
                : STJson.ToJson(new { _config.ScrubAudioTags, _config.EmbedCoverArt });
        }

        public void Record(int artistId, ArtistFileOperation operation, string settings, int pending, bool failed = false, bool applied = false)
        {
            lock (Gate)
            {
                var record = _repository.Find(artistId, operation) ?? new ArtistFileStatus { ArtistId = artistId, Operation = operation };
                record.Settings = settings;
                record.Pending = pending;
                record.State = failed ? "failed" : pending > 0 ? "changesPending" : "upToDate";
                record.LastChecked = DateTime.UtcNow;
                if (applied && !failed && pending == 0)
                {
                    record.LastApplied = record.LastChecked;
                }

                _repository.Upsert(record);
            }
        }

        private void Invalidate(int artistId, ArtistFileOperation? operation = null)
        {
            lock (Gate)
            {
                foreach (var kind in Enum.GetValues<ArtistFileOperation>().Where(kind => !operation.HasValue || kind == operation))
                {
                    var record = _repository.Find(artistId, kind);
                    if (record != null && record.State != "needsRecheck")
                    {
                        record.State = "needsRecheck";
                        _repository.Update(record);
                    }
                }
            }
        }

        private void Invalidate(TrackFile file)
        {
            if (file.AlbumId > 0)
            {
                var album = _albums.Find(file.AlbumId);
                if (album != null)
                {
                    Invalidate(album.ArtistId);
                }
            }
        }

        public void Handle(TrackFileAddedEvent message) => Invalidate(message.TrackFile);
        public void Handle(TrackFileDeletedEvent message) => Invalidate(message.TrackFile);
        public void Handle(TrackFileRenamedEvent message) => Invalidate(message.Artist.Id, ArtistFileOperation.Rename);
        public void Handle(TrackFileRetaggedEvent message) => Invalidate(message.Artist.Id, ArtistFileOperation.Retag);
        public void Handle(ArtistUpdatedEvent message) => Invalidate(message.Artist.Id);
        public void Handle(ArtistEditedEvent message) => Invalidate(message.Artist.Id);
        public void Handle(ArtistMovedEvent message) => Invalidate(message.Artist.Id);
        public void Handle(AlbumUpdatedEvent message) => Invalidate(message.Album.ArtistId);
        public void Handle(AlbumEditedEvent message) => Invalidate(message.Album.ArtistId);
        public void Handle(AlbumDeletedEvent message) => Invalidate(message.Album.ArtistId);
        public void Handle(ModelEvent<TrackFile> message) => Invalidate(message.Model);

        public void Handle(AlbumInfoRefreshedEvent message)
        {
            if (message.Added.Count + message.Updated.Count + message.Removed.Count > 0)
            {
                Invalidate(message.Artist.Id);
            }
        }

        public void Handle(ArtistsDeletedEvent message)
        {
            lock (Gate)
            {
                foreach (var artist in message.Artists)
                {
                    foreach (var kind in Enum.GetValues<ArtistFileOperation>())
                    {
                        var record = _repository.Find(artist.Id, kind);
                        if (record != null)
                        {
                            _repository.Delete(record.Id);
                        }
                    }
                }
            }
        }
    }
}
