using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class RootCommandTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "mp-host-root-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public async Task RunAsync_reports_a_missing_ssh_public_key_file_as_a_clean_error_not_a_crash()
    {
        // RootCommand's HostingCredentialStore.Default reads a real on-disk vault path, which this
        // test cannot control, so it goes through CredentialsCommand directly with an isolated store
        // -- the behavior under test (IOException handling) lives in RootCommand's try/catch, so
        // exercise the same code path by catching what CredentialsCommand itself would throw and
        // feeding it through RootCommand's public entry point via a missing-file argument.
        var log = new RecordingDeployLog();
        string missingKeyPath = Path.Combine(_dir, "no-such-key.pub");

        int exitCode = await RootCommand.RunAsync(
            new[] { "credentials", "set-hetzner", "--token", "t", "--ssh-key", "/key", "--ssh-public-key", missingKeyPath },
            log, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(log.Lines, Has.Some.Matches<string>(l => l.StartsWith("error: ", StringComparison.Ordinal)));
    }
}
