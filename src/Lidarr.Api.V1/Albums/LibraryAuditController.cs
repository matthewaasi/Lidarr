using System;
using System.Linq;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.LibraryAudit;

namespace Lidarr.Api.V1.Albums
{
    public class LibraryAuditAction
    {
        public string Action { get; set; }
        public string EvidenceKey { get; set; }
    }

    [V1ApiController("libraryaudit")]
    public class LibraryAuditController : Controller
    {
        private readonly ILibraryAuditService _service;

        public LibraryAuditController(ILibraryAuditService service)
        {
            _service = service;
        }

        [HttpGet]
        public object Get(string status = "pending", string kind = "all", int page = 1, int pageSize = 25)
        {
            if (!new[] { "pending", "ignored", "resolved", "all" }.Contains(status) ||
                !new[] { "version", "duplicate", "errors", "all" }.Contains(kind))
            {
                throw new BadRequestException("Unknown audit filter.");
            }

            pageSize = Math.Clamp(pageSize, 1, 100);
            var all = _service.Findings();
            var rows = all.Where(x => (status == "all" || x.Status == status) &&
                (kind == "all" || x.Kind == kind || (kind == "errors" && x.Kind.EndsWith("Error", StringComparison.Ordinal)))).ToList();
            page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(rows.Count / (double)pageSize)));
            return new
            {
                totalRecords = rows.Count,
                page,
                pageSize,
                records = rows.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new
                {
                    x.Id, x.EvidenceKey, x.Kind, x.Status, x.Updated,
                    evidence = JsonConvert.DeserializeObject<AuditEvidence>(x.Evidence)
                })
            };
        }

        [HttpDelete("{id:int}")]
        public object Delete(int id)
        {
            _service.DeleteFindings(new[] { id });
            return new { };
        }

        [HttpDelete("bulk")]
        public object DeleteSelected([FromBody] int[] ids)
        {
            _service.DeleteFindings(ids);
            return new { };
        }

        [HttpDelete("all")]
        public object DeleteAll()
        {
            _service.DeleteAllFindings();
            return new { };
        }

        [HttpPost("deletefiles/preview")]
        public object PreviewFiles([FromBody] AuditFileDeletion selection)
        {
            return _service.PreviewFindingFileDeletion(selection);
        }

        [HttpPost("deletefiles")]
        public object DeleteFiles([FromBody] AuditFileDeletion selection)
        {
            _service.DeleteFindingFiles(selection);
            return new { };
        }

        [HttpPost("{id:int}")]
        public object Decide(int id, [FromBody] LibraryAuditAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.EvidenceKey))
            {
                throw new BadRequestException("The current evidence key is required.");
            }

            _service.Decide(id, action.EvidenceKey, action.Action);
            return new { };
        }
    }
}
