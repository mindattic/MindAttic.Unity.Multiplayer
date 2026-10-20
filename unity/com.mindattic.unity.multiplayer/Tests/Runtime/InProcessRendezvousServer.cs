using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MindAttic.Unity.Multiplayer.Core;
using F = MindAttic.Unity.Multiplayer.Core.RendezvousProtocol.Fields;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// The rendezvous service's HTTP API (<see cref="RendezvousProtocol"/>), served from this process on a loopback port so
    /// PlayMode tests exercise the real HTTP client, heartbeats and join-by-code without Docker or the .NET 10 server.
    /// Behaves like server/Rendezvous: required secret, caller's address when none is given, TTL expiry, heartbeat
    /// renewal, token-checked delete, 400/404 on resolve.
    /// </summary>
    internal sealed class InProcessRendezvousServer : IDisposable
    {
        public const string Secret = "playmode-secret";

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly object _gate = new object();
        private int _heartbeats;

        public InProcessRendezvousServer(int ttlSeconds = 60)
        {
            TtlSeconds = ttlSeconds;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(AcceptLoop);
        }

        public int Port { get; }

        public string Url
        {
            get { return "http://127.0.0.1:" + Port; }
        }

        public int TtlSeconds { get; }

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

        /// <summary>Drops every registration, as a restarted service would.</summary>
        public void ForgetAll()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }

        public void Dispose()
        {
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

        internal sealed class Entry
        {
            public JoinCode Code;
            public string Token;
            public string Address;
            public int Port;
            public TransportKind Transport;
            public DateTime ExpiresAt;
        }
    }
}
