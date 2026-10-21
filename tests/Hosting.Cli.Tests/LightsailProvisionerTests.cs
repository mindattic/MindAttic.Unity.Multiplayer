using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class LightsailProvisionerTests
{
    private FakeLightsailApiClient _api = null!;
    private FakeSshCommandRunner _ssh = null!;
    private LightsailProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;
    private ProviderConnection _connection = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeLightsailApiClient();
        _ssh = new FakeSshCommandRunner();
        _provisioner = new LightsailProvisioner(_api, _ssh);
        _log = new RecordingDeployLog();
        _connection = new ProviderConnection("ls-box", new Dictionary<string, string>
        {
            ["keyPairName"] = "my-keypair",
            ["sshUser"] = "ubuntu",
            ["sshKeyPath"] = "/keys/id_ed25519",
            ["availabilityZone"] = "us-east-1a",
        }, new Dictionary<string, string>());
    }

    private ProvisionRequest BuildRequest() => new(
        _connection, "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
        TestManifests.WithHealthPort(),
        new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_URL"] = "http://rendezvous:8080" },
        null, null);

    [Test]
    public async Task ProvisionAsync_creates_an_instance_with_the_declared_blueprint_and_bundle()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        var create = _api.CreateCalls.Single();
        Assert.That(create.BlueprintId, Is.EqualTo("ubuntu_22_04"));
        Assert.That(create.BundleId, Is.EqualTo("nano_3_0"));
        Assert.That(create.AvailabilityZone, Is.EqualTo("us-east-1a"));
        Assert.That(result.ServerId, Is.EqualTo("srv"));
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.20"));
    }

    [Test]
    public void ProvisionAsync_throws_when_no_zone_or_region_is_given()
    {
        _connection = new ProviderConnection("ls-box", new Dictionary<string, string> { ["keyPairName"] = "k", ["sshKeyPath"] = "/k" }, new Dictionary<string, string>());
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task OpenFirewallAsync_replaces_public_ports_with_ssh_rendezvous_and_the_game_port()
    {
        await _provisioner.OpenFirewallAsync("srv", BuildRequest(), _log, CancellationToken.None);
        var (name, ports) = _api.OpenPortsCalls.Single();
        Assert.That(name, Is.EqualTo("srv"));
        Assert.That(ports, Has.Some.Matches<LightsailPortRange>(p => p.FromPort == 22 && p.Protocol == "tcp"));
        Assert.That(ports, Has.Some.Matches<LightsailPortRange>(p => p.FromPort == 8080 && p.Protocol == "tcp"));
        Assert.That(ports, Has.Some.Matches<LightsailPortRange>(p => p.FromPort == 7777 && p.Protocol == "udp"));
    }

    [Test]
    public async Task WaitForHealthyAsync_probes_over_ssh()
    {
        _ssh.Responses.Add(("curl", new SshCommandResult(0, "200", "")));
        bool healthy = await _provisioner.WaitForHealthyAsync("srv", BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
    }

    [Test]
    public async Task GetStatusAsync_maps_lightsail_state_directly_when_not_running()
    {
        _api.InstanceToReturn = new LightsailInstance("srv", "pending", null);
        var status = await _provisioner.GetStatusAsync("srv", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Provisioning));
    }

    [Test]
    public async Task UpdateAsync_restarts_in_place_over_ssh()
    {
        await _provisioner.UpdateAsync("srv", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("pull").And.Contain("up -d"));
    }

    [Test]
    public async Task DestroyAsync_stops_compose_then_deletes_the_instance()
    {
        await _provisioner.DestroyAsync("srv", _connection, _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Any(c => c.Command.Contains("down -v")), Is.True);
        Assert.That(_api.DeleteCalls, Is.EqualTo(new[] { "srv" }));
    }
}
