using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Deploy;
using Unity.Multiplayer.Hosting.Cli.Docker;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class DeployPipelineTests
{
    private sealed class StubPackageBuilder : IServerPackageBuilder
    {
        public ServerPackageBuildRequest? Received;
        public Task<BuiltServerPackage> BuildAsync(ServerPackageBuildRequest request, IDeployLog log, CancellationToken ct)
        {
            Received = request;
            return Task.FromResult(new BuiltServerPackage("/built/package", TestManifests.WithHealthPort()));
        }
    }

    private sealed class StubImageTool : IContainerImageTool
    {
        public readonly List<string> Calls = new();
        public Task LoginAsync(string registry, string username, string token, IDeployLog log, CancellationToken ct) { Calls.Add($"login:{registry}"); return Task.CompletedTask; }
        public Task BuildAsync(string contextDirectory, string imageReference, IDeployLog log, CancellationToken ct) { Calls.Add($"build:{imageReference}"); return Task.CompletedTask; }
        public Task PushAsync(string imageReference, IDeployLog log, CancellationToken ct) { Calls.Add($"push:{imageReference}"); return Task.CompletedTask; }
    }

    [Test]
    public async Task RunAsync_executes_every_step_in_order_and_returns_the_join_code()
    {
        var packageBuilder = new StubPackageBuilder();
        var imageTool = new StubImageTool();
        var provisioner = new FakeProvisioner { Logs = "[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ" };
        var pipeline = new DeployPipeline(packageBuilder, imageTool, provisioner);
        var log = new RecordingDeployLog();

        var outcome = await pipeline.RunAsync(
            new ServerPackageBuildRequest("GenericVps", "/built/package", null, null),
            "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
            new ProviderConnection("prod-box", new Dictionary<string, string>(), new Dictionary<string, string>()),
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_URL"] = "http://rendezvous" },
            null, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
            log, CancellationToken.None);

        Assert.That(imageTool.Calls, Is.EqualTo(new[] { "login:ghcr.io", "build:ghcr.io/mindattic/game:v1", "push:ghcr.io/mindattic/game:v1" }));
        Assert.That(provisioner.ProvisionCallCount, Is.EqualTo(1));
        Assert.That(provisioner.OpenFirewallCallCount, Is.EqualTo(1));
        Assert.That(outcome.Healthy, Is.True);
        Assert.That(outcome.JoinCode, Is.EqualTo("ABC-DEF-GHJ"));
        Assert.That(outcome.ServerId, Is.EqualTo("server-1"));
    }

    [Test]
    public async Task RunAsync_still_returns_a_result_when_no_join_code_appears_in_time()
    {
        var pipeline = new DeployPipeline(new StubPackageBuilder(), new StubImageTool(), new FakeProvisioner { Logs = "starting up" });
        var outcome = await pipeline.RunAsync(
            new ServerPackageBuildRequest("GenericVps", "/built/package", null, null),
            "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
            ProviderConnection.Named("prod-box"), new Dictionary<string, string>(), null, null,
            TimeSpan.FromSeconds(5), TimeSpan.Zero, new RecordingDeployLog(), CancellationToken.None);

        Assert.That(outcome.JoinCode, Is.Null);
    }

    [Test]
    public async Task RunAsync_reports_unhealthy_without_throwing()
    {
        var pipeline = new DeployPipeline(new StubPackageBuilder(), new StubImageTool(), new FakeProvisioner { Healthy = false });
        var outcome = await pipeline.RunAsync(
            new ServerPackageBuildRequest("GenericVps", "/built/package", null, null),
            "ghcr.io/mindattic/game:v1", "ghcr.io", "octocat", "ghp_token",
            ProviderConnection.Named("prod-box"), new Dictionary<string, string>(), null, null,
            TimeSpan.Zero, TimeSpan.Zero, new RecordingDeployLog(), CancellationToken.None);

        Assert.That(outcome.Healthy, Is.False);
    }
}
