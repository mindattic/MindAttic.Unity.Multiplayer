using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class RunPodProvisionerTests
{
    private FakeRunPodApiClient _api = null!;
    private RunPodProvisioner _provisioner = null!;
    private RecordingDeployLog _log = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeRunPodApiClient();
        _provisioner = new RunPodProvisioner(_api);
        _log = new RecordingDeployLog();
    }

    private ProvisionRequest BuildRequest() => new(
        ProviderConnection.Named("ws-server"), "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
        TestManifests.WithoutHealthPort(),
        new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_URL"] = "http://rendezvous.example" },
        null, null);

    [Test]
    public async Task ProvisionAsync_creates_a_pod_with_the_declared_ports_and_image()
    {
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);

        Assert.That(_api.CreateCalls.Single().ImageName, Is.EqualTo("ghcr.io/mindattic/game:v1"));
        Assert.That(_api.CreateCalls.Single().TcpPorts, Is.EqualTo(new[] { 7777 }));
        Assert.That(result.ServerId, Is.EqualTo("pod-1"));
        Assert.That(result.PublicAddress, Is.EqualTo("198.51.100.1"));
        Assert.That(result.PortMappings[7777], Is.EqualTo(37777));
    }

    [Test]
    public async Task ProvisionAsync_falls_back_to_the_container_port_when_RunPod_has_not_reported_a_mapping_yet()
    {
        _api.PodToCreate = new RunPodPod("pod-2", "RUNNING", null, null, new List<RunPodPortMapping>());
        var result = await _provisioner.ProvisionAsync(BuildRequest(), _log, CancellationToken.None);
        Assert.That(result.PortMappings[7777], Is.EqualTo(7777));
    }

    [Test]
    public async Task OpenFirewallAsync_is_a_documented_no_op()
    {
        await _provisioner.OpenFirewallAsync("pod-1", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_api.CreateCalls, Is.Empty);
        Assert.That(_api.GetCalls, Is.Empty);
    }

    [Test]
    public async Task WaitForHealthyAsync_polls_until_RUNNING()
    {
        _api.GetSequence = new Queue<RunPodPod>(new[]
        {
            new RunPodPod("pod-1", "RUNNING", null, null, new List<RunPodPortMapping>()),
            new RunPodPod("pod-1", "RUNNING", "RUNNING", "198.51.100.1", new List<RunPodPortMapping>()),
        });
        bool healthy = await _provisioner.WaitForHealthyAsync("pod-1", BuildRequest(), TimeSpan.FromSeconds(10), _log, CancellationToken.None);
        Assert.That(healthy, Is.True);
        Assert.That(_api.GetCalls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task WaitForHealthyAsync_gives_up_after_the_timeout()
    {
        _api.PodToCreate = new RunPodPod("pod-1", "RUNNING", null, null, new List<RunPodPortMapping>());
        bool healthy = await _provisioner.WaitForHealthyAsync("pod-1", BuildRequest(), TimeSpan.Zero, _log, CancellationToken.None);
        Assert.That(healthy, Is.False);
    }

    [TestCase("RUNNING", ServerState.Running)]
    [TestCase("EXITED", ServerState.Gone)]
    [TestCase("TERMINATED", ServerState.Gone)]
    [TestCase(null, ServerState.Provisioning)]
    [TestCase("WEIRD", ServerState.Unknown)]
    public async Task GetStatusAsync_maps_RunPod_runtime_status(string? runtimeStatus, ServerState expected)
    {
        _api.PodToCreate = new RunPodPod("pod-1", "RUNNING", runtimeStatus, "1.2.3.4", new List<RunPodPortMapping>());
        var status = await _provisioner.GetStatusAsync("pod-1", ProviderConnection.Named("ws-server"), _log, CancellationToken.None);
        Assert.That(status.State, Is.EqualTo(expected));
    }

    [Test]
    public async Task GetLogsAsync_delegates_straight_to_the_api_client()
    {
        _api.LogsToReturn = "[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ";
        string logs = await _provisioner.GetLogsAsync("pod-1", 50, ProviderConnection.Named("ws-server"), _log, CancellationToken.None);
        Assert.That(logs, Does.Contain("ABC-DEF-GHJ"));
    }

    [Test]
    public async Task UpdateAsync_terminates_the_old_pod_then_provisions_a_new_one()
    {
        await _provisioner.UpdateAsync("pod-1", BuildRequest(), _log, CancellationToken.None);
        Assert.That(_api.TerminateCalls, Is.EqualTo(new[] { "pod-1" }));
        Assert.That(_api.CreateCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DestroyAsync_terminates_the_pod()
    {
        await _provisioner.DestroyAsync("pod-1", ProviderConnection.Named("ws-server"), _log, CancellationToken.None);
        Assert.That(_api.TerminateCalls, Is.EqualTo(new[] { "pod-1" }));
    }
}
