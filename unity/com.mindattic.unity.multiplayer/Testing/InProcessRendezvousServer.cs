using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using F = Unity.Multiplayer.Core.RendezvousProtocol.Fields;

namespace Unity.Multiplayer.Testing
{
    /// <summary>
    /// The rendezvous service's HTTP API (<see cref="RendezvousProtocol"/>), served from this process on a loopback port so
    /// PlayMode tests exercise the real HTTP client, heartbeats and join-by-code without Docker or the .NET 10 server.
    /// Behaves like server/Rendezvous: required secret, caller's address when none is given, TTL expiry, heartbeat
    /// renewal, token-checked delete, 400/404 on resolve.
    /// </summary>
    /// <remarks>
    /// Point a dedicated server at it with <see cref="ForServer"/> (it registers itself once running) and a client with
    /// <see cref="ForClient"/> (it resolves codes here); <see cref="Find"/>, <see cref="Count"/> and <see cref="Heartbeats"/>
    /// show what the service saw. Dispose it when the test ends (<see cref="NetSessionTest.StartRendezvous"/> does).
    /// </remarks>
    public sealed class InProcessRendezvousServer : IDisposable
    {
        /// <summary>The registration secret a server needs when none is given to the constructor.</summary>
        public const string DefaultSecret = "playmode-secret";

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly object _gate = new object();
        private int _heartbeats;

        /// <summary>Starts serving on a free loopback port.</summary>
        /// <param name="ttlSeconds">How long a registration lives without a heartbeat (a server heartbeats at a third of it).</param>
        /// <param name="secret">The registration secret servers must send; <see cref="DefaultSecret"/> when null.</param>
        public InProcessRendezvousServer(int ttlSeconds = 60, string secret = null)
        {
            if (ttlSeconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(ttlSeconds), "A registration has to live at least a second.");
            }

            TtlSeconds = ttlSeconds;
            Secret = secret ?? DefaultSecret;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(AcceptLoop);
        }

        /// <summary>The loopback TCP port it serves on.</summary>
        public int Port { get; }

        /// <summary>The registration secret servers must send (<see cref="NetConfig.RendezvousSecret"/>).</summary>
        public string Secret { get; }

        /// <summary>Its base URL (<see cref="NetConfig.RendezvousUrl"/>).</summary>
        public string Url
        {
            get { return "http://127.0.0.1:" + Port; }
        }

        /// <summary>How long a registration lives without a heartbeat.</summary>
        public int TtlSeconds { get; }

        /// <summary>How many heartbeats it has accepted.</summary>
        public int Heartbeats
        {
            get { return Volatile.Read(ref _heartbeats); }
        }

