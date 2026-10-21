using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using F = Unity.Multiplayer.Core.RendezvousProtocol.Fields;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Speaks the self-hosted rendezvous service's HTTP API (<see cref="RendezvousProtocol"/>): register a
    /// dedicated server, keep its registration alive, resolve a code a player typed, and unregister.
    /// Thread-safe; every call is one request.
    /// </summary>
    public sealed class RendezvousClient : IDisposable
    {
        private readonly IRendezvousHttp http;
        private readonly bool ownsHttp;

        /// <summary>Talks to the service at <paramref name="baseUrl"/> (http or https) over <see cref="HttpClientRendezvousHttp"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="baseUrl"/> is not an absolute http(s) URL.</exception>
        public RendezvousClient(string baseUrl)
            : this(new HttpClientRendezvousHttp(ParseBaseUrl(baseUrl)), true)
        {
        }

        /// <summary>Talks to the service through <paramref name="http"/>; disposes it only when <paramref name="ownsHttp"/> is true.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="http"/> is null.</exception>
        public RendezvousClient(IRendezvousHttp http, bool ownsHttp = false)
        {
            this.http = http ?? throw new ArgumentNullException(nameof(http));
            this.ownsHttp = ownsHttp;
        }

        /// <summary>Parses a rendezvous base URL: absolute, http or https.</summary>
        /// <exception cref="ArgumentException">It is not.</exception>
        public static Uri ParseBaseUrl(string baseUrl)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(baseUrl)
                || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new ArgumentException("The rendezvous URL must be an absolute http:// or https:// URL; got '" + baseUrl + "'.", nameof(baseUrl));
            }

            return uri;
        }

        /// <summary>True when <paramref name="baseUrl"/> is an absolute http(s) URL.</summary>
        public static bool IsValidBaseUrl(string baseUrl)
        {
            Uri uri;
            return !string.IsNullOrWhiteSpace(baseUrl)
                && Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out uri)
                && (uri.Scheme == "http" || uri.Scheme == "https");
        }

        /// <summary>
        /// Registers a server and returns its code and private token. A null or empty <paramref name="address"/>
        /// asks the service to use the address the request came from.
        /// </summary>
        /// <exception cref="RendezvousException">The service refused (wrong secret, bad endpoint, full) or could not be reached.</exception>
        public async Task<RendezvousRegistration> RegisterAsync(
            string secret, string address, ushort port, TransportKind transport, CancellationToken cancellationToken = default)
        {
            var body = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>(F.Secret, secret),
                new KeyValuePair<string, object>(F.Address, string.IsNullOrWhiteSpace(address) ? null : address.Trim()),
                new KeyValuePair<string, object>(F.Port, (int)port),
                new KeyValuePair<string, object>(F.Transport, RendezvousProtocol.ToWire(transport)),
            };

            RendezvousHttpResponse response = await Send("POST", RendezvousProtocol.RegisterPath, RendezvousJson.Write(body), null, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw Failure("register", response);
            }

            Dictionary<string, object> json = ParseBody(response, "register");
            JoinCode code = ParseCode(json, "register");
            string token = RendezvousJson.GetString(json, F.Token);
            if (string.IsNullOrEmpty(token))
            {
                throw new RendezvousException(response.StatusCode, "The rendezvous service's register reply has no token.");
            }

            long? ttl = RendezvousJson.GetInteger(json, F.TtlSeconds);
            return new RendezvousRegistration(code, token, (int)Math.Max(1, ttl ?? 60));
        }

        /// <summary>Renews a registration for another TTL. False when the service no longer knows it (404): register again.</summary>
        /// <exception cref="RendezvousException">Any other refusal, or the service could not be reached.</exception>
        public async Task<bool> HeartbeatAsync(JoinCode code, string token, CancellationToken cancellationToken = default)
        {
            RequireCode(code);
            var body = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>(F.Code, code.Value),
                new KeyValuePair<string, object>(F.Token, token),
            };

            RendezvousHttpResponse response = await Send("POST", RendezvousProtocol.HeartbeatPath, RendezvousJson.Write(body), null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == 404)
            {
                return false;
            }

            if (!response.IsSuccess)
            {
                throw Failure("heartbeat", response);
            }

            return true;
        }

        /// <summary>The server <paramref name="code"/> leads to, or null when the service does not know the code (404, or 400 for a malformed one).</summary>
        /// <exception cref="RendezvousException">Any other refusal (such as 429), or the service could not be reached.</exception>
        public async Task<JoinTarget> ResolveAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            RequireCode(code);
            RendezvousHttpResponse response = await Send("GET", RendezvousProtocol.ResolvePath(code), null, null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == 404 || response.StatusCode == 400)
            {
                return null;
            }

            if (!response.IsSuccess)
            {
                throw Failure("resolve", response);
            }

            Dictionary<string, object> json = ParseBody(response, "resolve");
            string address = RendezvousJson.GetString(json, F.Address);
            long? port = RendezvousJson.GetInteger(json, F.Port);
            TransportKind transport;
            if (string.IsNullOrWhiteSpace(address) || port == null || port < 1 || port > ushort.MaxValue
                || !RendezvousProtocol.TryParseTransport(RendezvousJson.GetString(json, F.Transport), out transport))
            {
                throw new RendezvousException(response.StatusCode, "The rendezvous service's resolve reply is not a usable address: " + response.Body);
            }

            try
            {
                return JoinTarget.ForEndpoint(address, (ushort)port.Value, transport);
            }
            catch (ArgumentException ex)
            {
                throw new RendezvousException(response.StatusCode, "The rendezvous service resolved to an unusable address: " + ex.Message);
            }
        }

        /// <summary>Deletes a registration. False when the service did not know it (404).</summary>
        /// <exception cref="RendezvousException">Any other refusal, or the service could not be reached.</exception>
        public async Task<bool> UnregisterAsync(JoinCode code, string token, CancellationToken cancellationToken = default)
        {
            RequireCode(code);
            var headers = new Dictionary<string, string> { { RendezvousProtocol.TokenHeader, token ?? string.Empty } };
            RendezvousHttpResponse response = await Send("DELETE", RendezvousProtocol.RegistrationPath(code), null, headers, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == 404)
            {
                return false;
            }

            if (!response.IsSuccess)
            {
                throw Failure("unregister", response);
            }

            return true;
        }

        /// <summary>Disposes the HTTP transport if this client owns it.</summary>
        public void Dispose()
        {
            if (ownsHttp && http is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private async Task<RendezvousHttpResponse> Send(
            string method, string path, string body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            try
            {
                return await http.SendAsync(method, path, body, headers, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is RendezvousException))
            {
                // HttpRequestException, a timeout's TaskCanceledException, a socket error...
                throw new RendezvousException(0, "Could not reach the rendezvous service: " + ex.Message, ex);
            }
        }

        private static Dictionary<string, object> ParseBody(RendezvousHttpResponse response, string operation)
        {
            Dictionary<string, object> json;
            if (!RendezvousJson.TryParseObject(response.Body, out json))
            {
                throw new RendezvousException(response.StatusCode, "The rendezvous service's " + operation + " reply is not JSON: " + response.Body);
            }

            return json;
        }

        private static JoinCode ParseCode(Dictionary<string, object> json, string operation)
        {
            JoinCode code;
            if (!JoinCode.TryParse(RendezvousJson.GetString(json, F.Code), out code))
            {
                throw new RendezvousException(200, "The rendezvous service's " + operation + " reply has no valid code.");
            }

            return code;
        }

        private static RendezvousException Failure(string operation, RendezvousHttpResponse response)
        {
            Dictionary<string, object> json;
            string error = RendezvousJson.TryParseObject(response.Body, out json) ? RendezvousJson.GetString(json, F.Error) : null;
            string message = "The rendezvous service refused " + operation + " (HTTP " + response.StatusCode + ")"
                + (string.IsNullOrWhiteSpace(error) ? "." : ": " + error);
            return new RendezvousException(response.StatusCode, message);
        }

        private static void RequireCode(JoinCode code)
        {
            if (code.IsEmpty)
            {
                throw new ArgumentException("A join code is required.", nameof(code));
            }
        }
    }

    /// <summary>A live registration: the code players type and the private token that renews or deletes it.</summary>
    public sealed class RendezvousRegistration
    {
        /// <summary>Creates a registration record.</summary>
        public RendezvousRegistration(JoinCode code, string token, int ttlSeconds)
        {
            Code = code;
            Token = token;
            TtlSeconds = ttlSeconds;
        }

        /// <summary>The code players type.</summary>
        public JoinCode Code { get; }

        /// <summary>The private token; never show it to players.</summary>
        public string Token { get; }

        /// <summary>Seconds the registration lives without a heartbeat.</summary>
        public int TtlSeconds { get; }
    }

    /// <summary>The rendezvous service refused a request, or could not be reached (<see cref="StatusCode"/> 0).</summary>
    public sealed class RendezvousException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public RendezvousException(int statusCode, string message, Exception inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
        }

        /// <summary>The HTTP status, or 0 when no response came back.</summary>
        public int StatusCode { get; }

        /// <summary>True when trying again later may work: unreachable, 429 or a 5xx.</summary>
        public bool IsTransient
        {
            get { return StatusCode == 0 || StatusCode == 429 || StatusCode >= 500; }
        }
    }
}
