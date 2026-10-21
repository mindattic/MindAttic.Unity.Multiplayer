using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Docker;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class DockerImageToolTests
{
    [Test]
    public async Task LoginAsync_feeds_the_token_over_stdin_never_the_argument_list()
    {
        var processes = new FakeProcessRunner();
        var tool = new DockerCliImageTool(processes);
        await tool.LoginAsync("ghcr.io", "octocat", "ghp_secret", new RecordingDeployLog(), CancellationToken.None);

        var call = processes.Calls.Single();
        Assert.That(call.FileName, Is.EqualTo("docker"));
        Assert.That(call.Arguments, Does.Not.Contain("ghp_secret"));
        Assert.That(call.StandardInput, Is.EqualTo("ghp_secret"));
    }

    [Test]
    public void LoginAsync_throws_on_a_non_zero_exit()
    {
        var processes = new FakeProcessRunner { Outcome = new ProcessOutcome(1, "", "denied") };
        var tool = new DockerCliImageTool(processes);
        Assert.ThrowsAsync<InvalidOperationException>(() => tool.LoginAsync("ghcr.io", "u", "t", new RecordingDeployLog(), CancellationToken.None));
    }

    [Test]
    public async Task BuildAsync_tags_the_image_from_the_package_directory()
    {
        var processes = new FakeProcessRunner();
        var tool = new DockerCliImageTool(processes);
        await tool.BuildAsync("/pkg", "ghcr.io/x/y:v1", new RecordingDeployLog(), CancellationToken.None);
        Assert.That(processes.Calls.Single().Arguments, Is.EqualTo(new[] { "build", "-t", "ghcr.io/x/y:v1", "/pkg" }));
    }

    [Test]
    public async Task PushAsync_pushes_the_given_reference()
    {
        var processes = new FakeProcessRunner();
        var tool = new DockerCliImageTool(processes);
        await tool.PushAsync("ghcr.io/x/y:v1", new RecordingDeployLog(), CancellationToken.None);
        Assert.That(processes.Calls.Single().Arguments, Is.EqualTo(new[] { "push", "ghcr.io/x/y:v1" }));
    }

    [Test]
    public async Task DryRunImageTool_runs_no_process_and_withholds_the_token()
    {
        var log = new RecordingDeployLog();
        var tool = new DryRunImageTool();
        await tool.LoginAsync("ghcr.io", "octocat", "ghp_secret", log, CancellationToken.None);
        await tool.BuildAsync("/pkg", "ghcr.io/x/y:v1", log, CancellationToken.None);
        await tool.PushAsync("ghcr.io/x/y:v1", log, CancellationToken.None);

        Assert.That(log.DryRunCalls, Has.Count.EqualTo(3));
        Assert.That(log.Lines.Any(l => l.Contains("ghp_secret")), Is.False);
    }
}
