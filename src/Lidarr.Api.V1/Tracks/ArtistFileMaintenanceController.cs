using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;

namespace Lidarr.Api.V1.Tracks
{
    [V1ApiController("artistfilemaintenance")]
    public class ArtistFileMaintenanceController : Controller
    {
        private readonly IArtistFileMaintenanceService _maintenance;
        private readonly IArtistFileStatusService _status;
        private readonly IManageCommandQueue _commands;

        public ArtistFileMaintenanceController(IArtistFileMaintenanceService maintenance, IArtistFileStatusService status, IManageCommandQueue commands)
        {
            _maintenance = maintenance;
            _status = status;
            _commands = commands;
        }

        [HttpGet]
        public List<ArtistFileStatus> GetStatuses()
        {
            var statuses = _status.All();
            foreach (var status in statuses)
            {
                status.Settings = null;
            }

            return statuses;
        }

        [HttpGet("preview")]
        public IActionResult Preview(int artistId, ArtistFileOperation operation)
        {
            if (artistId <= 0 || !Enum.IsDefined(operation))
            {
                return BadRequest();
            }

            var preview = _maintenance.Preview(artistId, operation);
            return Ok(new
            {
                preview.Key,
                preview.Skipped,
                Renames = preview.Renames.ToResource(),
                Retags = preview.Retags.ToResource()
            });
        }

        [HttpPost("apply")]
        public IActionResult Apply([FromBody] ApplyArtistFilePreviewsCommand command)
        {
            if (command == null || !Enum.IsDefined(command.Operation) ||
                command.Reviews == null || command.Reviews.Count == 0 ||
                command.Reviews.Any(x => x.Key <= 0 || x.Value?.Length != 64))
            {
                return BadRequest();
            }

            var queued = _commands.Push(command, CommandPriority.Normal, CommandTrigger.Manual);
            return Accepted(new { queued.Id });
        }
    }
}
