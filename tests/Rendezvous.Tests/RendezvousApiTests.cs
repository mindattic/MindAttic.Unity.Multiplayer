using System.Net;
using System.Text.Json;
using MindAttic.Unity.Multiplayer.Core;

namespace MindAttic.Unity.Multiplayer.Rendezvous.Tests;

[TestFixture]
public class RendezvousApiTests
{
    private RendezvousFactory factory = null!;
    private HttpClient client = null!;

    [SetUp]
    public void SetUp()
    {
        factory = new RendezvousFactory();
        client = factory.ClientFrom("198.51.100.1");
    }

    [TearDown]
    public void TearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task Register_ThenResolve_ReturnsTheServer()
    {
        HttpResponseMessage response = await client.RegisterServer("203.0.113.7", 7777);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        JsonElement json = await response.Json();
        string code = json.GetProperty("code").GetString()!;
        Assert.That(json.GetProperty("token").GetString(), Has.Length.GreaterThanOrEqualTo(40));
        Assert.That(json.GetProperty("ttlSeconds").GetInt32(), Is.EqualTo(60));
        Assert.That(json.GetProperty("expiresAt").GetDateTimeOffset(), Is.EqualTo(factory.Time.GetUtcNow().AddSeconds(60)));
        Assert.That(response.Headers.Location!.ToString(), Is.EqualTo(RendezvousProtocol.ResolvePathPrefix + code));

        HttpResponseMessage resolved = await client.Resolve(code);
        Assert.That(resolved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(resolved.Headers.CacheControl!.NoStore, Is.True);
        JsonElement target = await resolved.Json();
        Assert.That(target.GetProperty("code").GetString(), Is.EqualTo(code));
        Assert.That(target.GetProperty("address").GetString(), Is.EqualTo("203.0.113.7"));
        Assert.That(target.GetProperty("port").GetInt32(), Is.EqualTo(7777));
        Assert.That(target.GetProperty("transport").GetString(), Is.EqualTo("udp"));
    }

    [Test]
    public async Task Register_MintsCoreFormatCodes()
    {
        (string code, _) = await client.RegisterOk();

        Assert.That(JoinCode.TryParse(code, out JoinCodeParseResult parsed), Is.True);
        Assert.That(parsed.Corrected, Is.False);
        Assert.That(parsed.Code.Value, Is.EqualTo(code), "the wire carries the canonical form");
        Assert.That(parsed.Code.Data, Has.Length.EqualTo(JoinCodeFormat.DefaultDataLength));
    }

    [Test]
    public async Task Register_CodeDataLengthIsConfigurable()
    {
        using RendezvousFactory longCodes = new RendezvousFactory().With("Rendezvous:CodeDataLength", "12");
        (string code, _) = await longCodes.ClientFrom("198.51.100.1").RegisterOk();

        Assert.That(JoinCode.Parse(code).Data, Has.Length.EqualTo(12));
    }

    [Test]
    public async Task Register_ManyServers_GetDistinctCodes()
    {
        var codes = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            (string code, _) = await factory.ClientFrom("198.51.100." + (10 + i)).RegisterOk(port: 7000 + i);
            codes.Add(code);
        }

        Assert.That(codes, Has.Count.EqualTo(10));
        Assert.That(factory.Store.Count, Is.EqualTo(10));
    }

