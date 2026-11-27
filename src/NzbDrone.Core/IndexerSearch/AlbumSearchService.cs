using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.IndexerSearch
{
    internal class AlbumSearchService : IExecute<AlbumSearchCommand>,
                               IExecute<MissingAlbumSearchCommand>,
                               IExecute<CutoffUnmetAlbumSearchCommand>,
                               IExecute<ArtistUnsatisfiedSearchCommand>
    {
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IAlbumService _albumService;
        private readonly IArtistService _artistService;
        private readonly IAlbumCutoffService _albumCutoffService;
        private readonly IQueueService _queueService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;

        public AlbumSearchService(ISearchForReleases nzbSearchService,
            IAlbumService albumService,
            IArtistService artistService,
            IAlbumCutoffService albumCutoffService,
            IQueueService queueService,
            IProcessDownloadDecisions processDownloadDecisions,
            Logger logger)
        {
            _releaseSearchService = nzbSearchService;
            _albumService = albumService;
            _artistService = artistService;
            _albumCutoffService = albumCutoffService;
            _queueService = queueService;
            _processDownloadDecisions = processDownloadDecisions;
            _logger = logger;
        }

        private async Task SearchForBulkAlbums(List<Album> albums, bool userInvokedSearch)
        {
            _logger.ProgressInfo("Performing missing search for {0} albums", albums.Count);
            var downloadedCount = 0;

            foreach (var album in albums.OrderBy(a => a.LastSearchTime ?? DateTime.MinValue))
            {
                ProgressMessageContext.CommandModel?.Body.CancellationToken.ThrowIfCancellationRequested();
                List<DownloadDecision> decisions;

                try
                {
                    decisions = await _releaseSearchService.AlbumSearch(album.Id, false, userInvokedSearch, false);
                }
                catch (OperationCanceledException) when (ProgressMessageContext.CommandModel?.Body.CancellationRequested == true)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to search for album: [{0}]", album);
                    continue;
                }

                ProgressMessageContext.CommandModel?.Body.CancellationToken.ThrowIfCancellationRequested();
                var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("Completed search for {0} albums. {1} reports downloaded.", albums.Count, downloadedCount);
        }

        public void Execute(AlbumSearchCommand message)
        {
            foreach (var albumId in message.AlbumIds)
            {
                message.CancellationToken.ThrowIfCancellationRequested();
                var decisions = _releaseSearchService.AlbumSearch(albumId, false, message.Trigger == CommandTrigger.Manual, false).GetAwaiter().GetResult();
                message.CancellationToken.ThrowIfCancellationRequested();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo("Album search completed. {0} reports downloaded.", processed.Grabbed.Count);
            }
        }

        public void Execute(MissingAlbumSearchCommand message)
        {
            List<Album> albums;

            if (message.ArtistId.HasValue)
            {
                var artistId = message.ArtistId.Value;

                var pagingSpec = new PagingSpec<Album>
                {
                    Page = 1,
                    PageSize = 100000,
                    SortDirection = SortDirection.Ascending,
                    SortKey = "Id"
                };

                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Artist.Value.Monitored == true);

                albums = _albumService.AlbumsWithoutFiles(pagingSpec).Records.Where(e => e.ArtistId.Equals(artistId)).ToList();
            }
            else
            {
                var pagingSpec = new PagingSpec<Album>
                {
                    Page = 1,
                    PageSize = 100000,
                    SortDirection = SortDirection.Ascending,
                    SortKey = "Id"
                };

                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Artist.Value.Monitored == true);

                albums = _albumService.AlbumsWithoutFiles(pagingSpec).Records.ToList();
            }

            var queue = _queueService.GetQueue().Where(q => q.Album != null).Select(q => q.Album.Id);
            var missing = albums.Where(e => !queue.Contains(e.Id)).ToList();

            SearchForBulkAlbums(missing, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        public void Execute(CutoffUnmetAlbumSearchCommand message)
        {
            var pagingSpec = new PagingSpec<Album>
            {
                Page = 1,
                PageSize = 100000,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Artist.Value.Monitored == true);

            var albums = _albumCutoffService.AlbumsWhereCutoffUnmet(pagingSpec).Records.ToList();
            var queue = _queueService.GetQueue().Where(q => q.Album != null).Select(q => q.Album.Id);
            var cutoffUnmet = albums.Where(e => !queue.Contains(e.Id)).ToList();

            SearchForBulkAlbums(cutoffUnmet, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        public void Execute(ArtistUnsatisfiedSearchCommand message)
        {
            var pagingSpec = new PagingSpec<Album>
            {
                Page = 1,
                PageSize = int.MaxValue,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            message.CancellationToken.ThrowIfCancellationRequested();
            var artistMetadataId = _artistService.GetArtist(message.ArtistId).ArtistMetadataId;
            pagingSpec.FilterExpressions.Add(album => album.ArtistMetadataId == artistMetadataId &&
                album.Monitored == true && album.Artist.Value.Monitored == true);

            var missing = _albumService.AlbumsWithoutFiles(pagingSpec).Records;
            var cutoffUnmet = _albumCutoffService.AlbumsWhereCutoffUnmet(pagingSpec).Records;
            var queuedAlbumIds = _queueService.GetQueue().Where(item => item.Album != null)
                .Select(item => item.Album.Id).ToHashSet();
            var albums = missing.Concat(cutoffUnmet).DistinctBy(album => album.Id)
                .Where(album => (message.AlbumType == null || album.AlbumType == message.AlbumType) &&
                    !queuedAlbumIds.Contains(album.Id)).ToList();

            SearchForBulkAlbums(albums, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }
    }
}
