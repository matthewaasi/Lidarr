using System.Linq;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Music.Singles;

namespace Lidarr.Api.V1.Albums
{
    public class SinglesReviewAction
    {
        public string Action { get; set; }
        public string Fingerprint { get; set; }
    }

    [V1ApiController("singlesreview")]
    public class SinglesReviewController : Controller
    {
        private readonly IUniqueSinglesService _service;
        private readonly IConfigService _config;

        public SinglesReviewController(IUniqueSinglesService service, IConfigService config)
        {
            _service = service;
            _config = config;
        }

        [HttpGet]
        public object Get(string status = "pending", int page = 1, int pageSize = 25)
        {
            var result = _service.Page(status, page, pageSize);
            return new
            {
                enabled = _config.UniqueSinglesEnabled,
                result.TotalRecords,
                result.Page,
                result.PageSize,
                records = result.Records.Select(row => new
                {
                    row.Id, row.AlbumId, row.ArtistId, row.Status, row.AutoUnmonitored, row.Updated,
                    evidence = JsonConvert.DeserializeObject<SinglesEvidence>(row.Evidence)
                })
            };
        }

        [HttpPost("{id:int}")]
        public object Decide(int id, [FromBody] SinglesReviewAction action)
        {
            _service.Decide(id, action.Action, action.Fingerprint);
            return new { };
        }
    }
}
