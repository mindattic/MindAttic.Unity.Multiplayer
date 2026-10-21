using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class GameLiftProvisionerTests
{
    private FakeGameLiftFleetApiClient _api = null!;
    private GameLiftProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;
    private ProviderConnection _connection = null!;
    private string _packageDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeGameLiftFleetApiClient();
        _provisioner = new GameLiftProvisioner(_api);
        _log = new RecordingDeployLog();
        _connection = new ProviderConnection("my-fleet", new Dictionary<string, string>(), new Dictionary<string, string>());

        _packageDirectory = Path.Combine(Path.GetTempPath(), "um-t30-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_packageDirectory);
        File.WriteAllText(Path.Combine(_packageDirectory, "Game.x86_64"), "fake binary");
        File.WriteAllText(Path.Combine(_packageDirectory, "start.sh"), "#!/bin/sh\nexec ./Game.x86_64 \"$@\"\n");
        File.WriteAllText(Path.Combine(_packageDirectory, "server.json"), "{}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }

    private ProvisionRequest BuildRequest(IReadOnlyDictionary<string, string>? options = null) => new(
        options is null ? _connection : _connection with { ProviderOptions = options },
        "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
        TestManifests.WithHealthPort(),
        new Dictionary<string, string>(),
        null, null,
        _packageDirectory);

    [Test]
    public void RequiresContainerImage_IsFalse()
    {
        Assert.That(_provisioner.RequiresContainerImage, Is.False);
    }

    [Test]
    public void ProvisionAsync_throws_when_PackageDirectory_is_missing()
    {
        var request = new ProvisionRequest(_connection, "ghcr.io/x:v1", "ghcr.io", "o", "t", TestManifests.WithHealthPort(), new Dictionary<string, string>(), null, null);
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(request, _log, CancellationToken.None));
    }

    [Test]
    public async Task ProvisionAsync_uploads_a_build_then_creates_a_fleet_and_alias()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        Assert.That(_api.CreateBuildCalls.Single().Name, Is.EqualTo("my-fleet"));

        var (build, zipPath) = _api.UploadCalls.Single();
        Assert.That(build.BuildId, Is.EqualTo("build-1"));
        Assert.That(File.Exists(zipPath), Is.False, "the temp zip must be cleaned up once uploaded");

        var fleetRequest = _api.CreateFleetCalls.Single();
        Assert.That(fleetRequest.Name, Is.EqualTo("my-fleet"));
        Assert.That(fleetRequest.BuildId, Is.EqualTo("build-1"));
        Assert.That(fleetRequest.Ec2InstanceType, Is.EqualTo("c5.large"));
        Assert.That(fleetRequest.FleetType, Is.EqualTo("ON_DEMAND"));
        Assert.That(fleetRequest.ConcurrentExecutions, Is.EqualTo(1));
        Assert.That(fleetRequest.LaunchPath, Is.EqualTo("/local/game/start.sh"));
        Assert.That(fleetRequest.LaunchParameters, Does.Contain("-port 7777"));
        Assert.That(fleetRequest.InboundPermissions, Has.Some.Matches<GameLiftPortRange>(p => p.FromPort == 7777 && p.Protocol == "udp"));

        Assert.That(_api.CreateAliasCalls.Single().FleetId, Is.EqualTo("fleet-1"));

        var (fleetId, aliasId, buildId) = GameLiftServerId.Split(result.ServerId);
        Assert.That(fleetId, Is.EqualTo("fleet-1"));
        Assert.That(aliasId, Is.EqualTo("alias-1"));
        Assert.That(buildId, Is.EqualTo("build-1"));
        Assert.That(result.PublicAddress, Is.EqualTo("fleet-1"));
        Assert.That(result.PortMappings[7777], Is.EqualTo(7777));
    }

    [Test]
    public async Task ProvisionAsync_reads_launch_options_from_ProviderOptions()
    {
        var options = new Dictionary<string, string> { ["ec2InstanceType"] = "c5.xlarge", ["fleetType"] = "SPOT", ["concurrentExecutions"] = "2" };
        await _provisioner.ProvisionAsync(BuildRequest(options), _log, CancellationToken.None);

        var fleetRequest = _api.CreateFleetCalls.Single();
        Assert.That(fleetRequest.Ec2InstanceType, Is.EqualTo("c5.xlarge"));
        Assert.That(fleetRequest.FleetType, Is.EqualTo("SPOT"));
        Assert.That(fleetRequest.ConcurrentExecutions, Is.EqualTo(2));
    }

    [Test]
    public async Task ProvisionAsync_waits_for_the_build_to_become_ready()
    {
        _api.DescribeBuildSequence = new Queue<GameLiftBuild>(new[]
        {
            new GameLiftBuild("build-1", "INITIALIZED", null, null, null, null, null, null),
            new GameLiftBuild("build-1", "READY", null, null, null, null, null, null),
        });

        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        Assert.That(_api.DescribeBuildCalls, Has.Count.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void ProvisionAsync_throws_when_the_build_fails()
    {
        _api.BuildToReturn = new GameLiftBuild("build-1", "FAILED", null, null, null, null, null, null);
        Assert.ThrowsAsync<InvalidOperationException>(() => _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task OpenFirewallAsync_reasserts_the_game_port()
    {
        string serverId = GameLiftServerId.Combine("fleet-1", "alias-1", "build-1");
        await _provisioner.OpenFirewallAsync(serverId, BuildRequest(), _log, CancellationToken.None);

        var (fleetId, ports) = _api.UpdatePortsCalls.Single();
        Assert.That(fleetId, Is.EqualTo("fleet-1"));
        Assert.That(ports, Has.Some.Matches<GameLiftPortRange>(p => p.FromPort == 7777 && p.Protocol == "udp"));
    }

    [Test]
    public async Task WaitForHealthyAsync_true_once_the_fleet_is_active()
    {
        _api.DescribeFleetSequence = new Queue<GameLiftFleet>(new[]
        {
            new GameLiftFleet("fleet-1", "ACTIVATING", null),
            new GameLiftFleet("fleet-1", "ACTIVE", null),
        });
        string serverId = GameLiftServerId.Combine("fleet-1", "alias-1", "build-1");
        bool healthy = await _provisioner.WaitForHealthyAsync(serverId, BuildRequest(), TimeSpan.FromSeconds(30), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
    }

    [Test]
    public async Task WaitForHealthyAsync_false_on_fleet_error()
    {
        _api.FleetToReturn = new GameLiftFleet("fleet-1", "ERROR", null);
        string serverId = GameLiftServerId.Combine("fleet-1", "alias-1", "build-1");
        bool healthy = await _provisioner.WaitForHealthyAsync(serverId, BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None);
        Assert.That(healthy, Is.False);
    }

    [Test]
    public async Task GetStatusAsync_maps_fleet_status()
    {
        _api.FleetToReturn = new GameLiftFleet("fleet-1", "ACTIVE", null);
        string serverId = GameLiftServerId.Combine("fleet-1", "alias-1", "build-1");
        var status = await _provisioner.GetStatusAsync(serverId, _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Running));
    }

    [Test]
    public async Task GetLogsAsync_formats_fleet_events()
    {
        _api.EventsToReturn = new List<GameLiftEvent> { new("evt-1", "FLEET_CREATED", "Fleet created.", DateTimeOffset.UnixEpoch) };
        string serverId = GameLiftServerId.Combine("fleet-1", "alias-1", "build-1");
        string logs = await _provisioner.GetLogsAsync(serverId, 50, _connection, _log, CancellationToken.None);
        Assert.That(logs, Does.Contain("FLEET_CREATED").And.Contain("Fleet created."));
    }

    [Test]
    public async Task UpdateAsync_creates_a_new_fleet_then_repoints_the_existing_alias()
    {
        string serverId = GameLiftServerId.Combine("fleet-1", "alias-1", "build-1");
        _api.FleetToReturn = new GameLiftFleet("fleet-2", "ACTIVE", null);
        _api.AliasIdToReturn = "alias-2";

        await _provisioner.UpdateAsync(serverId, BuildRequest(), _log, CancellationToken.None);

        Assert.That(_api.CreateFleetCalls, Has.Count.EqualTo(1));
        var (repointedAlias, newFleet) = _api.UpdateAliasCalls.Single();
        Assert.That(repointedAlias, Is.EqualTo("alias-1"));
        Assert.That(newFleet, Is.EqualTo("fleet-2"));
        Assert.That(_api.DeleteAliasCalls, Is.EqualTo(new[] { "alias-2" }), "the fleet's own freshly-created alias is redundant once the existing alias is repointed");
    }

    [Test]
    public async Task DestroyAsync_deletes_alias_fleet_and_build()
    {
        string serverId = GameLiftServerId.Combine("fleet-1", "alias-1", "build-1");
        await _provisioner.DestroyAsync(serverId, _connection, _log, CancellationToken.None);

        Assert.That(_api.DeleteAliasCalls, Is.EqualTo(new[] { "alias-1" }));
        Assert.That(_api.DeleteFleetCalls, Is.EqualTo(new[] { "fleet-1" }));
        Assert.That(_api.DeleteBuildCalls, Is.EqualTo(new[] { "build-1" }));
    }
}
