using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class GceProvisionerTests
{
    private FakeGceApiClient _api = null!;
    private FakeSshCommandRunner _ssh = null!;
    private GceProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;
    private ProviderConnection _connection = null!;
    private string _publicKeyFile = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeGceApiClient();
        _ssh = new FakeSshCommandRunner();
        _provisioner = new GceProvisioner(_api, _ssh);
        _log = new RecordingDeployLog();
        _publicKeyFile = Path.Combine(Path.GetTempPath(), "mp-host-gce-pub-" + Guid.NewGuid().ToString("N") + ".pub");
        File.WriteAllText(_publicKeyFile, "ssh-ed25519 AAAATESTKEY test@example.com\n");
        _connection = new ProviderConnection("gce-box", new Dictionary<string, string>
        {
            ["sshUser"] = "ubuntu",
            ["sshKeyPath"] = "/keys/id_ed25519",
            ["sshPublicKeyPath"] = _publicKeyFile,
        }, new Dictionary<string, string>());
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_publicKeyFile)) File.Delete(_publicKeyFile);
    }

    private ProvisionRequest BuildRequest() => new(
        _connection, "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
        TestManifests.WithHealthPort(),
        new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_URL"] = "http://rendezvous:8080" },
        null, null);

    [Test]
    public async Task ProvisionAsync_inserts_an_instance_with_the_default_zone_and_machine_type()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        var insert = _api.InsertCalls.Single();
        Assert.That(insert.Zone, Is.EqualTo("us-central1-a"));
        Assert.That(insert.MachineType, Is.EqualTo("e2-small"));
        Assert.That(insert.SshPublicKey, Does.Contain("AAAATESTKEY"));
        Assert.That(result.ServerId, Is.EqualTo("us-central1-a|srv"));
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.30"));
    }

    [Test]
    public void ProvisionAsync_throws_when_sshPublicKeyPath_is_missing()
    {
        _connection = new ProviderConnection("gce-box", new Dictionary<string, string> { ["sshKeyPath"] = "/k" }, new Dictionary<string, string>());
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task OpenFirewallAsync_creates_a_rule_named_and_tagged_after_the_server()
    {
        await _provisioner.OpenFirewallAsync("us-central1-a|srv", BuildRequest(), _log, CancellationToken.None);
        var (name, network, tag, allowed) = _api.CreateFirewallCalls.Single();
        Assert.That(name, Is.EqualTo("mp-host-gce-box"));
        Assert.That(tag, Is.EqualTo("mp-host-gce-box"));
        Assert.That(network, Is.EqualTo("global/networks/default"));
        Assert.That(allowed.Any(a => a.IpProtocol == "tcp" && a.Ports.Contains("22") && a.Ports.Contains("8080")), Is.True);
        Assert.That(allowed.Any(a => a.IpProtocol == "udp" && a.Ports.Contains("7777")), Is.True);
    }

    [Test]
    public async Task WaitForHealthyAsync_probes_over_ssh()
    {
        _ssh.Responses.Add(("curl", new SshCommandResult(0, "200", "")));
        bool healthy = await _provisioner.WaitForHealthyAsync("us-central1-a|srv", BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
    }

    [Test]
    public async Task GetStatusAsync_maps_gce_status_directly_when_not_running()
    {
        _api.InstanceToReturn = new GceInstance("srv", "PROVISIONING", null);
        var status = await _provisioner.GetStatusAsync("us-central1-a|srv", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Provisioning));
    }

    [Test]
    public async Task UpdateAsync_restarts_in_place_over_ssh()
    {
        await _provisioner.UpdateAsync("us-central1-a|srv", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("pull").And.Contain("up -d"));
    }

    [Test]
    public async Task DestroyAsync_stops_compose_deletes_the_instance_and_the_firewall_rule()
    {
        await _provisioner.DestroyAsync("us-central1-a|srv", _connection, _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Any(c => c.Command.Contains("down -v")), Is.True);
        Assert.That(_api.DeleteInstanceCalls, Is.EqualTo(new[] { ("us-central1-a", "srv") }));
        Assert.That(_api.DeleteFirewallCalls, Is.EqualTo(new[] { "mp-host-gce-box" }));
    }
}
