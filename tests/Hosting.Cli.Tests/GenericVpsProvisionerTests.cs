using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class GenericVpsProvisionerTests
{
    private FakeSshCommandRunner _ssh = null!;
    private GenericVpsProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;
    private ProviderConnection _connection = null!;

    [SetUp]
    public void SetUp()
    {
        _ssh = new FakeSshCommandRunner();
        _provisioner = new GenericVpsProvisioner(_ssh);
        _log = new RecordingDeployLog();
        _connection = new ProviderConnection("prod-box", new Dictionary<string, string>
        {
            ["sshHost"] = "203.0.113.5",
            ["sshPort"] = "2222",
            ["sshUser"] = "deploy",
            ["sshKeyPath"] = "/keys/id_ed25519",
        }, new Dictionary<string, string>());
    }

    private ProvisionRequest BuildRequest() => new(
        _connection, "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
        TestManifests.WithHealthPort(),
        new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_URL"] = "http://rendezvous:8080" },
        null, null);

    [Test]
    public async Task ProvisionAsync_uploads_compose_token_and_bootstrap_then_runs_it()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        Assert.That(result.ServerId, Is.EqualTo("deploy@203.0.113.5:2222"));
        Assert.That(result.PublicAddress, Is.EqualTo("203.0.113.5"));
        Assert.That(_ssh.UploadCalls.Select(u => u.RemotePath), Is.EquivalentTo(new[]
        {
            "docker-compose.yml", "/tmp/mp-host-ghcr-token", "/tmp/mp-host-bootstrap.sh",
        }));
        Assert.That(_ssh.UploadCalls.Single(u => u.RemotePath == "docker-compose.yml").Content, Does.Contain("ghcr.io/mindattic/game:v1"));
        Assert.That(_ssh.UploadCalls.Single(u => u.RemotePath == "/tmp/mp-host-ghcr-token").Content, Is.EqualTo("ghp_token"));
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("mp-host-bootstrap.sh"));
    }

    [Test]
    public void ProvisionAsync_throws_when_the_bootstrap_script_fails()
    {
        _ssh.Responses.Add(("bootstrap.sh", new SshCommandResult(1, "", "docker: command not found")));
        Assert.ThrowsAsync<InvalidOperationException>(() => _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task OpenFirewallAsync_allows_every_non_health_port_and_skips_the_health_port()
    {
        await _provisioner.OpenFirewallAsync("deploy@203.0.113.5:2222", BuildRequest(), _log, CancellationToken.None);

        Assert.That(_ssh.RunCalls, Has.Count.EqualTo(1));
        Assert.That(_ssh.RunCalls[0].Command, Does.Contain("ufw allow 7777/udp"));
        Assert.That(_ssh.RunCalls.Any(c => c.Command.Contains("8080")), Is.False);
    }

    [Test]
    public async Task OpenFirewallAsync_warns_but_does_not_throw_when_the_probe_fails()
    {
        _ssh.Responses.Add(("ufw", new SshCommandResult(1, "", "no ufw")));
        await _provisioner.OpenFirewallAsync("x", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_log.Lines.Any(l => l.StartsWith("warn:", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task WaitForHealthyAsync_returns_true_on_a_200_and_false_when_never_healthy()
    {
        _ssh.Responses.Add(("curl", new SshCommandResult(0, "200", "")));
        Assert.That(await _provisioner.WaitForHealthyAsync("x", BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None), Is.True);

        var unhealthySsh = new FakeSshCommandRunner();
        unhealthySsh.Responses.Add(("curl", new SshCommandResult(0, "503", "")));
        var unhealthyProvisioner = new GenericVpsProvisioner(unhealthySsh);
        Assert.That(await unhealthyProvisioner.WaitForHealthyAsync("x", BuildRequest(), TimeSpan.Zero, _log, CancellationToken.None), Is.False);
    }

    [Test]
    public async Task WaitForHealthyAsync_skips_the_probe_when_the_manifest_has_no_health_port()
    {
        var request = BuildRequest() with { Manifest = TestManifests.WithoutHealthPort() };
        Assert.That(await _provisioner.WaitForHealthyAsync("x", request, TimeSpan.Zero, _log, CancellationToken.None), Is.True);
        Assert.That(_ssh.RunCalls, Is.Empty);
    }

    [Test]
    public async Task GetStatusAsync_reports_Running_when_the_game_service_is_listed()
    {
        _ssh.Responses.Add(("ps --status running", new SshCommandResult(0, "game\n", "")));
        var status = await _provisioner.GetStatusAsync("deploy@203.0.113.5:2222", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Running));
    }

    [Test]
    public async Task GetStatusAsync_reports_Stopped_when_the_game_service_is_not_listed()
    {
        _ssh.Responses.Add(("ps --status running", new SshCommandResult(0, "", "")));
        var status = await _provisioner.GetStatusAsync("x", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Stopped));
    }

    [Test]
    public async Task GetLogsAsync_returns_compose_logs_output()
    {
        _ssh.Responses.Add(("logs --no-color", new SshCommandResult(0, "[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ\n", "")));
        string logs = await _provisioner.GetLogsAsync("x", 100, _connection, _log, CancellationToken.None);
        Assert.That(logs, Does.Contain("Join code: ABC-DEF-GHJ"));
    }

    [Test]
    public async Task UpdateAsync_re_uploads_compose_and_pulls_then_restarts()
    {
        await _provisioner.UpdateAsync("x", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_ssh.UploadCalls.Single().RemotePath, Is.EqualTo("docker-compose.yml"));
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("pull").And.Contain("up -d"));
    }

    [Test]
    public async Task DestroyAsync_runs_compose_down_with_volumes()
    {
        await _provisioner.DestroyAsync("x", _connection, _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("down -v"));
    }
}
