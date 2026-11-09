using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class LmStudioClientFixture
    {
        [TestCase(200, false)]
        [TestCase(503, false)]
        [TestCase(400, false)]
        [TestCase(400, true)]
        public async Task should_send_structured_request_to_configured_endpoint(int status, bool contextError)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var cancellation = new CancellationTokenSource(10000);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var settings = new LmStudioSettings { BaseUrl = $"http://127.0.0.1:{port}/v1/", Model = "local-model", ApiKey = "test-token", TimeoutSeconds = 5 };
            var payload = LmStudioMatcher.BuildRequest(JObject.Parse("{'files':[],'candidates':[]}"), settings);
            var expectedBody = payload.ToString(Newtonsoft.Json.Formatting.None);
            var server = Task.Run(async () =>
            {
                using var connection = await listener.AcceptTcpClientAsync(cancellation.Token);
                using var stream = connection.GetStream();
                var bytes = new byte[16384];
                var count = 0;
                var received = string.Empty;
                while (!received.Contains("\r\n\r\n") || count < received.IndexOf("\r\n\r\n", System.StringComparison.Ordinal) + 4 + Encoding.UTF8.GetByteCount(expectedBody))
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(count), cancellation.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    count += read;
                    received = Encoding.UTF8.GetString(bytes, 0, count);
                }

                var body = contextError ? "{\"error\":\"request exceeds available context size\"}" : "{\"choices\":[]}";
                var response = Encoding.UTF8.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                await stream.WriteAsync(response, cancellation.Token);
                return received;
            });

            if (status == 200)
            {
                var result = new LmStudioClient().Complete(payload, settings);
                Assert.That(result["choices"], Is.TypeOf<JArray>());
            }
            else if (contextError)
            {
                Assert.Throws<LmStudioContextException>(() => new LmStudioClient().Complete(payload, settings));
            }
            else
            {
                Assert.Throws<HttpRequestException>(() => new LmStudioClient().Complete(payload, settings));
            }

            var request = await server;
            Assert.That(request, Does.StartWith("POST /v1/chat/completions HTTP/1.1"));
            Assert.That(request, Does.Contain("Authorization: Bearer test-token"));
            Assert.That(request, Does.Contain("\"type\":\"json_schema\""));
            Assert.That(request, Does.Contain("\"model\":\"local-model\""));
            Assert.That(request, Does.Contain("\"reasoning_effort\":\"none\""));
        }
    }
}
