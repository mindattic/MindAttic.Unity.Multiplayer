using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Deploy;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class JoinCodeWatcherTests
{
    [Test]
    public async Task Returns_the_code_from_the_exact_published_log_line()
    {
        var provisioner = new FakeProvisioner { Logs = "some noise\n[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ\nmore noise" };
        string? code = await JoinCodeWatcher.WaitForJoinCodeAsync(
            provisioner, "server-1", ProviderConnection.Named("x"), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), new RecordingDeployLog(), CancellationToken.None);
        Assert.That(code, Is.EqualTo("ABC-DEF-GHJ"));
    }

    [Test]
    public async Task Returns_the_most_recent_code_when_the_server_re_registered()
    {
        var provisioner = new FakeProvisioner
        {
            Logs = "[MindAttic.Multiplayer] Join code: FIRST-ONE-XXX\n[MindAttic.Multiplayer] Join code: SECOND-TWO-YYY",
        };
        string? code = await JoinCodeWatcher.WaitForJoinCodeAsync(
            provisioner, "server-1", ProviderConnection.Named("x"), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), new RecordingDeployLog(), CancellationToken.None);
        Assert.That(code, Is.EqualTo("SECOND-TWO-YYY"));
    }

    [Test]
    public async Task Returns_null_and_warns_when_the_timeout_elapses_with_no_code()
    {
        var provisioner = new FakeProvisioner { Logs = "server starting up, nothing published yet" };
        var log = new RecordingDeployLog();
        string? code = await JoinCodeWatcher.WaitForJoinCodeAsync(
            provisioner, "server-1", ProviderConnection.Named("x"), TimeSpan.Zero, TimeSpan.FromMilliseconds(10), log, CancellationToken.None);
        Assert.That(code, Is.Null);
        Assert.That(log.Lines.Any(l => l.StartsWith("warn:", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task Polls_again_once_the_first_check_finds_nothing()
    {
        var provisioner = new FakeProvisioner();
        provisioner.LogsSequence.Enqueue("nothing yet");
        provisioner.LogsSequence.Enqueue("[MindAttic.Multiplayer] Join code: LATER-CODE-ZZZ");
        string? code = await JoinCodeWatcher.WaitForJoinCodeAsync(
            provisioner, "server-1", ProviderConnection.Named("x"), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5), new RecordingDeployLog(), CancellationToken.None);
        Assert.That(code, Is.EqualTo("LATER-CODE-ZZZ"));
        Assert.That(provisioner.LogCallCount, Is.EqualTo(2));
    }
}
