using System.Net;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpFlyApiClientTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler, HttpStatusCode status = HttpStatusCode.OK)
    {
        handler = new FakeHttpMessageHandler(respond, status);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.machines.dev/v1/") };
    }

    [Test]
    public async Task EnsureAppAsync_posts_the_app_name_and_org()
    {
        var http = MakeClient(_ => "{}", out var handler);
        await new HttpFlyApiClient(http).EnsureAppAsync("my-app", "personal", CancellationToken.None);
        Assert.That(handler.RequestBodies.Single(), Does.Contain("my-app").And.Contain("personal"));
    }

    [Test]
    public async Task EnsureAppAsync_treats_a_name_conflict_as_already_exists()
    {
        var http = MakeClient(_ => "{\"error\":\"already taken\"}", out _, HttpStatusCode.UnprocessableEntity);
        Assert.DoesNotThrowAsync(() => new HttpFlyApiClient(http).EnsureAppAsync("my-app", "personal", CancellationToken.None));
    }

    [Test]
    public async Task AllocateDedicatedIpv4Async_sends_type_v4_and_parses_the_address_back()
    {
        const string response = """{ "id": "ip-1", "address": "66.241.1.1", "type": "v4" }""";
        var http = MakeClient(_ => response, out var handler);
        var ip = await new HttpFlyApiClient(http).AllocateDedicatedIpv4Async("my-app", "iad", CancellationToken.None);

        Assert.That(ip.Address, Is.EqualTo("66.241.1.1"));
        Assert.That(ip.Type, Is.EqualTo("v4"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("\"type\":\"v4\"").Or.Contain("\"type\": \"v4\""));
    }

    [Test]
    public async Task CreateMachineAsync_sends_the_image_region_process_group_and_services_then_parses_the_machine_back()
    {
        const string response = """
            { "id": "machine-1", "name": "my-app-game", "state": "started", "region": "iad",
              "config": { "metadata": { "fly_process_group": "app" } } }
            """;
        var http = MakeClient(_ => response, out var handler);
        var request = new FlyCreateMachineRequest(
            "my-app-game", "iad", "ghcr.io/x/y:v1", "app", new FlyGuest("shared", 1, 256),
            new Dictionary<string, string> { ["MINDATTIC_BIND_HOST"] = "fly-global-services" },
            new List<FlyServicePort> { new(7777, "udp", 7777) });

        var machine = await new HttpFlyApiClient(http).CreateMachineAsync("my-app", request, CancellationToken.None);

        Assert.That(machine.Id, Is.EqualTo("machine-1"));
        Assert.That(machine.State, Is.EqualTo("started"));
        Assert.That(machine.ProcessGroup, Is.EqualTo("app"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("ghcr.io/x/y:v1").And.Contain("fly-global-services").And.Contain("7777"));
    }

    [Test]
    public async Task ListMachinesAsync_parses_every_machine_in_the_array()
    {
        const string response = """
            [
              { "id": "m1", "name": "my-app-game", "state": "started", "config": { "metadata": { "fly_process_group": "app" } } },
              { "id": "m2", "name": "my-app-rendezvous", "state": "started", "config": { "metadata": { "fly_process_group": "rendezvous" } } }
            ]
            """;
        var http = MakeClient(_ => response, out _);
        var machines = await new HttpFlyApiClient(http).ListMachinesAsync("my-app", CancellationToken.None);

        Assert.That(machines, Has.Count.EqualTo(2));
        Assert.That(machines[0].ProcessGroup, Is.EqualTo("app"));
        Assert.That(machines[1].ProcessGroup, Is.EqualTo("rendezvous"));
    }

    [Test]
    public async Task DestroyMachineAsync_tolerates_an_already_gone_machine()
    {
        var http = MakeClient(_ => "", out _, HttpStatusCode.NotFound);
        Assert.DoesNotThrowAsync(() => new HttpFlyApiClient(http).DestroyMachineAsync("my-app", "m1", CancellationToken.None));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var http = MakeClient(_ => "{\"error\":\"unauthorized\"}", out _, HttpStatusCode.Unauthorized);
        Assert.ThrowsAsync<InvalidOperationException>(() => new HttpFlyApiClient(http).GetMachineAsync("my-app", "m1", CancellationToken.None));
    }
}