        /// <summary>How many live (unexpired) registrations there are.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    Expire();
                    return _entries.Count;
                }
            }
        }

        /// <summary>The live registration for <paramref name="code"/>, or null.</summary>
        public Entry Find(JoinCode code)
        {
            lock (_gate)
            {
                Expire();
                Entry entry;
                return _entries.TryGetValue(code.Value, out entry) ? entry : null;
            }
        }

        /// <summary>
        /// <paramref name="config"/> (a new one when null) pointed at this service for a server: its URL and secret. A
        /// dedicated server registers itself as soon as it is running; a host publishes here from
        /// <see cref="NetSession.HostWithCode"/> once <see cref="NetConfig.JoinCodes"/> is also
        /// <see cref="JoinCodeProvider.Rendezvous"/> (its default is Relay).
        /// </summary>
        public NetConfig ForServer(NetConfig config = null)
        {
            config = config ?? new NetConfig();
            config.RendezvousUrl = Url;
            config.RendezvousSecret = Secret;
            return config;
        }

        /// <summary>
        /// <paramref name="config"/> (a new one when null) pointed at this service for a client that joins by a code
        /// registered here: its URL. The endpoint and transport come from the registration.
        /// </summary>
        public NetConfig ForClient(NetConfig config = null)
        {
            config = config ?? new NetConfig();
            config.RendezvousUrl = Url;
            return config;
        }

        /// <summary>Drops every registration, as a restarted service would.</summary>
        public void ForgetAll()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }

        /// <summary>Stops serving. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            _stop.Cancel();
            _listener.Stop();
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return; // stopped
                }

                _ = Task.Run(() => Serve(client));
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    string remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    string head = await ReadHead(stream).ConfigureAwait(false);
                    string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] requestLine = lines[0].Split(' ');
                    string method = requestLine[0];
                    string path = requestLine[1];
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon > 0)
                        {
                            headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                        }
                    }

                    string body = string.Empty;
                    string lengthText;
                    int length;
                    if (headers.TryGetValue("Content-Length", out lengthText) && int.TryParse(lengthText, out length) && length > 0)
                    {
                        var buffer = new byte[length];
                        int read = 0;
                        while (read < length)
                        {
                            int n = await stream.ReadAsync(buffer, read, length - read).ConfigureAwait(false);
                            if (n == 0)
                            {
                                break;
                            }

                            read += n;
                        }

                        body = Encoding.UTF8.GetString(buffer, 0, read);
                    }

                    int status;
                    string reply = Handle(method, path, headers, body, remote, out status);
                    byte[] payload = Encoding.UTF8.GetBytes(reply);
                    string response = "HTTP/1.1 " + status + " " + Reason(status) + "\r\n"
                        + "Content-Type: application/json; charset=utf-8\r\n"
                        + "Content-Length: " + payload.Length + "\r\n"
                        + "Connection: close\r\n\r\n";
                    byte[] headBytes = Encoding.ASCII.GetBytes(response);
                    await stream.WriteAsync(headBytes, 0, headBytes.Length).ConfigureAwait(false);
                    await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A client that hung up mid-request; nothing to answer.
                }
            }
        }

        private static async Task<string> ReadHead(Stream stream)
        {
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int n = await stream.ReadAsync(one, 0, 1).ConfigureAwait(false);
                if (n == 0)
                {
                    throw new IOException("Connection closed before the request head ended.");
                }

                head.Append((char)one[0]);
                if (head.Length > 16384)
                {
                    throw new IOException("Request head too large.");
                }
            }

            return head.ToString(0, head.Length - 4);
        }

        private string Handle(string method, string path, Dictionary<string, string> headers, string body, string remote, out int status)
        {
            Dictionary<string, object> json;
            RendezvousJson.TryParseObject(body, out json);
            lock (_gate)
            {
                Expire();
                if (method == "POST" && path == RendezvousProtocol.RegisterPath)
                {
                    if (RendezvousJson.GetString(json, F.Secret) != Secret)
                    {
                        return Error(401, "Wrong or missing registration secret.", out status);
                    }

                    long? port = RendezvousJson.GetInteger(json, F.Port);
                    TransportKind transport;
                    if (port == null || port < 1 || port > 65535
                        || !RendezvousProtocol.TryParseTransport(RendezvousJson.GetString(json, F.Transport), out transport))
                    {
                        return Error(400, "Bad endpoint.", out status);
                    }

                    JoinCode code;
                    do
                    {
                        code = JoinCode.Generate();
                    }
                    while (_entries.ContainsKey(code.Value));

                    var entry = new Entry
                    {
                        Code = code,
                        Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                        Address = RendezvousJson.GetString(json, F.Address) ?? remote,
                        Port = (int)port.Value,
                        Transport = transport,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(TtlSeconds),
                    };
                    _entries[code.Value] = entry;
                    status = 201;
                    return RendezvousJson.Write(new Dictionary<string, object>
                    {
                        { F.Code, code.Value }, { F.Token, entry.Token }, { F.TtlSeconds, TtlSeconds }, { F.ExpiresAt, entry.ExpiresAt.ToString("o") },
                    });
                }

                if (method == "POST" && path == RendezvousProtocol.HeartbeatPath)
                {
                    Entry entry;
                    string code = RendezvousJson.GetString(json, F.Code);
                    if (code == null || !_entries.TryGetValue(code, out entry) || entry.Token != RendezvousJson.GetString(json, F.Token))
                    {
                        return Error(404, "No live registration with that code and token; register again.", out status);
                    }

                    entry.ExpiresAt = DateTime.UtcNow.AddSeconds(TtlSeconds);
                    Interlocked.Increment(ref _heartbeats);
                    status = 200;
                    return RendezvousJson.Write(new Dictionary<string, object>
                    {
                        { F.Code, code }, { F.TtlSeconds, TtlSeconds }, { F.ExpiresAt, entry.ExpiresAt.ToString("o") },
                    });
                }

                if (method == "GET" && path.StartsWith(RendezvousProtocol.ResolvePathPrefix, StringComparison.Ordinal))
                {
                    JoinCode code;
                    if (!JoinCode.TryParse(Uri.UnescapeDataString(path.Substring(RendezvousProtocol.ResolvePathPrefix.Length)), out code))
                    {
                        return Error(400, "That is not a join code.", out status);
                    }

                    Entry entry;
                    if (!_entries.TryGetValue(code.Value, out entry))
                    {
                        return Error(404, "No server is using that code. It may have closed or expired.", out status);
                    }

                    status = 200;
                    return RendezvousJson.Write(new Dictionary<string, object>
                    {
                        { F.Code, code.Value }, { F.Address, entry.Address }, { F.Port, entry.Port }, { F.Transport, RendezvousProtocol.ToWire(entry.Transport) },
                    });
                }

                if (method == "DELETE" && path.StartsWith(RendezvousProtocol.RegistrationPathPrefix, StringComparison.Ordinal))
                {
                    string code = path.Substring(RendezvousProtocol.RegistrationPathPrefix.Length);
                    string token;
                    headers.TryGetValue(RendezvousProtocol.TokenHeader, out token);
                    Entry entry;
                    if (!_entries.TryGetValue(code, out entry) || entry.Token != token)
                    {
                        return Error(404, "No live registration with that code and token.", out status);
                    }

                    _entries.Remove(code);
                    status = 204;
                    return string.Empty;
                }
            }

            return Error(404, "No such route.", out status);
        }

        private void Expire()
        {
            DateTime now = DateTime.UtcNow;
            var dead = new List<string>();
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (pair.Value.ExpiresAt <= now)
                {
                    dead.Add(pair.Key);
                }
            }

            foreach (string code in dead)
            {
                _entries.Remove(code);
            }
        }

        private static string Error(int code, string message, out int status)
        {
            status = code;
            return RendezvousJson.Write(new Dictionary<string, object> { { F.Error, message } });
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 201: return "Created";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                default: return "Not Found";
            }
        }

        /// <summary>One live registration, as the service holds it.</summary>
        public sealed class Entry
        {
            /// <summary>The code it was given.</summary>
            public JoinCode Code { get; internal set; }

            /// <summary>The token heartbeats and the delete must carry.</summary>
            public string Token { get; internal set; }

            /// <summary>The address clients are sent to: the one registered, else the caller's.</summary>
            public string Address { get; internal set; }

            /// <summary>The port clients are sent to.</summary>
            public int Port { get; internal set; }

            /// <summary>The transport clients use.</summary>
            public TransportKind Transport { get; internal set; }

            /// <summary>When it expires without another heartbeat (UTC).</summary>
            public DateTime ExpiresAt { get; internal set; }
        }
    }
}
