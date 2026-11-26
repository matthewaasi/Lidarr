using System.Collections.Generic;
using System.Linq;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;

namespace Lidarr.Api.V1.Tracks
{
    [V1ApiController("retag")]
    public class RetagTrackController : Controller
    {
        private readonly IAudioTagService _audioTagService;

        private readonly IArtistFileMaintenanceService _maintenance;

        public RetagTrackController(IAudioTagService audioTagService, IArtistFileMaintenanceService maintenance)
        {
            _audioTagService = audioTagService;
            _maintenance = maintenance;
        }

        [HttpGet]
        public List<RetagTrackResource> GetTracks(int artistId, int? albumId)
        {
            if (albumId.HasValue)
            {
                return _audioTagService.GetRetagPreviewsByAlbum(albumId.Value).Where(x => x.Changes.Any()).ToResource();
            }

            var preview = _maintenance.Preview(artistId, ArtistFileOperation.Retag);
            if (preview.Skipped.Count == 0)
            {
                return preview.Retags.ToResource();
            }

            return _audioTagService.GetRetagPreviewsByArtist(artistId).Where(x => x.Changes.Any()).ToResource();
        }
    }
}
