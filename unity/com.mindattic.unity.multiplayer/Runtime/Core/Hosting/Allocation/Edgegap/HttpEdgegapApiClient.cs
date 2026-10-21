using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// <see cref="IEdgegapApiClient"/> over Edgegap's v1 REST API (<c>https://api.edgegap.com/v1</c>:
    /// app-version upsert, deploy, status and stop). Verify the exact request and response shape
    /// against Edgegap's current docs before depending on this for a public launch -- a third-party
    /// API's schema can drift after this was written, which is also why the real-provider smoke test
    /// is opt-in rather than part of every build.
    /// </summary>
    public sealed class HttpEdgegapApiClient : IEdgegapApiClient, IDisposable
    {
        private readonly HttpClient client;
        private readonly bool ownsClient;

        /// <summary>Talks to Edgegap at <c>https://api.edgegap.com/v1/</c> with <paramref name="apiToken"/> sent as its <c>authorization</c> header.</summary>
        /// <exception cref="ArgumentException"><paramref name="apiToken"/> is empty.</exception>
        public HttpEdgegapApiClient(string apiToken)
            : this(BuildClient(apiToken), true)
        {
        }

        /// <summary>Uses <paramref name="client"/> as already configured (base address and auth header); disposes it only when <paramref name="ownsClient"/> is true.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
        public HttpEdgegapApiClient(HttpClient client, bool ownsClient = false)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            this.client = client;
            this.ownsClient = ownsClient;
        }

        private static HttpClient BuildClient(string apiToken)
        {
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                throw new ArgumentException("An Edgegap API token is required.", nameof(apiToken));
            }

            var http = new HttpClient { BaseAddress = new Uri("https://api.edgegap.com/v1/") };
            http.DefaultRequestHeaders.TryAddWithoutValidation("authorization", apiToken.Trim());
            return http;
        }

        /// <inheritdoc />
        public async Task EnsureAppVersionAsync(EdgegapAppVersionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string repository = request.ImageReference;
            string tag = "latest";
            int lastSlash = request.ImageReference.LastIndexOf('/');
            int colon = request.ImageReference.LastIndexOf(':');
            if (colon > lastSlash)
            {
                repository = request.ImageReference.Substring(0, colon);
                tag = request.ImageReference.Substring(colon + 1);
            }

            var ports = new List<object>();
            foreach (EdgegapPortSpec port in request.Ports)
            {
                ports.Add(new Dictionary<string, object>
                {
                    { "name", port.Name },
                    { "internal_port", port.Port },
                    { "protocol", port.Transport == TransportKind.Udp ? "UDP" : "TCP" },
                });
            }

            var env = new List<object>();
            foreach (KeyValuePair<string, string> variable in request.EnvironmentVariables)
            {
                env.Add(new Dictionary<string, object> { { "key", variable.Key }, { "value", variable.Value } });
            }

            var body = new Dictionary<string, object>
            {
                { "name", request.VersionName },
                { "docker_repository", repository },
                { "tag", tag },
                { "private", false },
                { "ports", ports },
                { "env", env },
            };

            await SendAsync(HttpMethod.Put, "app/" + Uri.EscapeDataString(request.AppName) + "/version", body, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EdgegapDeployment> DeployAsync(EdgegapDeployRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var body = new Dictionary<string, object>
            {
                { "app_name", request.AppName },
                { "version_name", request.VersionName },
            };
            if (!string.IsNullOrWhiteSpace(request.Region))
            {
                body["geo_location_preference"] = new List<object> { request.Region };
            }

            Dictionary<string, object> json = await SendAsync(HttpMethod.Post, "deploy", body, cancellationToken).ConfigureAwait(false);
            return ParseDeployment(json);
        }

        /// <inheritdoc />
        public async Task<EdgegapDeployment> GetDeploymentAsync(string requestId, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> json =
                await SendAsync(HttpMethod.Get, "status/" + Uri.EscapeDataString(requestId), null, cancellationToken).ConfigureAwait(false);
            return ParseDeployment(json);
        }

        /// <inheritdoc />
        public async Task StopDeploymentAsync(string requestId, CancellationToken cancellationToken = default)
        {
            await SendAsync(HttpMethod.Delete, "stop/" + Uri.EscapeDataString(requestId), null, cancellationToken, true).ConfigureAwait(false);
        }

        private static EdgegapDeployment ParseDeployment(Dictionary<string, object> json)
        {
            string requestId = AllocationJson.GetString(json, "request_id") ?? AllocationJson.GetString(json, "name") ?? string.Empty;
            string publicIp = AllocationJson.GetString(json, "public_ip");
            bool? running = AllocationJson.GetBool(json, "running");
            bool isRunning = running ?? string.Equals(AllocationJson.GetString(json, "current_status"), "READY", StringComparison.OrdinalIgnoreCase);

            var mappings = new List<EdgegapPortMapping>();
            Dictionary<string, object> ports = AllocationJson.GetObject(json, "ports");
            if (ports != null)
            {
                foreach (KeyValuePair<string, object> entry in ports)
                {
                    Dictionary<string, object> portJson = entry.Value as Dictionary<string, object>;
                    if (portJson == null)
                    {
                        continue;
                    }

                    long? external = AllocationJson.GetInteger(portJson, "external_port") ?? AllocationJson.GetInteger(portJson, "external");
                    if (!external.HasValue)
                    {
                        continue;
                    }

                    string protocol = AllocationJson.GetString(portJson, "protocol");
                    TransportKind transport = string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase) ? TransportKind.WebSocket : TransportKind.Udp;
                    mappings.Add(new EdgegapPortMapping(entry.Key, (int)external.Value, transport));
                }
            }

            return new EdgegapDeployment(requestId, publicIp, isRunning, mappings);
        }

        private async Task<Dictionary<string, object>> SendAsync(
            HttpMethod method, string path, object body, CancellationToken cancellationToken, bool allowNotFound = false)
        {
            using (var requestMessage = new HttpRequestMessage(method, path))
            {
                if (body != null)
                {
                    requestMessage.Content = new StringContent(AllocationJson.Write(body), Encoding.UTF8, "application/json");
                }

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new MatchAllocationException("Could not reach Edgegap: " + ex.Message, true, ex);
                }

                using (response)
                {
                    string text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return new Dictionary<string, object>();
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        bool transient = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                        throw new MatchAllocationException(
                            "Edgegap refused " + method + " " + path + " (HTTP " + (int)response.StatusCode + "): " + text, transient);
                    }

                    object parsed;
                    if (string.IsNullOrWhiteSpace(text) || !AllocationJson.TryParse(text, out parsed))
                    {
                        return new Dictionary<string, object>();
                    }

                    return AllocationJson.AsObject(parsed) ?? new Dictionary<string, object>();
                }
            }
        }

        /// <summary>Disposes the HttpClient if this object created it.</summary>
        public void Dispose()
        {
            if (ownsClient)
            {
                client.Dispose();
            }
        }
    }
}
