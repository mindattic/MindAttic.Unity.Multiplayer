using MindAttic.Unity.Multiplayer.Core;

namespace MindAttic.Unity.Multiplayer.Rendezvous.Tests;

/// <summary>
/// UM-T13: the game's own client (Core's <see cref="RendezvousClient"/> and <see cref="RendezvousJoinCodeService"/>, the
/// code a dedicated server and players run) against the real service, so the hand-rolled JSON on the Unity side and
/// System.Text.Json on the server side agree.
/// </summary>
[TestFixture]
public class CoreClientInteropTests
{
    private RendezvousFactory factory = null!;
    private RendezvousClient client = null!;

    [SetUp]
    public void SetUp()
    {
        factory = new RendezvousFactory();
        client = new RendezvousClient(new HttpClientRendezvousHttp(factory.ClientFrom("198.51.100.1"), ownsClient: true), ownsHttp: true);
    }

    [TearDown]
    public void TearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task Register_Resolve_Heartbeat_Unregister()
    {
        RendezvousRegistration registration = await client.RegisterAsync(RendezvousFactory.Secret, "203.0.113.7", 7777, TransportKind.WebSocket);

        Assert.That(registration.TtlSeconds, Is.EqualTo(60));
        Assert.That(registration.Token, Has.Length.GreaterThanOrEqualTo(40));
        Assert.That(await client.ResolveAsync(registration.Code), Is.EqualTo(JoinTarget.ForEndpoint("203.0.113.7", 7777, TransportKind.WebSocket)));
        Assert.That(await client.HeartbeatAsync(registration.Code, registration.Token), Is.True);
        Assert.That(await client.HeartbeatAsync(registration.Code, "not-the-token"), Is.False);
        Assert.That(await client.UnregisterAsync(registration.Code, registration.Token), Is.True);
        Assert.That(await client.ResolveAsync(registration.Code), Is.Null);
        Assert.That(await client.UnregisterAsync(registration.Code, registration.Token), Is.False);
    }

    [Test]
    public async Task Register_WithNoAddress_RecordsTheCallersAddress()
    {
        RendezvousRegistration registration = await client.RegisterAsync(RendezvousFactory.Secret, null, 7777, TransportKind.Udp);

        JoinTarget? target = await client.ResolveAsync(registration.Code);
        Assert.That(target!.Address, Is.EqualTo("198.51.100.1"));
        Assert.That(target.Transport, Is.EqualTo(TransportKind.Udp));
    }

    [Test]
    public void Register_WrongSecret_SurfacesTheServicesMessage()
    {
        var ex = Assert.ThrowsAsync<RendezvousException>(() => client.RegisterAsync("wrong", null, 7777, TransportKind.Udp));
        Assert.That(ex!.StatusCode, Is.EqualTo(401));
        Assert.That(ex.Message, Does.Contain("Wrong or missing registration secret."));
    }

    [Test]
    public async Task Resolve_UnknownCode_IsNull()
    {
        Assert.That(await client.ResolveAsync(JoinCode.Generate()), Is.Null);
    }

    [Test]
    public async Task JoinCodeService_PublishesAndRevokes()
    {
        using var service = new RendezvousJoinCodeService(client, RendezvousFactory.Secret);
        JoinCode code = await service.RegisterAsync(ServerAdvertisement.Explicit("203.0.113.9", 7778, TransportKind.Udp));

        Assert.That(factory.Store.Count, Is.EqualTo(1));
        Assert.That(await service.ResolveAsync(code), Is.EqualTo(JoinTarget.ForEndpoint("203.0.113.9", 7778)));
        Assert.That(await service.RevokeAsync(code), Is.True);
        Assert.That(factory.Store.Count, Is.EqualTo(0));
    }
}
