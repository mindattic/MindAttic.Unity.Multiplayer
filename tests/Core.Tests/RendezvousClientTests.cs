using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// A rendezvous service in memory, behind <see cref="IRendezvousHttp"/>: the protocol's routes, status codes and
    /// bodies, so the client and the join-code service are tested without sockets.
    /// </summary>
    internal sealed class FakeRendezvousHttp : IRendezvousHttp
    {
        public const string Secret = "s3cret";

        private readonly Dictionary<string, (string Token, string Address, int Port, string Transport)> registrations =
            new Dictionary<string, (string, string, int, string)>();

        private readonly object gate = new object();

        public int TtlSeconds { get; set; } = 60;

        public string CallerAddress { get; set; } = "198.51.100.9";

        public int Heartbeats;

        public int Requests;

        /// <summary>When set, every request answers this status with an error body.</summary>
        public int? FailWith { get; set; }

        /// <summary>When true, every request throws as if the service were unreachable.</summary>
        public bool Unreachable { get; set; }

        public List<string> Log { get; } = new List<string>();

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return registrations.Count;
                }
            }
        }

        public void Forget(string code)
        {
            lock (gate)
            {
                registrations.Remove(code);
            }
        }

        public Task<RendezvousHttpResponse> SendAsync(
            string method, string path, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            lock (gate)
            {
                Log.Add(method + " " + path);
            }

            if (Unreachable)
            {
                throw new System.Net.Http.HttpRequestException("connection refused");
            }

            if (FailWith.HasValue)
            {
                return Reply(FailWith.Value, "{\"error\":\"forced failure\"}");
            }

            RendezvousJson.TryParseObject(jsonBody, out Dictionary<string, object> body);
            lock (gate)
            {
                if (method == "POST" && path == RendezvousProtocol.RegisterPath)
                {
                    if (RendezvousJson.GetString(body, "secret") != Secret)
                    {
                        return Reply(401, "{\"error\":\"Wrong or missing registration secret.\"}");
                    }

                    string code = JoinCode.Generate().Value;
                    string token = Guid.NewGuid().ToString("N");
                    string address = RendezvousJson.GetString(body, "address") ?? CallerAddress;
                    registrations[code] = (token, address, (int)RendezvousJson.GetInteger(body, "port").Value, RendezvousJson.GetString(body, "transport") ?? "udp");
                    return Reply(201, RendezvousJson.Write(new Dictionary<string, object>
                    {
                        { "code", code }, { "token", token }, { "ttlSeconds", TtlSeconds }, { "expiresAt", "2026-10-20T12:01:00+00:00" },
                    }));
                }

                if (method == "POST" && path == RendezvousProtocol.HeartbeatPath)
                {
                    string code = RendezvousJson.GetString(body, "code");
                    if (code == null || !registrations.TryGetValue(code, out var r) || r.Token != RendezvousJson.GetString(body, "token"))
                    {
                        return Reply(404, "{\"error\":\"No live registration with that code and token; register again.\"}");
                    }

                    Interlocked.Increment(ref Heartbeats);
                    return Reply(200, "{\"code\":\"" + code + "\",\"ttlSeconds\":" + TtlSeconds + "}");
                }

                if (method == "GET" && path.StartsWith(RendezvousProtocol.ResolvePathPrefix, StringComparison.Ordinal))
                {
                    string code = path.Substring(RendezvousProtocol.ResolvePathPrefix.Length);
                    if (!JoinCode.TryParse(code, out JoinCode parsed))
                    {
                        return Reply(400, "{\"error\":\"not a code\"}");
                    }

                    if (!registrations.TryGetValue(parsed.Value, out var r))
                    {
                        return Reply(404, "{\"error\":\"No server is using that code.\"}");
                    }

                    return Reply(200, RendezvousJson.Write(new Dictionary<string, object>
                    {
                        { "code", parsed.Value }, { "address", r.Address }, { "port", r.Port }, { "transport", r.Transport },
                    }));
                }

                if (method == "DELETE" && path.StartsWith(RendezvousProtocol.RegistrationPathPrefix, StringComparison.Ordinal))
                {
                    string code = path.Substring(RendezvousProtocol.RegistrationPathPrefix.Length);
                    headers.TryGetValue(RendezvousProtocol.TokenHeader, out string token);
                    if (!registrations.TryGetValue(code, out var r) || r.Token != token)
                    {
                        return Reply(404, "{\"error\":\"No live registration with that code and token.\"}");
                    }

                    registrations.Remove(code);
                    return Reply(204, string.Empty);
                }
            }

            return Reply(404, "{\"error\":\"no route\"}");
        }

        private static Task<RendezvousHttpResponse> Reply(int status, string body)
        {
            return Task.FromResult(new RendezvousHttpResponse(status, body));
        }
    }

    [TestFixture]
    public class RendezvousClientTests
    {
        private FakeRendezvousHttp http;
        private RendezvousClient client;

        [SetUp]
        public void SetUp()
        {
            http = new FakeRendezvousHttp();
            client = new RendezvousClient(http);
        }

        [Test]
        public async Task Register_Resolve_Heartbeat_Unregister_RoundTrip()
        {
            RendezvousRegistration registration = await client.RegisterAsync(FakeRendezvousHttp.Secret, "203.0.113.7", 7777, TransportKind.WebSocket);

            Assert.That(registration.Code.IsEmpty, Is.False);
            Assert.That(registration.Token, Is.Not.Empty);
            Assert.That(registration.TtlSeconds, Is.EqualTo(60));

            JoinTarget target = await client.ResolveAsync(registration.Code);
            Assert.That(target, Is.EqualTo(JoinTarget.ForEndpoint("203.0.113.7", 7777, TransportKind.WebSocket)));

            Assert.That(await client.HeartbeatAsync(registration.Code, registration.Token), Is.True);
            Assert.That(await client.HeartbeatAsync(registration.Code, "wrong"), Is.False, "404 means register again");

            Assert.That(await client.UnregisterAsync(registration.Code, "wrong"), Is.False);
            Assert.That(await client.UnregisterAsync(registration.Code, registration.Token), Is.True);
            Assert.That(await client.ResolveAsync(registration.Code), Is.Null);
        }

        [Test]
        public async Task Register_WithoutAddress_LetsTheServiceUseTheCallersAddress()
        {
            RendezvousRegistration registration = await client.RegisterAsync(FakeRendezvousHttp.Secret, null, 7777, TransportKind.Udp);

            JoinTarget target = await client.ResolveAsync(registration.Code);
            Assert.That(target.Address, Is.EqualTo(http.CallerAddress));
        }

        [Test]
        public void Register_WrongSecret_ThrowsWithTheServicesReason()
        {
            var ex = Assert.ThrowsAsync<RendezvousException>(() => client.RegisterAsync("nope", null, 7777, TransportKind.Udp));
            Assert.That(ex.StatusCode, Is.EqualTo(401));
            Assert.That(ex.IsTransient, Is.False);
            Assert.That(ex.Message, Does.Contain("Wrong or missing registration secret."));
        }

        [Test]
        public async Task Resolve_UnknownCode_IsNull()
        {
            Assert.That(await client.ResolveAsync(JoinCode.Generate()), Is.Null);
        }

        [Test]
        public void Resolve_RateLimited_ThrowsTransient()
        {
            http.FailWith = 429;
            var ex = Assert.ThrowsAsync<RendezvousException>(() => client.ResolveAsync(JoinCode.Generate()));
            Assert.That(ex.StatusCode, Is.EqualTo(429));
            Assert.That(ex.IsTransient, Is.True);
        }

        [Test]
        public void Unreachable_ThrowsStatusZero()
        {
            http.Unreachable = true;
            var ex = Assert.ThrowsAsync<RendezvousException>(() => client.ResolveAsync(JoinCode.Generate()));
            Assert.That(ex.StatusCode, Is.EqualTo(0));
            Assert.That(ex.IsTransient, Is.True);
            Assert.That(ex.Message, Does.Contain("Could not reach"));
        }

        [TestCase("https://rv.example.com", true)]
        [TestCase("http://127.0.0.1:5080/base", true)]
        [TestCase("ftp://rv.example.com", false)]
        [TestCase("rv.example.com", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void BaseUrl_MustBeAbsoluteHttp(string url, bool valid)
        {
            Assert.That(RendezvousClient.IsValidBaseUrl(url), Is.EqualTo(valid));
            if (!valid)
            {
                Assert.Throws<ArgumentException>(() => RendezvousClient.ParseBaseUrl(url));
            }
        }

        [Test]
        public void BaseUrl_GetsATrailingSlashSoItsPathIsKept()
        {
            Assert.That(HttpClientRendezvousHttp.WithTrailingSlash(new Uri("https://x.example/rv")).AbsoluteUri, Is.EqualTo("https://x.example/rv/"));
            Assert.That(HttpClientRendezvousHttp.WithTrailingSlash(new Uri("https://x.example/")).AbsoluteUri, Is.EqualTo("https://x.example/"));
        }
    }

    [TestFixture]
    public class RendezvousJoinCodeServiceTests
    {
        private FakeRendezvousHttp http;
        private RendezvousJoinCodeService service;

        [SetUp]
        public void SetUp()
        {
            http = new FakeRendezvousHttp { TtlSeconds = 1 };
            service = new RendezvousJoinCodeService(new RendezvousClient(http), FakeRendezvousHttp.Secret, ownsClient: true);
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
        }

        [Test]
        public async Task Publish_Resolves_ThenRevokeDeletesIt()
        {
            JoinTarget target = JoinTarget.ForEndpoint("203.0.113.7", 7777);
            JoinCode code = await service.PublishAsync(target);

            Assert.That(await service.ResolveAsync(code), Is.EqualTo(target));
            Assert.That(service.LiveCount, Is.EqualTo(1));

            Assert.That(await service.RevokeAsync(code), Is.True);
            Assert.That(await service.RevokeAsync(code), Is.False, "revoke is idempotent");
            Assert.That(await service.ResolveAsync(code), Is.Null);
            Assert.That(service.LiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Publish_RelayTarget_IsNotSupported()
        {
            Assert.ThrowsAsync<NotSupportedException>(() => service.PublishAsync(JoinTarget.ForRelay("BCDF67")));
        }

        [Test]
        public async Task Revoke_ACodeItDidNotPublish_IsFalseAndDeletesNothing()
        {
            using (var other = new RendezvousJoinCodeService(new RendezvousClient(http), FakeRendezvousHttp.Secret))
            {
                JoinCode code = await other.PublishAsync(JoinTarget.ForEndpoint("203.0.113.7", 7777));
                Assert.That(await service.RevokeAsync(code), Is.False);
                Assert.That(await service.ResolveAsync(code), Is.Not.Null);
            }
        }

        [Test]
        public async Task Heartbeats_KeepTheRegistrationAlive()
        {
            int sent = 0;
            service.HeartbeatSent += _ => Interlocked.Increment(ref sent);
            await service.RegisterAsync(ServerAdvertisement.Explicit(null, 7777, TransportKind.Udp));

            Assert.That(RendezvousJoinCodeService.HeartbeatInterval(1), Is.EqualTo(TimeSpan.FromSeconds(1 / 3.0)));
            await WaitFor(() => Volatile.Read(ref sent) >= 2, TimeSpan.FromSeconds(5));
            Assert.That(http.Heartbeats, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task TransientHeartbeatFailures_AreRetried()
        {
            JoinCode code = await service.RegisterAsync(ServerAdvertisement.Explicit(null, 7777, TransportKind.Udp));
            string lost = null;
            service.RegistrationLost += (_, why) => lost = why;

            http.FailWith = 503;
            await WaitFor(() => http.Requests >= 4, TimeSpan.FromSeconds(5));
            http.FailWith = null;
            int before = http.Heartbeats;
            await WaitFor(() => http.Heartbeats > before, TimeSpan.FromSeconds(5));

            Assert.That(lost, Is.Null, "a 503 is retried, not given up on");
            Assert.That(service.LiveCount, Is.EqualTo(1));
            Assert.That(await service.ResolveAsync(code), Is.Not.Null);
        }

        [Test]
        public async Task ARegistrationTheServiceForgot_RaisesRegistrationLost()
        {
            JoinCode code = await service.RegisterAsync(ServerAdvertisement.Explicit(null, 7777, TransportKind.Udp));
            var lost = new TaskCompletionSource<JoinCode>();
            service.RegistrationLost += (c, _) => lost.TrySetResult(c);

            http.Forget(code.Value);

            Task done = await Task.WhenAny(lost.Task, Task.Delay(5000));
            Assert.That(done, Is.SameAs(lost.Task), "RegistrationLost was not raised");
            Assert.That(lost.Task.Result, Is.EqualTo(code));
            Assert.That(service.LiveCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Dispose_StopsHeartbeatsAndDeletesRegistrations()
        {
            await service.RegisterAsync(ServerAdvertisement.Explicit(null, 7777, TransportKind.Udp));
            await service.RegisterAsync(ServerAdvertisement.Explicit(null, 7778, TransportKind.Udp));
            Assert.That(http.Count, Is.EqualTo(2));

            service.Dispose();

            Assert.That(http.Count, Is.EqualTo(0));
            int beats = http.Heartbeats;
            await Task.Delay(800);
            Assert.That(http.Heartbeats, Is.EqualTo(beats), "no heartbeats after Dispose");
            Assert.ThrowsAsync<ObjectDisposedException>(() => service.RegisterAsync(ServerAdvertisement.Explicit(null, 7777, TransportKind.Udp)));
        }

        private static async Task WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            DateTime until = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > until)
                {
                    Assert.Fail("Timed out waiting.");
                }

                await Task.Delay(20);
            }
        }
    }
}
