using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>Where a server is in its life, as its health endpoint reports it.</summary>
    public enum HealthStatus
    {
        /// <summary>The process is up but not yet accepting players (503).</summary>
        Starting = 0,

        /// <summary>Accepting players (200).</summary>
        Running = 1,

        /// <summary>Shutting down (503).</summary>
        Stopping = 2,
    }

    /// <summary>A snapshot of a server's health, served as JSON by <see cref="HealthEndpoint"/>.</summary>
    public sealed class HealthReport
    {
        /// <summary>Where the server is in its life.</summary>
        public HealthStatus Status { get; set; }

        /// <summary>The server's name; may be empty.</summary>
        public string Name { get; set; }

        /// <summary>The game version.</summary>
        public string Version { get; set; }

        /// <summary>Players connected now.</summary>
        public int Players { get; set; }

        /// <summary>The player cap.</summary>
        public int MaxPlayers { get; set; }

        /// <summary>The game port.</summary>
        public int Port { get; set; }

        /// <summary>The transport.</summary>
        public TransportKind Transport { get; set; }

        /// <summary>True when players need a password.</summary>
        public bool PasswordProtected { get; set; }

        /// <summary>Seconds since the server process started.</summary>
        public double UptimeSeconds { get; set; }

        /// <summary>The HTTP status code: 200 while <see cref="HealthStatus.Running"/>, otherwise 503.</summary>
        public int HttpStatus
        {
            get { return Status == HealthStatus.Running ? 200 : 503; }
        }

        /// <summary>The report as one JSON object.</summary>
        public string ToJson()
        {
            var json = new StringBuilder("{");
            json.Append("\"status\":").Append(ServerPackageSpec.Quote(Status.ToString().ToLowerInvariant()));
            json.Append(",\"name\":").Append(ServerPackageSpec.Quote(Name));
            json.Append(",\"version\":").Append(ServerPackageSpec.Quote(Version));
            json.Append(",\"players\":").Append(Players.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"maxPlayers\":").Append(MaxPlayers.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"port\":").Append(Port.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"transport\":").Append(ServerPackageSpec.Quote(Transport == TransportKind.WebSocket ? "websocket" : "udp"));
            json.Append(",\"password\":").Append(PasswordProtected ? "true" : "false");
            json.Append(",\"uptimeSeconds\":").Append(Math.Floor(UptimeSeconds).ToString("0", CultureInfo.InvariantCulture));
            return json.Append('}').ToString();
        }
    }

    /// <summary>
    /// The server package's health endpoint: a tiny HTTP/1.1 server on its own TCP port that answers
    /// <c>GET /healthz</c> (and <c>GET /</c>) with a <see cref="HealthReport"/>: 200 while running, 503 while starting or
    /// stopping. Docker's HEALTHCHECK, a load balancer or a deploy tool probes it. Anything else gets 404 or 405.
    /// </summary>
    /// <remarks>
    /// Engine-free and thread-based: the report callback runs on a pool thread, so hand it a snapshot the game updates
    /// on its own thread (<see cref="Volatile"/> reads of an immutable report are enough). Each request gets one
    /// response and the connection closes.
    /// </remarks>
    public sealed class HealthEndpoint : IDisposable
    {
        private const int MaxRequestBytes = 8192;
        private const int ReadTimeoutMs = 3000;

        private readonly Func<HealthReport> _report;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        /// <summary>Creates an endpoint that serves what <paramref name="report"/> returns.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
        public HealthEndpoint(Func<HealthReport> report)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        /// <summary>The port it listens on (the one chosen by the system when started on 0); 0 when stopped.</summary>
        public int Port { get; private set; }

        /// <summary>True while listening.</summary>
        public bool IsRunning
        {
            get { return _running; }
        }

        /// <summary>Starts listening on <paramref name="port"/> (0 picks a free one) on every interface, or on <paramref name="address"/>.</summary>
        /// <exception cref="InvalidOperationException">It is already running.</exception>
        /// <exception cref="SocketException">The port is taken.</exception>
        public void Start(int port, IPAddress address = null)
        {
            if (_running)
            {
                throw new InvalidOperationException("The health endpoint is already running.");
            }

            var listener = new TcpListener(address ?? IPAddress.Any, port);
            listener.Start();
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MindAttic health endpoint" };
            _acceptThread.Start();
        }

        /// <summary>Stops listening. Safe to call more than once.</summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }

            _listener = null;
            Port = 0;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// The whole HTTP response to <paramref name="requestLine"/> (such as <c>GET /healthz HTTP/1.1</c>), headers and
        /// body. The endpoint's logic, without sockets.
        /// </summary>
        public static string Respond(string requestLine, HealthReport report)
        {
            string[] parts = (requestLine ?? string.Empty).Split(' ');
            if (parts.Length < 2)
            {
                return Response(400, "Bad Request", "{\"error\":\"bad request\"}", true);
            }

            string method = parts[0];
            string path = parts[1];
            int query = path.IndexOf('?');
            if (query >= 0)
            {
                path = path.Substring(0, query);
            }

            if (path != ServerSettings.HealthPath && path != "/")
            {
                return Response(404, "Not Found", "{\"error\":\"not found\"}", method != "HEAD");
            }

            if (method != "GET" && method != "HEAD")
            {
                return Response(405, "Method Not Allowed", "{\"error\":\"method not allowed\"}", true);
            }

            HealthReport current = report ?? new HealthReport { Status = HealthStatus.Starting };
            int status = current.HttpStatus;
            return Response(status, status == 200 ? "OK" : "Service Unavailable", current.ToJson(), method != "HEAD");
        }

        private static string Response(int status, string reason, string body, bool includeBody)
        {
            int length = Encoding.UTF8.GetByteCount(body);
            var text = new StringBuilder();
            text.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reason).Append("\r\n");
            text.Append("Content-Type: application/json; charset=utf-8\r\n");
            text.Append("Content-Length: ").Append(length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("Cache-Control: no-store\r\n");
            text.Append("Connection: close\r\n\r\n");
            if (includeBody)
            {
                text.Append(body);
            }

            return text.ToString();
        }

        private void AcceptLoop()
        {
            TcpListener listener = _listener;
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                ThreadPool.QueueUserWorkItem(Serve, client);
            }
        }

        private void Serve(object state)
        {
            using (var client = (TcpClient)state)
            {
                try
                {
                    client.ReceiveTimeout = ReadTimeoutMs;
                    client.SendTimeout = ReadTimeoutMs;
                    NetworkStream stream = client.GetStream();
                    string requestLine = ReadRequestHead(stream);
                    HealthReport report;
                    try
                    {
                        report = _report();
                    }
                    catch (Exception)
                    {
                        report = new HealthReport { Status = HealthStatus.Starting };
                    }

                    byte[] response = Encoding.UTF8.GetBytes(Respond(requestLine, report));
                    stream.Write(response, 0, response.Length);
                    stream.Flush();
                }
                catch (Exception)
                {
                    // A client that hangs up or times out gets nothing; the endpoint carries on.
                }
            }
        }

        private static string ReadRequestHead(NetworkStream stream)
        {
            var buffer = new byte[1024];
            var head = new StringBuilder();
            int total = 0;
            while (total < MaxRequestBytes)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                total += read;
                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (head.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) >= 0 || head.ToString().IndexOf("\n\n", StringComparison.Ordinal) >= 0)
                {
                    break;
                }
            }

            string text = head.ToString();
            int end = text.IndexOf('\n');
            return (end >= 0 ? text.Substring(0, end) : text).TrimEnd('\r');
        }
    }
}
