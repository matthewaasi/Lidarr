using FluentValidation;
using Lidarr.Http;
using NzbDrone.Core.Configuration;

namespace Lidarr.Api.V1.Config
{
    [V1ApiController("config/downloadclient")]
    public class DownloadClientConfigController : ConfigController<DownloadClientConfigResource>
    {
        public DownloadClientConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.QueueRefreshInterval).InclusiveBetween(1, 1440);
        }

        protected override DownloadClientConfigResource ToResource(IConfigService model)
        {
            return DownloadClientConfigResourceMapper.ToResource(model);
        }
    }
}
