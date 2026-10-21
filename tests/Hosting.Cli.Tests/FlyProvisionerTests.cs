using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class FlyProvisionerTests
{
    private FakeFlyApiClient _api = null!;
    private FakeFlyCli _cli = null!;
    private FlyProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeFlyApiClient();
        _cli = new FakeFlyCli();
        _provisioner = new FlyProvisioner(_api, _cli);
        _log = new RecordingDeployLog();
    }

    private static ProviderConnection Connection(string serverName = "fly-server", string? rendezvousMode = null, string? rendezvousImage = null)
    {
        var options = new Dictionary<string, string> { ["orgSlug"] = "personal" };
        if (rendezvousMode is not null) options["rendezvousMode"] = rendezvousMode;
        if (rendezvousImage is not null) options["rendezvousImage"] = rendezvousImage;
        return new ProviderConnection(serverName, options, new Dictionary<string, string>());
    }

    private static ProvisionRequest BuildRequest(ProviderConnection? connection = null, string? region = null, string? size = null) => new(
        connection ?? Connection(), "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
        TestManifests.WithHealthPort(),
        new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "s3cr3t" },
        region, size);

    [Test]
    public async Task ProvisionAsync_requires_an_org_slug()
    {
        var connection = new ProviderConnection("fly-server", new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(BuildRequest(connection), _log, CancellationToken.None));
    }

    [Test]
    public async Task ProvisionAsync_creates_the_app_and_a_dedicated_ipv4_for_udp()
    {
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        Assert.That(_api.EnsureAppCalls.Single(), Is.EqualTo(("fly-server", "personal")));
        Assert.That(_api.AllocateDedicatedCalls, Is.EqualTo(new[] { "fly-server" }));
    }

    [Test]
    public async Task ProvisionAsync_creates_the_game_machine_binding_fly_global_services_for_udp()
    {
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        var (appName, request) = _api.CreateCalls.First();
        Assert.That(appName, Is.EqualTo("fly-server"));
        Assert.That(request.ProcessGroup, Is.EqualTo(FlyProvisioner.GameProcessGroup));
        Assert.That(request.Image, Is.EqualTo("ghcr.io/mindattic/game:v1"));
        Assert.That(request.Env["MINDATTIC_BIND_HOST"], Is.EqualTo(FlyTomlGenerator.UdpBindHost));
        Assert.That(request.Services.Single(), Is.EqualTo(new FlyServicePort(7777, "udp", 7777)));
    }

    [Test]
    public async Task ProvisionAsync_defaults_rendezvous_to_a_co_located_process_group()
    {
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        Assert.That(_api.CreateCalls, Has.Count.EqualTo(2));
        var rendezvous = _api.CreateCalls[1];
        Assert.That(rendezvous.AppName, Is.EqualTo("fly-server"));
        Assert.That(rendezvous.Request.ProcessGroup, Is.EqualTo(FlyProvisioner.RendezvousProcessGroup));
        Assert.That(rendezvous.Request.Image, Is.EqualTo("ghcr.io/mindattic/rendezvous:latest"));
        Assert.That(_api.AllocateSharedCalls, Is.Empty);
    }

    [Test]
    public async Task ProvisionAsync_rendezvousMode_app_creates_a_separate_app_with_a_shared_ipv4()
    {
        var connection = Connection(rendezvousMode: FlyTomlGenerator.AppRendezvousMode);
        await _provisioner.ProvisionAsync(BuildRequest(connection), _log, CancellationToken.None);

        Assert.That(_api.EnsureAppCalls, Is.EqualTo(new[] { ("fly-server", "personal"), ("fly-server-rendezvous", "personal") }));
        Assert.That(_api.AllocateSharedCalls, Is.EqualTo(new[] { "fly-server-rendezvous" }));
        var rendezvous = _api.CreateCalls.Single(c => c.AppName == "fly-server-rendezvous");
        Assert.That(rendezvous.Request.ProcessGroup, Is.EqualTo(FlyProvisioner.GameProcessGroup));
    }

    [Test]
    public async Task ProvisionAsync_rejects_an_unknown_rendezvous_mode()
    {
        var connection = Connection(rendezvousMode: "sidecar");
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(BuildRequest(connection), _log, CancellationToken.None));
    }

    [Test]
    public async Task ProvisionAsync_returns_the_dedicated_ipv4_as_the_public_address()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        Assert.That(result.ServerId, Is.EqualTo("fly-server"));
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.10"));
        Assert.That(result.PortMappings[7777], Is.EqualTo(7777));
    }

    [Test]
    public async Task OpenFirewallAsync_is_a_documented_no_op()
    {
        await _provisioner.OpenFirewallAsync("fly-server", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_api.CreateCalls, Is.Empty);
    }

    [Test]
    public async Task WaitForHealthyAsync_polls_until_the_game_machine_is_started()
    {
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        bool healthy = await _provisioner.WaitForHealthyAsync("fly-server", BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
    }

    [Test]
    public async Task WaitForHealthyAsync_gives_up_after_the_timeout_when_there_is_no_app_yet()
    {
        bool healthy = await _provisioner.WaitForHealthyAsync("never-provisioned", BuildRequest(), TimeSpan.Zero, _log, CancellationToken.None);
        Assert.That(healthy, Is.False);
    }

    [Test]
    public async Task GetStatusAsync_maps_the_game_machines_state()
    {
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        var status = await _provisioner.GetStatusAsync("fly-server", Connection(), _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Running));
        Assert.That(status.PublicAddress, Is.EqualTo("198.51.100.10"));
    }

    [Test]
    public async Task GetStatusAsync_reports_Gone_when_the_app_has_no_machines()
    {
        var status = await _provisioner.GetStatusAsync("missing-app", Connection(), _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Gone));
    }

    [Test]
    public async Task GetLogsAsync_delegates_to_flyctl()
    {
        _cli.LogsToReturn = "[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ";
        string logs = await _provisioner.GetLogsAsync("fly-server", 50, Connection(), _log, CancellationToken.None);
        Assert.That(logs, Does.Contain("ABC-DEF-GHJ"));
        Assert.That(_cli.LogCalls, Is.EqualTo(new[] { "fly-server" }));
    }

    [Test]
    public async Task UpdateAsync_replaces_the_game_machines_config_in_place()
    {
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        var updateRequest = BuildRequest() with { ImageReference = "ghcr.io/mindattic/game:v2" };

        await _provisioner.UpdateAsync("fly-server", updateRequest, _log, CancellationToken.None);

        var (appName, machineId, request) = _api.UpdateCalls.Single();
        Assert.That(appName, Is.EqualTo("fly-server"));
        Assert.That(machineId, Is.Not.Empty);
        Assert.That(request.Image, Is.EqualTo("ghcr.io/mindattic/game:v2"));
        Assert.That(_api.CreateCalls, Has.Count.EqualTo(2)); // only the original provision created machines
    }

    [Test]
    public void UpdateAsync_throws_when_the_app_has_no_game_machine()
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => _provisioner.UpdateAsync("never-provisioned", BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task DestroyAsync_destroys_every_machine_then_the_app()
    {
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        await _provisioner.DestroyAsync("fly-server", Connection(), _log, CancellationToken.None);

        Assert.That(_api.DestroyMachineCalls, Has.Count.EqualTo(2));
        Assert.That(_api.DestroyAppCalls, Is.EqualTo(new[] { "fly-server" }));
    }

    [Test]
    public async Task DestroyAsync_also_destroys_the_separate_rendezvous_app_in_app_mode()
    {
        var connection = Connection(rendezvousMode: FlyTomlGenerator.AppRendezvousMode);
        await _provisioner.ProvisionAsync(BuildRequest(connection), _log, CancellationToken.None);
        await _provisioner.DestroyAsync("fly-server", connection, _log, CancellationToken.None);

        Assert.That(_api.DestroyAppCalls, Is.EqualTo(new[] { "fly-server", "fly-server-rendezvous" }));
    }
}
