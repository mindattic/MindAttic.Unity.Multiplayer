using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Commands;
using Unity.Multiplayer.Hosting.Cli.Composition;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HostingComposerTests
{
    private string _dir = null!;
    private HostingCredentialStore _emptyStore = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mp-host-composer-" + Guid.NewGuid().ToString("N"));
        _emptyStore = new HostingCredentialStore(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void A_real_run_still_refuses_without_a_stored_ghcr_credential_or_ssh_target()
    {
        var composer = new HostingComposer(_emptyStore, dryRun: false);
        var log = new RecordingDeployLog();
        Assert.Throws<InvalidOperationException>(() => composer.RequireGhcrCreds(log));
        Assert.Throws<InvalidOperationException>(() => composer.BuildConnection(GenericVpsProvisioner.Id, "box", log));
    }

    [Test]
    public void A_dry_run_uses_labelled_placeholders_when_nothing_is_stored()
    {
        // UM-T49: --dry-run promises to print every call and touch nothing, so a missing vault
        // credential must not stop it; the stand-ins are labelled so nobody mistakes them for real.
        var composer = new HostingComposer(_emptyStore, dryRun: true);
        var log = new RecordingDeployLog();

        var ghcr = composer.RequireGhcrCreds(log);
        var connection = composer.BuildConnection(GenericVpsProvisioner.Id, "box", log);

        Assert.That(ghcr.Username, Does.StartWith(HostingComposer.PlaceholderPrefix));
        Assert.That(ghcr.Token, Does.StartWith(HostingComposer.PlaceholderPrefix));
        Assert.That(connection.ProviderOptions["sshHost"], Does.StartWith(HostingComposer.PlaceholderPrefix));
        Assert.That(log.Lines.Count(l => l.StartsWith("warn: Dry run:", StringComparison.Ordinal)), Is.EqualTo(2));
    }

    [Test]
    public void A_dry_run_prefers_stored_credentials_over_placeholders()
    {
        _emptyStore.SetGhcrCreds(new HostingCredentialStore.GhcrCreds("octocat", "ghp_abc"));
        var composer = new HostingComposer(_emptyStore, dryRun: true);
        var log = new RecordingDeployLog();

        Assert.That(composer.RequireGhcrCreds(log).Username, Is.EqualTo("octocat"));
        Assert.That(log.Lines, Has.None.StartsWith("warn:"));
    }

    [Test]
    public async Task Deploy_dry_run_completes_on_a_fresh_machine_with_an_empty_vault()
    {
        var log = new RecordingDeployLog();
        int exitCode = await DeployCommand.RunAsync(
            new[]
            {
                "deploy", "--profile", GenericVpsProvisioner.Id, "--name", "box", "--image", "owner/game",
                "--unity", "Unity.exe", "--unity-project", "C:/proj",
                "--rendezvous-url", "http://rv:8080", "--rendezvous-secret", "s3cr3t", "--public-address", "203.0.113.10",
                "--health-timeout", "1", "--joincode-timeout", "1", "--dry-run",
            },
            log, _emptyStore, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(log.DryRunCalls, Has.Some.Contains(HostingComposer.PlaceholderPrefix));
        Assert.That(Directory.Exists(_dir) ? Directory.GetFiles(_dir, "*", SearchOption.AllDirectories) : Array.Empty<string>(), Is.Empty,
            "a dry run must not write to the vault");
    }
}
