using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class OciProvisionerTests
{
    private FakeOciApiClient _api = null!;
    private FakeSshCommandRunner _ssh = null!;
    private OciProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;
    private ProviderConnection _connection = null!;
    private string _publicKeyFile = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeOciApiClient();
        _ssh = new FakeSshCommandRunner();
        _provisioner = new OciProvisioner(_api, _ssh);
        _log = new RecordingDeployLog();
        _publicKeyFile = Path.Combine(Path.GetTempPath(), "mp-host-oci-pub-" + Guid.NewGuid().ToString("N") + ".pub");
        File.WriteAllText(_publicKeyFile, "ssh-ed25519 AAAATESTKEY test@example.com\n");
        _connection = new ProviderConnection("oci-box", new Dictionary<string, string>
        {
            ["compartmentId"] = "ocid1.compartment.oc1..c",
            ["availabilityDomain"] = "AD-1",
            ["subnetId"] = "ocid1.subnet.oc1..s",
            ["securityListId"] = "ocid1.securitylist.oc1..sl",
            ["imageId"] = "ocid1.image.oc1..i",
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
    public async Task ProvisionAsync_launches_with_the_default_amd_micro_shape()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        var launch = _api.LaunchCalls.Single();
        Assert.That(launch.Shape, Is.EqualTo("VM.Standard.E2.1.Micro"));
        Assert.That(launch.AvailabilityDomain, Is.EqualTo("AD-1"));
        Assert.That(launch.SshPublicKey, Does.Contain("AAAATESTKEY"));
        Assert.That(result.ServerId, Is.EqualTo("ocid1.instance.oc1..srv"));
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.50"));
    }

    [Test]
    public async Task ProvisionAsync_uses_a_flex_shape_and_shape_config_when_given()
    {
        _connection = _connection with
        {
            ProviderOptions = new Dictionary<string, string>(_connection.ProviderOptions)
            {
                ["shape"] = "VM.Standard.A1.Flex",
                ["ocpus"] = "4",
                ["memoryInGBs"] = "24",
            },
        };
        await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        var launch = _api.LaunchCalls.Single();
        Assert.That(launch.Shape, Is.EqualTo("VM.Standard.A1.Flex"));
        Assert.That(launch.Ocpus, Is.EqualTo(4));
        Assert.That(launch.MemoryInGBs, Is.EqualTo(24));
    }

    [Test]
    public void ProvisionAsync_throws_when_a_required_option_is_missing()
    {
        _connection = new ProviderConnection("oci-box", new Dictionary<string, string> { ["compartmentId"] = "c" }, new Dictionary<string, string>());
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task OpenFirewallAsync_widens_the_existing_security_list_without_dropping_existing_rules()
    {
        _api.ExistingRules = new[] { new OciSecurityRule("tcp", 443, "https") };
        await _provisioner.OpenFirewallAsync("ocid1.instance.oc1..srv", BuildRequest(), _log, CancellationToken.None);

        var (securityListId, rules) = _api.SetRulesCalls.Single();
        Assert.That(securityListId, Is.EqualTo("ocid1.securitylist.oc1..sl"));
        Assert.That(rules.Any(r => r.Protocol == "tcp" && r.Port == 443), Is.True, "existing rule dropped");
        Assert.That(rules.Any(r => r.Protocol == "tcp" && r.Port == 22), Is.True);
        Assert.That(rules.Any(r => r.Protocol == "udp" && r.Port == 7777), Is.True);
    }

    [Test]
    public async Task OpenFirewallAsync_does_not_duplicate_an_already_present_rule()
    {
        _api.ExistingRules = new[] { new OciSecurityRule("udp", 7777, "game") };
        await _provisioner.OpenFirewallAsync("ocid1.instance.oc1..srv", BuildRequest(), _log, CancellationToken.None);

        var (_, rules) = _api.SetRulesCalls.Single();
        Assert.That(rules.Count(r => r.Protocol == "udp" && r.Port == 7777), Is.EqualTo(1));
    }

    [Test]
    public async Task WaitForHealthyAsync_probes_over_ssh()
    {
        _ssh.Responses.Add(("curl", new SshCommandResult(0, "200", "")));
        bool healthy = await _provisioner.WaitForHealthyAsync("ocid1.instance.oc1..srv", BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
    }

    [Test]
    public async Task GetStatusAsync_maps_lifecycle_state_directly_when_not_running()
    {
        _api.InstanceToReturn = new OciInstance("ocid1.instance.oc1..srv", "PROVISIONING", null);
        var status = await _provisioner.GetStatusAsync("ocid1.instance.oc1..srv", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Provisioning));
    }

    [Test]
    public async Task GetStatusAsync_maps_terminated_to_gone()
    {
        _api.InstanceToReturn = new OciInstance("ocid1.instance.oc1..srv", "TERMINATED", null);
        var status = await _provisioner.GetStatusAsync("ocid1.instance.oc1..srv", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Gone));
    }

    [Test]
    public async Task UpdateAsync_restarts_in_place_over_ssh()
    {
        await _provisioner.UpdateAsync("ocid1.instance.oc1..srv", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("pull").And.Contain("up -d"));
    }

    [Test]
    public async Task DestroyAsync_stops_compose_and_terminates_the_instance_but_leaves_the_security_list_alone()
    {
        await _provisioner.DestroyAsync("ocid1.instance.oc1..srv", _connection, _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Any(c => c.Command.Contains("down -v")), Is.True);
        Assert.That(_api.TerminateCalls, Is.EqualTo(new[] { "ocid1.instance.oc1..srv" }));
        Assert.That(_api.SetRulesCalls, Is.Empty);
    }
}
