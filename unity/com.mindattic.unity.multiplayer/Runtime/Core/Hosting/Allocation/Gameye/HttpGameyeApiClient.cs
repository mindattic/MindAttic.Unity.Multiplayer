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
    /// <see cref="IGameyeApiClient"/> over Gameye's REST API (<c>https://api.gameye.com</c>: image
    /// registration plus session start/stop). Verify the exact request and response shape against
    /// Gameye's current docs before depending on this for a public launch -- a third-party API's
    /// schema can drift after this was written, which is also why the real-provider smoke test is
    /// opt-in rather than part of every build.
    /// </summary>
    public sealed class HttpGameyeApiClient : IGameyeApiClient, IDisposable
    {
        private readonly HttpClient client;
        private readonly bool ownsClient;

        /// <summary>Talks to Gameye at <c>https://api.gameye.com/</c> with <paramref name="apiKey"/> as a bearer token.</summary>
        /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
        public HttpGameyeApiClient(string apiKey)
            : this(BuildClient(apiKey), true)
        {
        }

        /// <summary>Uses <paramref name="client"/> as already configured (base address and auth header); disposes it only when <paramref name="ownsClient"/> is true.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
        public HttpGameyeApiClient(HttpClient client, bool ownsClient = false)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            this.client = client;
            this.ownsClient = ownsClient;
        }

        private static HttpClient BuildClient(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("A Gameye API key is required.", nameof(apiKey));
            }

            var http = new HttpClient { BaseAddress = new Uri("https://api.gameye.com/") };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
            return http;
        }

        /// <inheritdoc />
        public async Task RegisterImageAsync(GameyeImageRegistration registration, CancellationToken cancellationToken = default)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            var body = new Dictionary<string, object>
            {
                { "image", registration.ImageReference },
            };
            if (!string.IsNullOrWhiteSpace(registration.RegistryUsername))
            {
                body["registry_username"] = registration.RegistryUsername;
                body["registry_password"] = registration.RegistryPassword;
            }

            await SendAsync(HttpMethod.Put, "images/" + Uri.EscapeDataString(registration.ImageName), body, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<GameyeSession> StartSessionAsync(GameyeStartSessionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var ports = new List<object>();
            foreach (GameyePortSpec port in request.Ports)
            {
                ports.Add(new Dictionary<string, object>
                {
                    { "name", port.Name },
                    { "port", port.Port },
                    { "protocol", port.Transport == TransportKind.Udp ? "UDP" : "TCP" },
                });
            }

            var body = new Dictionary<string, object>
            {
                { "image", request.ImageName },
                { "match_id", request.MatchId },
                { "ports", ports },
                { "env", ToObjectDictionary(request.EnvironmentVariables) },
            };

            Dictionary<string, object> json = await SendAsync(
                HttpMethod.Post, "locations/" + Uri.EscapeDataString(request.Location) + "/sessions", body, cancellationToken).ConfigureAwait(false);
            return ParseSession(json);
        }

        /// <inheritdoc />
        public async Task<GameyeSession> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> json =
                await SendAsync(HttpMethod.Get, "sessions/" + Uri.EscapeDataString(sessionId), null, cancellationToken).ConfigureAwait(false);
            return ParseSession(json);
        }

        /// <inheritdoc />
        public async Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            await SendAsync(HttpMethod.Delete, "sessions/" + Uri.EscapeDataString(sessionId), null, cancellationToken, true).ConfigureAwait(false);
        }

        private static Dictionary<string, object> ToObjectDictionary(IReadOnlyDictionary<string, string> source)
        {
            var result = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> pair in source)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static GameyeSession ParseSession(Dictionary<string, object> json)
        {
            string sessionId = AllocationJson.GetString(json, "id") ?? string.Empty;
            string ip = AllocationJson.GetString(json, "ip");
            bool? ready = AllocationJson.GetBool(json, "ready");
            bool isReady = ready ?? string.Equals(AllocationJson.GetString(json, "status"), "running", StringComparison.OrdinalIgnoreCase);

            var mappings = new List<GameyePortMapping>();
            List<object> ports = AllocationJson.GetArray(json, "ports");
            if (ports != null)
            {
                foreach (object entry in ports)
                {
                    Dictionary<string, object> portJson = entry as Dictionary<string, object>;
                    if (portJson == null)
                    {
                        continue;
                    }

                    string name = AllocationJson.GetString(portJson, "name");
                    long? port = AllocationJson.GetInteger(portJson, "port");
                    if (string.IsNullOrEmpty(name) || !port.HasValue)
                    {
                        continue;
                    }

                    string protocol = AllocationJson.GetString(portJson, "protocol");
                    TransportKind transport = string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase) ? TransportKind.WebSocket : TransportKind.Udp;
                    mappings.Add(new GameyePortMapping(name, (int)port.Value, transport));
                }
            }

            return new GameyeSession(sessionId, ip, isReady, mappings);
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
                    throw new MatchAllocationException("Could not reach Gameye: " + ex.Message, true, ex);
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
                            "Gameye refused " + method + " " + path + " (HTTP " + (int)response.StatusCode + "): " + text, transient);
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
