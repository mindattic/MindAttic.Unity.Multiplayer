using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class Ec2ProvisionerTests
{
    private FakeEc2ApiClient _api = null!;
    private FakeSshCommandRunner _ssh = null!;
    private Ec2Provisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;
    private ProviderConnection _connection = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeEc2ApiClient();
        _ssh = new FakeSshCommandRunner();
        _provisioner = new Ec2Provisioner(_api, _ssh);
        _log = new RecordingDeployLog();
        _connection = new ProviderConnection("ec2-box", new Dictionary<string, string>
        {
            ["amiId"] = "ami-123",
            ["keyName"] = "my-keypair",
            ["sshUser"] = "ubuntu",
            ["sshKeyPath"] = "/keys/id_ed25519",
        }, new Dictionary<string, string>());
    }

    private ProvisionRequest BuildRequest() => new(
        _connection, "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
        TestManifests.WithHealthPort(),
        new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_URL"] = "http://rendezvous:8080" },
        null, null);

    [Test]
    public async Task ProvisionAsync_creates_a_security_group_then_launches_an_instance()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        Assert.That(_api.CreateGroupCalls.Single().Name, Is.EqualTo("mp-host-ec2-box"));
        var run = _api.RunCalls.Single();
        Assert.That(run.ImageId, Is.EqualTo("ami-123"));
        Assert.That(run.KeyName, Is.EqualTo("my-keypair"));
        Assert.That(run.SecurityGroupId, Is.EqualTo("sg-1"));
        Assert.That(result.ServerId, Is.EqualTo("i-1|sg-1"));
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.10"));
        Assert.That(result.PortMappings[7777], Is.EqualTo(7777));
    }

    [Test]
    public void ProvisionAsync_throws_when_amiId_is_missing()
    {
        _connection = new ProviderConnection("ec2-box", new Dictionary<string, string> { ["keyName"] = "k", ["sshKeyPath"] = "/k" }, new Dictionary<string, string>());
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task ProvisionAsync_waits_for_a_public_ip_before_returning()
    {
        _api.DescribeSequence = new Queue<Ec2Instance>(new[]
        {
            new Ec2Instance("i-1", "pending", null),
            new Ec2Instance("i-1", "running", "198.51.100.10"),
        });
        _api.InstanceToReturn = new Ec2Instance("i-1", "pending", null);
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.10"));
    }

    [Test]
    public async Task OpenFirewallAsync_authorizes_ssh_rendezvous_and_the_declared_game_port()
    {
        await _provisioner.OpenFirewallAsync("i-1|sg-1", BuildRequest(), _log, CancellationToken.None);
        var (groupId, rules) = _api.AuthorizeCalls.Single();
        Assert.That(groupId, Is.EqualTo("sg-1"));
        Assert.That(rules, Has.Some.Matches<Ec2IngressRule>(r => r.Port == 22 && r.Protocol == "tcp"));
        Assert.That(rules, Has.Some.Matches<Ec2IngressRule>(r => r.Port == 8080 && r.Protocol == "tcp"));
        Assert.That(rules, Has.Some.Matches<Ec2IngressRule>(r => r.Port == 7777 && r.Protocol == "udp"));
        Assert.That(rules, Has.Count.EqualTo(3), "the health port must not be opened to the internet");
    }

    [Test]
    public async Task WaitForHealthyAsync_probes_over_ssh_once_the_public_ip_is_known()
    {
        _ssh.Responses.Add(("curl", new SshCommandResult(0, "200", "")));
        bool healthy = await _provisioner.WaitForHealthyAsync("i-1|sg-1", BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
        Assert.That(_ssh.RunCalls.Single().Target.Host, Is.EqualTo("198.51.100.10"));
    }

    [Test]
    public async Task GetStatusAsync_maps_ec2_state_directly_when_not_running()
    {
        _api.InstanceToReturn = new Ec2Instance("i-1", "pending", null);
        var status = await _provisioner.GetStatusAsync("i-1|sg-1", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Provisioning));
        Assert.That(_ssh.RunCalls, Is.Empty);
    }

    [Test]
    public async Task GetStatusAsync_checks_compose_over_ssh_once_running()
    {
        _ssh.Responses.Add(("ps --status running", new SshCommandResult(0, "game\n", "")));
        var status = await _provisioner.GetStatusAsync("i-1|sg-1", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Running));
    }

    [Test]
    public async Task GetLogsAsync_tails_compose_logs_over_ssh()
    {
        _ssh.Responses.Add(("logs --no-color", new SshCommandResult(0, "[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ\n", "")));
        string logs = await _provisioner.GetLogsAsync("i-1|sg-1", 50, _connection, _log, CancellationToken.None);
        Assert.That(logs, Does.Contain("ABC-DEF-GHJ"));
    }

    [Test]
    public async Task UpdateAsync_re_uploads_compose_and_restarts_in_place()
    {
        await _provisioner.UpdateAsync("i-1|sg-1", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_ssh.UploadCalls.Single().RemotePath, Is.EqualTo("docker-compose.yml"));
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("pull").And.Contain("up -d"));
    }

    [Test]
    public async Task DestroyAsync_stops_compose_then_terminates_the_instance_and_deletes_the_group()
    {
        await _provisioner.DestroyAsync("i-1|sg-1", _connection, _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Any(c => c.Command.Contains("down -v")), Is.True);
        Assert.That(_api.TerminateCalls, Is.EqualTo(new[] { "i-1" }));
        Assert.That(_api.DeleteGroupCalls, Is.EqualTo(new[] { "sg-1" }));
    }
}
