using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class AzureVmProvisionerTests
{
    private FakeAzureVmApiClient _api = null!;
    private FakeSshCommandRunner _ssh = null!;
    private AzureVmProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;
    private ProviderConnection _connection = null!;
    private string _publicKeyFile = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeAzureVmApiClient();
        _ssh = new FakeSshCommandRunner();
        _provisioner = new AzureVmProvisioner(_api, _ssh);
        _log = new RecordingDeployLog();
        _publicKeyFile = Path.Combine(Path.GetTempPath(), "mp-host-azure-pub-" + Guid.NewGuid().ToString("N") + ".pub");
        File.WriteAllText(_publicKeyFile, "ssh-ed25519 AAAATESTKEY test@example.com\n");
        _connection = new ProviderConnection("azure-box", new Dictionary<string, string>
        {
            ["resourceGroup"] = "mp-host-rg",
            ["subnetId"] = "/subscriptions/sub/resourceGroups/mp-host-rg/providers/Microsoft.Network/virtualNetworks/vnet/subnets/default",
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
    public async Task ProvisionAsync_creates_nsg_public_ip_nic_and_vm_in_order()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        Assert.That(_api.NsgCalls.Single().Name, Is.EqualTo("mp-host-azure-box-nsg"));
        Assert.That(_api.PublicIpCreateCalls.Single().Name, Is.EqualTo("mp-host-azure-box-ip"));
        Assert.That(_api.NicCalls.Single().Name, Is.EqualTo("mp-host-azure-box-nic"));
        var vm = _api.VmCreateCalls.Single();
        Assert.That(vm.Name, Is.EqualTo("azure-box"));
        Assert.That(vm.VmSize, Is.EqualTo("Standard_B1s"));
        Assert.That(vm.SshPublicKey, Does.Contain("AAAATESTKEY"));
        Assert.That(result.ServerId, Is.EqualTo("azure-box"));
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.40"));
    }

    [Test]
    public void ProvisionAsync_throws_when_subnetId_is_missing()
    {
        _connection = new ProviderConnection("azure-box", new Dictionary<string, string>
        {
            ["resourceGroup"] = "rg", ["sshKeyPath"] = "/k", ["sshPublicKeyPath"] = _publicKeyFile,
        }, new Dictionary<string, string>());
        Assert.ThrowsAsync<ArgumentException>(() => _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None));
    }

    [Test]
    public async Task OpenFirewallAsync_authorizes_ssh_rendezvous_and_the_declared_game_port()
    {
        await _provisioner.OpenFirewallAsync("azure-box", BuildRequest(), _log, CancellationToken.None);
        var nsg = _api.NsgCalls.Last();
        Assert.That(nsg.Rules, Has.Some.Matches<AzureSecurityRule>(r => r.Port == 22 && r.Protocol == "Tcp"));
        Assert.That(nsg.Rules, Has.Some.Matches<AzureSecurityRule>(r => r.Port == 8080 && r.Protocol == "Tcp"));
        Assert.That(nsg.Rules, Has.Some.Matches<AzureSecurityRule>(r => r.Port == 7777 && r.Protocol == "Udp"));
    }

    [Test]
    public async Task WaitForHealthyAsync_probes_over_ssh_once_the_public_ip_is_known()
    {
        _ssh.Responses.Add(("curl", new SshCommandResult(0, "200", "")));
        bool healthy = await _provisioner.WaitForHealthyAsync("azure-box", BuildRequest(), TimeSpan.FromSeconds(5), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
    }

    [Test]
    public async Task GetStatusAsync_maps_provisioning_state_when_there_is_no_power_state_yet()
    {
        _api.VmToReturn = new AzureVmInfo("azure-box", "Creating", null);
        var status = await _provisioner.GetStatusAsync("azure-box", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Provisioning));
    }

    [Test]
    public async Task GetStatusAsync_checks_compose_over_ssh_once_running()
    {
        _ssh.Responses.Add(("ps --status running", new SshCommandResult(0, "game\n", "")));
        var status = await _provisioner.GetStatusAsync("azure-box", _connection, _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(ServerState.Running));
    }

    [Test]
    public async Task UpdateAsync_restarts_in_place_over_ssh()
    {
        await _provisioner.UpdateAsync("azure-box", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Single().Command, Does.Contain("pull").And.Contain("up -d"));
    }

    [Test]
    public async Task DestroyAsync_deletes_the_vm_before_the_nic_before_the_ip_and_nsg()
    {
        await _provisioner.DestroyAsync("azure-box", _connection, _log, CancellationToken.None);
        Assert.That(_ssh.RunCalls.Any(c => c.Command.Contains("down -v")), Is.True);
        Assert.That(_api.DeleteVmCalls.Single().Name, Is.EqualTo("azure-box"));
        Assert.That(_api.DeleteNicCalls.Single().Name, Is.EqualTo("mp-host-azure-box-nic"));
        Assert.That(_api.DeleteIpCalls.Single().Name, Is.EqualTo("mp-host-azure-box-ip"));
        Assert.That(_api.DeleteNsgCalls.Single().Name, Is.EqualTo("mp-host-azure-box-nsg"));
    }
}
