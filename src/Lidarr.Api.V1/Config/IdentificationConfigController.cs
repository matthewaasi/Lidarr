using System;
using System.Globalization;
using FluentValidation;
using Lidarr.Http;
using Lidarr.Http.REST;
using Lidarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;

namespace Lidarr.Api.V1.Config
{
    public class IdentificationConfigResource : RestResource
    {
        public string MatchingEngine { get; set; }
        public string ManualMatchingEngine { get; set; }
        public int OriginalAlbumMatchPercent { get; set; }
        public int OriginalTrackMatchPercent { get; set; }
        public string LmStudioBaseUrl { get; set; }
        public string LmStudioModel { get; set; }
        public string LmStudioMode { get; set; }
        public int LmStudioConfidencePercent { get; set; }
    }

    [V1ApiController("config/identification")]
    public class IdentificationConfigController : ConfigController<IdentificationConfigResource>
    {
        public IdentificationConfigController(IConfigService config)
            : base(config)
        {
            SharedValidator.RuleFor(x => x.MatchingEngine).Must(x => x == "Original" || x == "LmStudio" || x == "Hybrid");
            SharedValidator.RuleFor(x => x.ManualMatchingEngine).Must(x => x is null or "Inherit" or "Original" or "LmStudio" or "Hybrid");
            SharedValidator.RuleFor(x => x.OriginalAlbumMatchPercent).InclusiveBetween(1, 100);
            SharedValidator.RuleFor(x => x.OriginalTrackMatchPercent).InclusiveBetween(1, 100);
            SharedValidator.RuleFor(x => x.LmStudioConfidencePercent).InclusiveBetween(1, 100);
            SharedValidator.RuleFor(x => x.LmStudioMode).Must(x => x == "Review" || x == "Automatic");
            SharedValidator.RuleFor(x => x.LmStudioBaseUrl).Must(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
                .When(x => x.MatchingEngine != "Original" || (x.ManualMatchingEngine ?? config.ManualMatchingEngine) is "LmStudio" or "Hybrid");
            SharedValidator.RuleFor(x => x.LmStudioModel).NotEmpty().When(x => x.MatchingEngine != "Original" || (x.ManualMatchingEngine ?? config.ManualMatchingEngine) is "LmStudio" or "Hybrid");
        }

        protected override IdentificationConfigResource ToResource(IConfigService model)
        {
            var confidence = Environment.GetEnvironmentVariable("LIDARR_AI_MIN_CONFIDENCE");
            return new IdentificationConfigResource
            {
                MatchingEngine = model.MatchingEngine,
                ManualMatchingEngine = model.ManualMatchingEngine,
                OriginalAlbumMatchPercent = model.OriginalAlbumMatchPercent,
                OriginalTrackMatchPercent = model.OriginalTrackMatchPercent,
                LmStudioBaseUrl = string.IsNullOrWhiteSpace(model.LmStudioBaseUrl) ? Environment.GetEnvironmentVariable("LIDARR_AI_BASE_URL") ?? "http://10.0.0.88:1234/v1" : model.LmStudioBaseUrl,
                LmStudioModel = string.IsNullOrWhiteSpace(model.LmStudioModel) ? Environment.GetEnvironmentVariable("LIDARR_AI_MODEL") ?? string.Empty : model.LmStudioModel,
                LmStudioMode = string.IsNullOrWhiteSpace(model.LmStudioMode) ? Environment.GetEnvironmentVariable("LIDARR_AI_MODE") ?? "Review" : model.LmStudioMode,
                LmStudioConfidencePercent = model.IsDefined("LmStudioConfidencePercent") || !double.TryParse(confidence, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? model.LmStudioConfidencePercent : (int)Math.Round(value * 100)
            };
        }

        [RestPutById]
        public override ActionResult<IdentificationConfigResource> SaveConfig([FromBody] IdentificationConfigResource resource)
        {
            // Explicitly saving the default 95 must also override a different Docker confidence value.
            _configService.LmStudioConfidencePercent = resource.LmStudioConfidencePercent;
            return base.SaveConfig(resource);
        }
    }
}
