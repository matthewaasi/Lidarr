using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentValidation;
using Lidarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Validation;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Lidarr.Api.V1.Indexers
{
    [V1ApiController]
    public class ReleaseController : ReleaseControllerBase
    {
        private readonly IAlbumService _albumService;
        private readonly IArtistService _artistService;
        private readonly IFetchAndParseRss _rssFetcherAndParser;
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IMakeDownloadDecision _downloadDecisionMaker;
        private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
        private readonly IParsingService _parsingService;
        private readonly IDownloadService _downloadService;
        private readonly Logger _logger;

        private readonly ICached<RemoteAlbum> _remoteAlbumCache;

        public ReleaseController(IAlbumService albumService,
                             IArtistService artistService,
                             IFetchAndParseRss rssFetcherAndParser,
                             ISearchForReleases nzbSearchService,
                             IMakeDownloadDecision downloadDecisionMaker,
                             IPrioritizeDownloadDecision prioritizeDownloadDecision,
                             IParsingService parsingService,
                             IDownloadService downloadService,
                             ICacheManager cacheManager,
                             IQualityProfileService qualityProfileService,
                             Logger logger)
            : base(qualityProfileService)
        {
            _albumService = albumService;
            _artistService = artistService;
            _rssFetcherAndParser = rssFetcherAndParser;
            _releaseSearchService = nzbSearchService;
            _downloadDecisionMaker = downloadDecisionMaker;
            _prioritizeDownloadDecision = prioritizeDownloadDecision;
            _parsingService = parsingService;
            _downloadService = downloadService;
            _logger = logger;

            PostValidator.RuleFor(s => s.IndexerId).ValidId();
            PostValidator.RuleFor(s => s.Guid).NotEmpty();

            _remoteAlbumCache = cacheManager.GetCache<RemoteAlbum>(GetType(), "remoteAlbums");
        }

        [HttpPost]
        public async Task<ActionResult<ReleaseResource>> DownloadRelease([FromBody] ReleaseResource release)
        {
            ValidateResource(release);

            var remoteAlbum = _remoteAlbumCache.Find(GetCacheKey(release));

            if (remoteAlbum == null)
            {
                _logger.Debug("Couldn't find requested release in cache, cache timeout probably expired.");

                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Couldn't find requested release in cache, try searching again");
            }

            try
            {
                if (remoteAlbum.Artist == null)
                {
                    if (release.AlbumId.HasValue)
                    {
                        var album = _albumService.GetAlbum(release.AlbumId.Value);

                        remoteAlbum.Artist = _artistService.GetArtist(album.ArtistId);
                        remoteAlbum.Albums = new List<Album> { album };
                    }
                    else if (release.ArtistId.HasValue)
                    {
                        var artist = _artistService.GetArtist(release.ArtistId.Value);
                        var albums = _parsingService.GetAlbums(remoteAlbum.ParsedAlbumInfo, artist);

                        if (albums.Empty())
                        {
                            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse albums in the release");
                        }

                        remoteAlbum.Artist = artist;
                        remoteAlbum.Albums = albums;
                    }
                    else
                    {
                        throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to find matching artist and albums");
                    }
                }
                else if (remoteAlbum.Albums.Empty())
                {
                    var albums = _parsingService.GetAlbums(remoteAlbum.ParsedAlbumInfo, remoteAlbum.Artist);

                    if (albums.Empty() && release.AlbumId.HasValue)
                    {
                        var album = _albumService.GetAlbum(release.AlbumId.Value);

                        albums = new List<Album> { album };
                    }

                    remoteAlbum.Albums = albums;
                }

                if (remoteAlbum.Albums.Empty())
                {
                    throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse albums in the release");
                }

                await _downloadService.DownloadReport(remoteAlbum, release.DownloadClientId);
            }
            catch (ReleaseDownloadException ex)
            {
                _logger.Error(ex, ex.Message);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Getting release from indexer failed");
            }

            return Ok(release);
        }

        [HttpGet("stream")]
        public async Task StreamReleases(int? albumId, int? artistId)
        {
            if (albumId.HasValue == artistId.HasValue || albumId <= 0 || artistId <= 0)
            {
                Response.StatusCode = 400;
                return;
            }

            var cancellationToken = HttpContext.RequestAborted;
            Response.ContentType = "application/x-ndjson";
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["X-Accel-Buffering"] = "no";
            var options = NzbDrone.Common.Serializer.STJson.GetSerializerSettings();
            options.WriteIndented = false;
            var winners = new Dictionary<string, DownloadDecision>();
            var weights = new Dictionary<string, int>();
            async Task Send(object message)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Response.WriteAsync(JsonSerializer.Serialize(message, options) + "\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            async Task Progress(int processed, int total, List<DownloadDecision> batch)
            {
                var changes = new List<ReleaseResource>();
                foreach (var decision in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = decision.RemoteAlbum.Release.Guid == null ? "null:" : "guid:" + decision.RemoteAlbum.Release.Guid;
                    if (winners.TryGetValue(key, out var previous))
                    {
                        var comparison = decision.Rejections.Count().CompareTo(previous.Rejections.Count());
                        if (comparison > 0 || (comparison == 0 && decision.RemoteAlbum.Release.IndexerPriority >= previous.RemoteAlbum.Release.IndexerPriority))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        weights[key] = weights.Count;
                    }

                    winners[key] = decision;
                    changes.Add(MapDecision(decision, weights[key]));
                }

                await Send(new { type = "batch", processed, total, records = changes });
            }

            try
            {
                await Send(new { type = "searching" });
                var decisions = albumId.HasValue ?
                    await _releaseSearchService.AlbumSearchProgressive(albumId.Value, cancellationToken, Progress) :
                    await _releaseSearchService.ArtistSearchProgressive(artistId.Value, cancellationToken, Progress);
                cancellationToken.ThrowIfCancellationRequested();
                var prioritized = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);
                cancellationToken.ThrowIfCancellationRequested();
                await Send(new { type = "complete", records = MapDecisions(prioritized) });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Closing the interactive window cancels evaluation, not previously queued downloads.
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Progressive release search failed");
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Send(new { type = "error", message = "Search did not finish. Partial results are incomplete. Check the logs or start a new search." });
                }
            }
        }

        [HttpGet]
        public async Task<List<ReleaseResource>> GetReleases(int? albumId, int? artistId)
        {
            if (albumId.HasValue)
            {
                return await GetAlbumReleases(int.Parse(Request.Query["albumId"]));
            }

            if (artistId.HasValue)
            {
                return await GetArtistReleases(int.Parse(Request.Query["artistId"]));
            }

            return await GetRss();
        }

        private async Task<List<ReleaseResource>> GetAlbumReleases(int albumId)
        {
            try
            {
                var decisions = await _releaseSearchService.AlbumSearch(albumId, true, true, true);
                var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

                return MapDecisions(prioritizedDecisions);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Album search failed");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<List<ReleaseResource>> GetArtistReleases(int artistId)
        {
            try
            {
                var decisions = await _releaseSearchService.ArtistSearch(artistId, false, true, true);
                var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

                return MapDecisions(prioritizedDecisions);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Artist search failed");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<List<ReleaseResource>> GetRss()
        {
            var reports = await _rssFetcherAndParser.Fetch();
            var decisions = _downloadDecisionMaker.GetRssDecision(reports);
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

            return MapDecisions(prioritizedDecisions);
        }

        protected override ReleaseResource MapDecision(DownloadDecision decision, int initialWeight)
        {
            var resource = base.MapDecision(decision, initialWeight);
            _remoteAlbumCache.Set(GetCacheKey(resource), decision.RemoteAlbum, TimeSpan.FromMinutes(30));

            return resource;
        }

        private string GetCacheKey(ReleaseResource resource)
        {
            return string.Concat(resource.IndexerId, "_", resource.Guid);
        }
    }
}
