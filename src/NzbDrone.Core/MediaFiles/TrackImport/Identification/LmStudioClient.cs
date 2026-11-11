using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public interface ILmStudioClient
    {
        JObject Complete(JObject payload, LmStudioSettings settings);
    }

    public class LmStudioClient : ILmStudioClient
    {
        private static readonly SemaphoreSlim InferenceGate = new SemaphoreSlim(1, 1);
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };

        public JObject Complete(JObject payload, LmStudioSettings settings)
        {
            settings.Validate();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            InferenceGate.Wait(cancellation.Token);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl.TrimEnd('/') + "/chat/completions");
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }

                using var response = Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation.Token).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    // Do not include server response bodies, which can contain credentials or full prompts.
                    throw new HttpRequestException($"LM Studio returned HTTP {(int)response.StatusCode}");
                }

                return JObject.Parse(response.Content.ReadAsStringAsync(cancellation.Token).GetAwaiter().GetResult());
            }
            finally
            {
                InferenceGate.Release();
            }
        }
    }
}