    [Test]
    public async Task Resolve_AcceptsPlayerTypedForms()
    {
        (string code, _) = await client.RegisterOk();
        string typed = JoinCodeFormat.Format(code).ToLowerInvariant();

        HttpResponseMessage resolved = await client.Resolve(typed);

        Assert.That(resolved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await resolved.Json()).GetProperty("code").GetString(), Is.EqualTo(code));
    }

    [Test]
    public async Task Register_WebSocketTransport_RoundTrips()
    {
        (string code, _) = await client.RegisterOk("game.example.net", 443, "WebSocket");

        JsonElement target = await (await client.Resolve(code)).Json();

        Assert.That(target.GetProperty("address").GetString(), Is.EqualTo("game.example.net"));
        Assert.That(target.GetProperty("port").GetInt32(), Is.EqualTo(443));
        Assert.That(target.GetProperty("transport").GetString(), Is.EqualTo("websocket"));
    }

    [Test]
    public async Task Register_WithoutAddress_UsesTheCallersAddress()
    {
        HttpResponseMessage response = await factory.ClientFrom("203.0.113.99")
            .Register(new { secret = RendezvousFactory.Secret, port = 7777 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        string code = (await response.Json()).GetProperty("code").GetString()!;
        JsonElement target = await (await client.Resolve(code)).Json();
        Assert.That(target.GetProperty("address").GetString(), Is.EqualTo("203.0.113.99"));
    }

    [TestCase("wrong-secret")]
    [TestCase("")]
    [TestCase(null)]
    public async Task Register_WrongOrMissingSecret_Is401(string? secret)
    {
        HttpResponseMessage response = await client.Register(new { secret, address = "203.0.113.7", port = 7777 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.ErrorText(), Does.Contain("secret"));
        Assert.That(factory.Store.Count, Is.Zero);
    }

    [Test]
    public async Task Register_OpenRegistration_NeedsNoSecret()
    {
        using RendezvousFactory open = new RendezvousFactory()
            .With("Rendezvous:RegistrationSecret", "")
            .With("Rendezvous:AllowOpenRegistration", "true");

        HttpResponseMessage response = await open.ClientFrom("198.51.100.1").Register(new { address = "203.0.113.7", port = 7777 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public void Startup_WithoutSecret_Fails()
    {
        using RendezvousFactory unsecured = new RendezvousFactory().With("Rendezvous:RegistrationSecret", "");

        Exception? ex = Assert.Catch(() => unsecured.CreateClient());

        Assert.That(ex!.ToString(), Does.Contain("RegistrationSecret"));
    }

    [TestCase("0.0.0.0", 7777, null, "wildcard")]
    [TestCase("::", 7777, null, "wildcard")]
    [TestCase("bad host", 7777, null, "whitespace")]
    [TestCase("203.0.113.7", 0, null, "Port")]
    [TestCase("203.0.113.7", 70000, null, "Port")]
    [TestCase("203.0.113.7", 7777, "carrier-pigeon", "Transport")]
    public async Task Register_BadEndpoint_Is400(string address, int port, string? transport, string reason)
    {
        HttpResponseMessage response = await client.RegisterServer(address, port, transport);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        string error = await response.ErrorText();
        Assert.That(error, Does.Contain(reason));
        Assert.That(error, Does.Not.Contain("(Parameter"));
    }

    [Test]
    public async Task Register_StoreFull_Is503()
    {
        using RendezvousFactory small = new RendezvousFactory().With("Rendezvous:MaxRegistrations", "2");
        HttpClient smallClient = small.ClientFrom("198.51.100.1");
        await smallClient.RegisterOk(port: 1);
        await smallClient.RegisterOk(port: 2);

        HttpResponseMessage third = await smallClient.RegisterServer(port: 3);
        Assert.That(third.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

        small.Time.Advance(TimeSpan.FromSeconds(61));
        HttpResponseMessage afterExpiry = await smallClient.RegisterServer(port: 3);
        Assert.That(afterExpiry.StatusCode, Is.EqualTo(HttpStatusCode.Created), "expired entries free their slots");
    }

    [TestCase("hello")]
    [TestCase("ABC-DEF-GH")]
    public async Task Resolve_NotAJoinCode_Is400WithReason(string input)
    {
        HttpResponseMessage response = await client.Resolve(input);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.ErrorText(), Is.Not.Empty);
    }

    [Test]
    public async Task Resolve_UnknownCode_Is404()
    {
        HttpResponseMessage response = await client.Resolve(JoinCode.Generate().Value);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.ErrorText(), Does.Contain("expired"));
    }

    [Test]
    public async Task Registration_ExpiresAfterTtl()
    {
        (string code, _) = await client.RegisterOk();

        factory.Time.Advance(TimeSpan.FromSeconds(59));
        Assert.That((await client.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        factory.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.That((await client.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Heartbeat_ExtendsTheTtl()
    {
        (string code, string token) = await client.RegisterOk();

        factory.Time.Advance(TimeSpan.FromSeconds(45));
        HttpResponseMessage beat = await client.Heartbeat(code, token);
        Assert.That(beat.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        JsonElement json = await beat.Json();
        Assert.That(json.GetProperty("code").GetString(), Is.EqualTo(code));
        Assert.That(json.GetProperty("expiresAt").GetDateTimeOffset(), Is.EqualTo(factory.Time.GetUtcNow().AddSeconds(60)));

        factory.Time.Advance(TimeSpan.FromSeconds(45));
        Assert.That((await client.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.OK), "90 s in, alive thanks to the heartbeat");

        factory.Time.Advance(TimeSpan.FromSeconds(15));
        Assert.That((await client.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Heartbeat_WrongToken_Is404AndDoesNotRenew()
    {
        (string code, _) = await client.RegisterOk();
        (_, string otherToken) = await client.RegisterOk(port: 8888);

        factory.Time.Advance(TimeSpan.FromSeconds(45));
        Assert.That((await client.Heartbeat(code, otherToken)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        factory.Time.Advance(TimeSpan.FromSeconds(15));
        Assert.That((await client.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Heartbeat_AfterExpiry_Is404()
    {
        (string code, string token) = await client.RegisterOk();
        factory.Time.Advance(TimeSpan.FromSeconds(61));

        HttpResponseMessage beat = await client.Heartbeat(code, token);

        Assert.That(beat.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await beat.ErrorText(), Does.Contain("register again"));
    }

    [Test]
    public async Task Heartbeat_MissingFields_Is400()
    {
        Assert.That((await client.Heartbeat("", "x")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.Heartbeat(JoinCode.Generate().Value, null)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Delete_WithToken_RemovesTheCode()
    {
        (string code, string token) = await client.RegisterOk();

        Assert.That((await client.Unregister(code, token)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await client.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.Unregister(code, token)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "idempotent: gone stays gone");
    }

    [TestCase(null)]
    [TestCase("not-the-token")]
    public async Task Delete_WithoutTheToken_Is404AndKeepsTheCode(string? token)
    {
        (string code, _) = await client.RegisterOk();

        Assert.That((await client.Unregister(code, token)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Health_IsHealthy()
    {
        HttpResponseMessage response = await client.GetAsync(RendezvousProtocol.HealthPath);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
    }

    [Test]
    public async Task Health_StoreFull_IsDegradedButStillAnswers200()
    {
        using RendezvousFactory small = new RendezvousFactory().With("Rendezvous:MaxRegistrations", "1");
        HttpClient smallClient = small.ClientFrom("198.51.100.1");
        await smallClient.RegisterOk();

        HttpResponseMessage response = await smallClient.GetAsync(RendezvousProtocol.HealthPath);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Degraded"));
    }

    [Test]
    public async Task MalformedJson_Is400()
    {
        var content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(RendezvousProtocol.RegisterPath, content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
