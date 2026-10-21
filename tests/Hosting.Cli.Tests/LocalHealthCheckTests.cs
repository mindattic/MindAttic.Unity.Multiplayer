using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Local;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class LocalHealthCheckTests
{
    [Test]
    public async Task WaitForHealthyAsync_returns_true_immediately_when_the_manifest_has_no_health_port()
    {
        var log = new RecordingDeployLog();
        using var http = new HttpClient();
        bool healthy = await LocalHealthCheck.WaitForHealthyAsync(http, TestManifests.WithoutHealthPort(), TimeSpan.FromMilliseconds(1), log, CancellationToken.None);
        Assert.That(healthy, Is.True);
    }

    [Test]
    public async Task WaitForHealthyAsync_times_out_when_nothing_answers()
    {
        var log = new RecordingDeployLog();
        using var http = new HttpClient();
        bool healthy = await LocalHealthCheck.WaitForHealthyAsync(http, TestManifests.WithHealthPort(), TimeSpan.Zero, log, CancellationToken.None);
        Assert.That(healthy, Is.False);
    }
}
