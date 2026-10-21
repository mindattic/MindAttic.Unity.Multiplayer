using Microsoft.Extensions.Options;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer.Rendezvous.Tests;

[TestFixture]
public class RegistrationStoreTests
{
    private static readonly JoinTarget Server = JoinTarget.ForEndpoint("203.0.113.7", 7777);

    private ManualTimeProvider time = null!;
    private RegistrationStore store = null!;

    [SetUp]
    public void SetUp()
    {
        time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        store = new RegistrationStore(Options.Create(new RendezvousOptions { TtlSeconds = 30, MaxRegistrations = 3 }), time);
    }

    [Test]
    public void Register_ResolvesUntilTtl()
    {
        Registration registration = store.Register(Server)!;

        Assert.That(registration.ExpiresAt, Is.EqualTo(time.GetUtcNow().AddSeconds(30)));
        Assert.That(store.Resolve(registration.Code)!.Target, Is.EqualTo(Server));

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.That(store.Resolve(registration.Code), Is.Null);
        Assert.That(store.Count, Is.Zero, "an expired entry read is dropped at once");
    }

    [Test]
    public void Tokens_AreUniqueAndUrlSafe()
    {
        Registration a = store.Register(Server)!;
        Registration b = store.Register(Server)!;

        Assert.That(a.Token, Is.Not.EqualTo(b.Token));
        Assert.That(a.Token, Does.Match("^[A-Za-z0-9_-]{43}$"));
    }

    [Test]
    public void SweepExpired_DropsOnlyExpired()
    {
        Registration old = store.Register(Server)!;
        time.Advance(TimeSpan.FromSeconds(20));
        Registration fresh = store.Register(Server)!;
        time.Advance(TimeSpan.FromSeconds(15));

        Assert.That(store.SweepExpired(), Is.EqualTo(1));
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.Resolve(old.Code), Is.Null);
        Assert.That(store.Resolve(fresh.Code), Is.Not.Null);
    }

    [Test]
    public void Register_WhenFull_ReturnsNullUntilSomethingExpires()
    {
        for (int i = 0; i < 3; i++)
        {
            Assert.That(store.Register(Server), Is.Not.Null);
        }

        Assert.That(store.Register(Server), Is.Null);

        time.Advance(TimeSpan.FromSeconds(31));
        Assert.That(store.Register(Server), Is.Not.Null);
    }

    [Test]
    public void Heartbeat_And_Remove_NeedTheToken()
    {
        Registration registration = store.Register(Server)!;

        Assert.That(store.Heartbeat(registration.Code, "nope"), Is.Null);
        Assert.That(store.Heartbeat(registration.Code, null), Is.Null);
        Assert.That(store.Remove(registration.Code, ""), Is.False);

        time.Advance(TimeSpan.FromSeconds(10));
        Registration renewed = store.Heartbeat(registration.Code, registration.Token)!;
        Assert.That(renewed.ExpiresAt, Is.EqualTo(time.GetUtcNow().AddSeconds(30)));
        Assert.That(renewed.Token, Is.EqualTo(registration.Token));

        Assert.That(store.Remove(registration.Code, registration.Token), Is.True);
        Assert.That(store.Remove(registration.Code, registration.Token), Is.False);
    }

    [Test]
    public void EmptyCode_IsNeverFound()
    {
        store.Register(Server);

        Assert.That(store.Resolve(JoinCode.Empty), Is.Null);
        Assert.That(store.Heartbeat(JoinCode.Empty, "x"), Is.Null);
    }
}
