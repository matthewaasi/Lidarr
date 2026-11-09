using FluentValidation;
using Lidarr.Http;
using Lidarr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Lidarr.Api.V1.Config
{
    public class UniqueSinglesConfigResource : RestResource
    {
        public bool UniqueSinglesEnabled { get; set; }
        public bool UniqueSinglesTitleDuration { get; set; }
        public int UniqueSinglesToleranceMs { get; set; }
    }

    [V1ApiController("config/uniquesingles")]
    public class UniqueSinglesConfigController : ConfigController<UniqueSinglesConfigResource>
    {
        public UniqueSinglesConfigController(IConfigService config)
            : base(config)
        {
            SharedValidator.RuleFor(x => x.UniqueSinglesToleranceMs).InclusiveBetween(0, 10000);
        }

        protected override UniqueSinglesConfigResource ToResource(IConfigService model)
        {
            return new UniqueSinglesConfigResource
            {
                UniqueSinglesEnabled = model.UniqueSinglesEnabled,
                UniqueSinglesTitleDuration = model.UniqueSinglesTitleDuration,
                UniqueSinglesToleranceMs = model.UniqueSinglesToleranceMs
            };
        }
    }
}
