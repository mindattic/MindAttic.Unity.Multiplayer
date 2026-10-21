using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The self-hosted rendezvous service's wire protocol (RFC 0001): its routes, header and JSON field
    /// names, shared by the server (<c>server/Rendezvous</c>) and the adapter's client (UM-T13) so the
    /// two cannot drift apart. The service maps a <see cref="JoinCode"/> to a dedicated server's
    /// address and port; it does not get through NAT, so listen servers use Relay instead.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <item><term><c>POST /v1/register</c></term><description>Body <c>{secret, address?, port, transport?}</c>.
    /// 201 with <c>{code, token, ttlSeconds, expiresAt}</c>. An omitted address means "the address this
    /// request came from". 401 for a wrong secret, 400 for a bad endpoint, 503 when the store is full.</description></item>
    /// <item><term><c>POST /v1/heartbeat</c></term><description>Body <c>{code, token}</c>. 200 with the new
    /// <c>{code, ttlSeconds, expiresAt}</c>; 404 for an unknown or expired code or the wrong token.</description></item>
    /// <item><term><c>GET /v1/resolve/{code}</c></term><description>200 with <c>{code, address, port, transport}</c>;
    /// 400 (with <c>error</c>) for input that is not a join code, 404 for an unknown or expired one.</description></item>
    /// <item><term><c>DELETE /v1/register/{code}</c></term><description>Header <see cref="TokenHeader"/>. 204, or
    /// 404 for an unknown code or the wrong token.</description></item>
    /// <item><term><c>GET /healthz</c></term><description>200 "Healthy" while the service runs; never rate limited.</description></item>
    /// </list>
    /// Every route may answer 429 when the caller's IP is over its rate limit. Error bodies are
    /// <c>{error}</c>.
    /// </remarks>
    public static class RendezvousProtocol
    {
        /// <summary>The protocol version, the first path segment of every API route.</summary>
        public const string Version = "v1";

        /// <summary><c>POST</c>: register a server and get its code.</summary>
        public const string RegisterPath = "/v1/register";

        /// <summary><c>POST</c>: keep a registration alive for another TTL.</summary>
        public const string HeartbeatPath = "/v1/heartbeat";

        /// <summary>Prefix of <c>GET /v1/resolve/{code}</c>; see <see cref="ResolvePath"/>.</summary>
        public const string ResolvePathPrefix = "/v1/resolve/";

        /// <summary>Prefix of <c>DELETE /v1/register/{code}</c>; see <see cref="RegistrationPath"/>.</summary>
        public const string RegistrationPathPrefix = "/v1/register/";

        /// <summary><c>GET</c>: liveness probe for Docker and load balancers.</summary>
        public const string HealthPath = "/healthz";

        /// <summary>The header that carries a registration's token on <c>DELETE</c>.</summary>
        public const string TokenHeader = "X-Rendezvous-Token";

        /// <summary>Wire value of <see cref="TransportKind.Udp"/>.</summary>
        public const string TransportUdp = "udp";

        /// <summary>Wire value of <see cref="TransportKind.WebSocket"/>.</summary>
        public const string TransportWebSocket = "websocket";

        /// <summary>The path that resolves <paramref name="code"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="code"/> is empty.</exception>
        public static string ResolvePath(JoinCode code)
        {
            return ResolvePathPrefix + RequireCode(code);
        }

        /// <summary>The path that deletes <paramref name="code"/>'s registration.</summary>
        /// <exception cref="ArgumentException"><paramref name="code"/> is empty.</exception>
        public static string RegistrationPath(JoinCode code)
        {
            return RegistrationPathPrefix + RequireCode(code);
        }

        /// <summary>The wire value of <paramref name="transport"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="transport"/> is not a defined value.</exception>
        public static string ToWire(TransportKind transport)
        {
            switch (transport)
            {
                case TransportKind.Udp:
                    return TransportUdp;
                case TransportKind.WebSocket:
                    return TransportWebSocket;
                default:
                    throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport.");
            }
        }

        /// <summary>
        /// Reads a wire transport value, ignoring case. Null or empty means <see cref="TransportKind.Udp"/>,
        /// the default.
        /// </summary>
        public static bool TryParseTransport(string value, out TransportKind transport)
        {
            if (string.IsNullOrEmpty(value) || string.Equals(value, TransportUdp, StringComparison.OrdinalIgnoreCase))
            {
                transport = TransportKind.Udp;
                return true;
            }

            if (string.Equals(value, TransportWebSocket, StringComparison.OrdinalIgnoreCase))
            {
                transport = TransportKind.WebSocket;
                return true;
            }

            transport = TransportKind.Udp;
            return false;
        }

        private static string RequireCode(JoinCode code)
        {
            if (code.IsEmpty)
            {
                throw new ArgumentException("A rendezvous path needs a join code.", nameof(code));
            }

            return code.Value;
        }

        /// <summary>JSON field names, camelCase.</summary>
        public static class Fields
        {
            /// <summary>The registration secret the operator configured (register request).</summary>
            public const string Secret = "secret";

            /// <summary>The server's public address (register request, resolve response).</summary>
            public const string Address = "address";

            /// <summary>The server's port (register request, resolve response).</summary>
            public const string Port = "port";

            /// <summary><see cref="TransportUdp"/> or <see cref="TransportWebSocket"/>.</summary>
            public const string Transport = "transport";

            /// <summary>The join code, canonical form.</summary>
            public const string Code = "code";

            /// <summary>The registration's private token, needed to heartbeat or delete it.</summary>
            public const string Token = "token";

            /// <summary>Seconds a registration lives without a heartbeat.</summary>
            public const string TtlSeconds = "ttlSeconds";

            /// <summary>When the registration expires unless renewed (ISO 8601, UTC).</summary>
            public const string ExpiresAt = "expiresAt";

            /// <summary>A human-readable reason on an error response.</summary>
            public const string Error = "error";
        }
    }
}
