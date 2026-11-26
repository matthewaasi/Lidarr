using System.Collections.Generic;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;

namespace Lidarr.Api.V1.Tracks
{
    [V1ApiController("rename")]
    public class RenameTrackController : Controller
    {
        private readonly IRenameTrackFileService _renameTrackFileService;

        private readonly IArtistFileMaintenanceService _maintenance;

        public RenameTrackController(IRenameTrackFileService renameTrackFileService, IArtistFileMaintenanceService maintenance)
        {
            _renameTrackFileService = renameTrackFileService;
            _maintenance = maintenance;
        }

        [HttpGet]
        public List<RenameTrackResource> GetTracks(int artistId, int? albumId)
        {
            if (albumId.HasValue)
            {
                return _renameTrackFileService.GetRenamePreviews(artistId, albumId.Value).ToResource();
            }

            var preview = _maintenance.Preview(artistId, ArtistFileOperation.Rename);
            if (preview.Skipped.Count == 0)
            {
                return preview.Renames.ToResource();
            }

            return _renameTrackFileService.GetRenamePreviews(artistId).ToResource();
        }
    }
}
