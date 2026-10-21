using System.Net;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer.Rendezvous.Tests;

[TestFixture]
public class RateLimitTests
{
    [Test]
    public async Task Resolve_OverTheLimit_Is429ForThatIpOnly()
    {
        using RendezvousFactory factory = new RendezvousFactory().With("Rendezvous:RateLimits:ResolvePerWindow", "3");
        HttpClient guesser = factory.ClientFrom("198.51.100.66");
        HttpClient player = factory.ClientFrom("198.51.100.1");
        string unknown = JoinCode.Generate().Value;

        for (int i = 0; i < 3; i++)
        {
            Assert.That((await guesser.Resolve(unknown)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        HttpResponseMessage limited = await guesser.Resolve(unknown);
        Assert.That(limited.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(limited.Headers.RetryAfter, Is.Not.Null);
        Assert.That(await limited.ErrorText(), Does.Contain("Too many requests"));

        Assert.That((await player.Resolve(unknown)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "another IP has its own budget");
    }

    [Test]
    public async Task Register_OverTheLimit_Is429()
    {
        using RendezvousFactory factory = new RendezvousFactory().With("Rendezvous:RateLimits:RegisterPerWindow", "2");
        HttpClient server = factory.ClientFrom("198.51.100.7");

        await server.RegisterOk(port: 1);
        await server.RegisterOk(port: 2);

        Assert.That((await server.RegisterServer(port: 3)).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(factory.Store.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Limits_AreSeparatePerRouteGroup()
    {
        using RendezvousFactory factory = new RendezvousFactory().With("Rendezvous:RateLimits:RegisterPerWindow", "1");
        HttpClient server = factory.ClientFrom("198.51.100.7");

        (string code, string token) = await server.RegisterOk();
        Assert.That((await server.RegisterServer(port: 2)).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));

        Assert.That((await server.Heartbeat(code, token)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await server.Resolve(code)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Ipv6_IsLimitedPerSlash64()
    {
        using RendezvousFactory factory = new RendezvousFactory().With("Rendezvous:RateLimits:ResolvePerWindow", "2");
        string unknown = JoinCode.Generate().Value;

        await factory.ClientFrom("2001:db8:1:2::1").Resolve(unknown);
        await factory.ClientFrom("2001:db8:1:2::2").Resolve(unknown);

        Assert.That((await factory.ClientFrom("2001:db8:1:2:ffff::3").Resolve(unknown)).StatusCode,
            Is.EqualTo(HttpStatusCode.TooManyRequests), "same /64");
        Assert.That((await factory.ClientFrom("2001:db8:1:3::1").Resolve(unknown)).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound), "different /64");
    }

    [Test]
    public async Task Health_IsNeverRateLimited()
    {
        using RendezvousFactory factory = new RendezvousFactory()
            .With("Rendezvous:RateLimits:ResolvePerWindow", "1")
            .With("Rendezvous:RateLimits:RegisterPerWindow", "1")
            .With("Rendezvous:RateLimits:HeartbeatPerWindow", "1");
        HttpClient probe = factory.ClientFrom("198.51.100.1");

        for (int i = 0; i < 20; i++)
        {
            Assert.That((await probe.GetAsync(RendezvousProtocol.HealthPath)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
