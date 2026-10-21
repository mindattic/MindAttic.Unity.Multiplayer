using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// <see cref="IGameLiftApiClient"/> over the GameLift service API (AWS JSON 1.1 RPC,
    /// <c>https://gamelift.&lt;region&gt;.amazonaws.com/</c>, one <c>X-Amz-Target: GameLift.&lt;Action&gt;</c>
    /// per call), signed with <see cref="GameLiftSigV4"/> using the same long-term access key mp-host's
    /// GameLiftProvisioner uses to create the fleet (<c>HostingCredentialStore.AwsCreds</c>). Field
    /// names follow GameLift's publicly documented API as of this writing; as with the Edgegap/Gameye
    /// clients, the opt-in real-provider smoke test (docs/articles/hosting/gamelift.md) is what
    /// actually guards this against drift.
    /// </summary>
    public sealed class HttpGameLiftApiClient : IGameLiftApiClient, IDisposable
    {
        private const string TargetPrefix = "GameLift.";
        private const string ContentType = "application/x-amz-json-1.1";

        private readonly HttpClient client;
        private readonly bool ownsClient;
        private readonly string accessKeyId;
        private readonly string secretAccessKey;
        private readonly string region;

        /// <summary>Talks to GameLift at <c>https://gamelift.&lt;region&gt;.amazonaws.com/</c>, signing every call with <paramref name="accessKeyId"/>/<paramref name="secretAccessKey"/>.</summary>
        /// <exception cref="ArgumentException">A credential or the region is empty.</exception>
        public HttpGameLiftApiClient(string accessKeyId, string secretAccessKey, string region)
            : this(BuildClient(region), accessKeyId, secretAccessKey, region, true)
        {
        }

        /// <summary>Uses <paramref name="client"/> as already configured (base address); disposes it only when <paramref name="ownsClient"/> is true.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
        /// <exception cref="ArgumentException">A credential or the region is empty.</exception>
        public HttpGameLiftApiClient(HttpClient client, string accessKeyId, string secretAccessKey, string region, bool ownsClient = false)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (string.IsNullOrWhiteSpace(accessKeyId))
            {
                throw new ArgumentException("An AWS access key id is required.", nameof(accessKeyId));
            }

            if (string.IsNullOrWhiteSpace(secretAccessKey))
            {
                throw new ArgumentException("An AWS secret access key is required.", nameof(secretAccessKey));
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                throw new ArgumentException("An AWS region is required.", nameof(region));
            }

            this.client = client;
            this.ownsClient = ownsClient;
            this.accessKeyId = accessKeyId.Trim();
            this.secretAccessKey = secretAccessKey;
            this.region = region.Trim();
        }

        private static HttpClient BuildClient(string region)
        {
            if (string.IsNullOrWhiteSpace(region))
            {
                throw new ArgumentException("An AWS region is required.", nameof(region));
            }

            return new HttpClient { BaseAddress = new Uri("https://gamelift." + region.Trim() + ".amazonaws.com/") };
        }

        /// <inheritdoc />
        public async Task<GameLiftGameSession> CreateGameSessionAsync(GameLiftCreateGameSessionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var body = new Dictionary<string, object> { { "MaximumPlayerSessionCount", (long)request.MaximumPlayerSessionCount } };
            if (!string.IsNullOrWhiteSpace(request.AliasId))
            {
                body["AliasId"] = request.AliasId;
            }
            else
            {
                body["FleetId"] = request.FleetId;
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                body["Name"] = request.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.IdempotencyToken))
            {
                body["IdempotencyToken"] = request.IdempotencyToken;
            }

            if (request.GameProperties != null && request.GameProperties.Count > 0)
            {
                var properties = new List<object>();
                foreach (KeyValuePair<string, string> property in request.GameProperties)
                {
                    properties.Add(new Dictionary<string, object> { { "Key", property.Key }, { "Value", property.Value } });
                }

                body["GameProperties"] = properties;
            }

            Dictionary<string, object> json = await SendAsync("CreateGameSession", body, cancellationToken).ConfigureAwait(false);
            return ParseGameSession(AllocationJson.GetObject(json, "GameSession") ?? json);
        }

        /// <inheritdoc />
        public async Task<GameLiftGameSession> DescribeGameSessionAsync(string gameSessionId, CancellationToken cancellationToken = default)
        {
            var body = new Dictionary<string, object> { { "GameSessionId", gameSessionId } };
            Dictionary<string, object> json = await SendAsync("DescribeGameSessions", body, cancellationToken).ConfigureAwait(false);
            List<object> sessions = AllocationJson.GetArray(json, "GameSessions");
            if (sessions == null || sessions.Count == 0)
            {
                throw new MatchAllocationException("GameLift no longer knows game session " + gameSessionId + ".");
            }

            return ParseGameSession(AllocationJson.AsObject(sessions[0]));
        }

        /// <inheritdoc />
        public async Task<GameLiftPlayerSession> CreatePlayerSessionAsync(string gameSessionId, string playerId, string playerData, CancellationToken cancellationToken = default)
        {
            var body = new Dictionary<string, object>
            {
                { "GameSessionId", gameSessionId },
                { "PlayerId", playerId },
            };
            if (!string.IsNullOrEmpty(playerData))
            {
                body["PlayerData"] = playerData;
            }

            Dictionary<string, object> json = await SendAsync("CreatePlayerSession", body, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> session = AllocationJson.GetObject(json, "PlayerSession") ?? json;
            string playerSessionId = AllocationJson.GetString(session, "PlayerSessionId") ?? string.Empty;
            string status = AllocationJson.GetString(session, "Status") ?? string.Empty;
            return new GameLiftPlayerSession(playerSessionId, status);
        }

        private static GameLiftGameSession ParseGameSession(Dictionary<string, object> json)
        {
            string id = AllocationJson.GetString(json, "GameSessionId") ?? string.Empty;
            string status = AllocationJson.GetString(json, "Status") ?? string.Empty;
            string ip = AllocationJson.GetString(json, "IpAddress");
            string dns = AllocationJson.GetString(json, "DnsName");
            long? port = AllocationJson.GetInteger(json, "Port");
            return new GameLiftGameSession(id, status, ip, port.HasValue ? (int)port.Value : 0, dns);
        }

        private async Task<Dictionary<string, object>> SendAsync(string action, object body, CancellationToken cancellationToken)
        {
            string json = AllocationJson.Write(body);
            string host = client.BaseAddress.Host;
            var extraHeaders = new Dictionary<string, string>
            {
                { "content-type", ContentType },
                { "x-amz-target", TargetPrefix + action },
            };
            GameLiftSigV4.SignedHeaders signed = GameLiftSigV4.Sign(
                "POST", host, "/", json, extraHeaders, accessKeyId, secretAccessKey, region, "gamelift", DateTime.UtcNow);

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, string.Empty))
            {
                requestMessage.Content = new StringContent(json, Encoding.UTF8, ContentType);
                requestMessage.Headers.Host = host;
                requestMessage.Headers.Add("X-Amz-Date", signed.AmzDate);
                requestMessage.Headers.Add("X-Amz-Target", TargetPrefix + action);
                requestMessage.Headers.TryAddWithoutValidation("Authorization", signed.Authorization);

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
                    throw new MatchAllocationException("Could not reach GameLift: " + ex.Message, true, ex);
                }

                using (response)
                {
                    string text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        bool transient = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                        string message = text;
                        object errorParsed;
                        if (AllocationJson.TryParse(text, out errorParsed))
                        {
                            message = AllocationJson.GetString(AllocationJson.AsObject(errorParsed), "message") ?? text;
                        }

                        throw new MatchAllocationException(
                            "GameLift refused " + action + " (HTTP " + (int)response.StatusCode + "): " + message, transient);
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
