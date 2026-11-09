using System;
using System.Globalization;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public interface ILmStudioSettingsProvider
    {
        LmStudioSettings GetSettings();
    }

    public class LmStudioSettings
    {
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public string ApiKey { get; set; }
        public string ReasoningEffort { get; set; } = "none";
        public int MaxOutputTokens { get; set; } = 4096;
        public bool Automatic { get; set; }
        public int TimeoutSeconds { get; set; } = 180;
        public double MinimumConfidence { get; set; } = 0.95;
        public int MaxRequestCharacters { get; set; } = 180000;
        public int StageRequestCharacters { get; set; } = 18000;
        public int StageFileCount { get; set; } = 8;

        public void Validate()
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException("Set LIDARR_AI_BASE_URL to the LM Studio HTTP(S) API base URL, including /v1");
            }

            if (string.IsNullOrWhiteSpace(Model))
            {
                throw new ArgumentException("Set LIDARR_AI_MODEL to the model identifier shown in LM Studio");
            }

            if (TimeoutSeconds < 1 || TimeoutSeconds > 1800 ||
                !double.IsFinite(MinimumConfidence) || MinimumConfidence < 0 || MinimumConfidence > 1 ||
                MaxRequestCharacters < 1000 || MaxRequestCharacters > 1000000 || MaxOutputTokens < 64 || MaxOutputTokens > 32768 ||
                StageRequestCharacters < 1000 || StageRequestCharacters > 1000000 || StageFileCount < 1 || StageFileCount > 32)
            {
                throw new ArgumentException("Invalid AI timeout, confidence, or request size setting");
            }

            if (ReasoningEffort != "none" && ReasoningEffort != "minimal" && ReasoningEffort != "low" &&
                ReasoningEffort != "medium" && ReasoningEffort != "high" && ReasoningEffort != "xhigh")
            {
                throw new ArgumentException("Invalid LIDARR_AI_REASONING_EFFORT; use none, minimal, low, medium, high or xhigh");
            }
        }
    }

    public class LmStudioSettingsProvider : ILmStudioSettingsProvider
    {
        private readonly IConfigService _config;

        public LmStudioSettingsProvider(IConfigService config)
        {
            _config = config;
        }

        public LmStudioSettings GetSettings()
        {
            var mode = !string.IsNullOrWhiteSpace(_config.LmStudioMode) ? _config.LmStudioMode : Environment.GetEnvironmentVariable("LIDARR_AI_MODE") ?? "Review";
            if (!mode.Equals("Review", StringComparison.OrdinalIgnoreCase) && !mode.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("LIDARR_AI_MODE must be Review or Automatic");
            }

            var settings = new LmStudioSettings
            {
                BaseUrl = !string.IsNullOrWhiteSpace(_config.LmStudioBaseUrl) ? _config.LmStudioBaseUrl : Environment.GetEnvironmentVariable("LIDARR_AI_BASE_URL"),
                Model = !string.IsNullOrWhiteSpace(_config.LmStudioModel) ? _config.LmStudioModel : Environment.GetEnvironmentVariable("LIDARR_AI_MODEL"),
                ApiKey = Environment.GetEnvironmentVariable("LIDARR_AI_API_KEY"),
                ReasoningEffort = Environment.GetEnvironmentVariable("LIDARR_AI_REASONING_EFFORT") ?? "none",
                MaxOutputTokens = int.Parse(Environment.GetEnvironmentVariable("LIDARR_AI_MAX_OUTPUT_TOKENS") ?? "4096", CultureInfo.InvariantCulture),
                Automatic = mode.Equals("Automatic", StringComparison.OrdinalIgnoreCase),
                TimeoutSeconds = int.Parse(Environment.GetEnvironmentVariable("LIDARR_AI_TIMEOUT_SECONDS") ?? "180", CultureInfo.InvariantCulture),
                MinimumConfidence = double.Parse(Environment.GetEnvironmentVariable("LIDARR_AI_MIN_CONFIDENCE") ?? "0.95", CultureInfo.InvariantCulture),
                MaxRequestCharacters = int.Parse(Environment.GetEnvironmentVariable("LIDARR_AI_MAX_REQUEST_CHARACTERS") ?? "180000", CultureInfo.InvariantCulture),
                StageRequestCharacters = int.Parse(Environment.GetEnvironmentVariable("LIDARR_AI_STAGE_CHARACTERS") ?? "18000", CultureInfo.InvariantCulture),
                StageFileCount = int.Parse(Environment.GetEnvironmentVariable("LIDARR_AI_STAGE_FILES") ?? "8", CultureInfo.InvariantCulture)
            };
            if (_config.IsDefined("LmStudioConfidencePercent"))
            {
                settings.MinimumConfidence = _config.LmStudioConfidencePercent / 100.0;
            }

            settings.Validate();
            return settings;
        }
    }
}
