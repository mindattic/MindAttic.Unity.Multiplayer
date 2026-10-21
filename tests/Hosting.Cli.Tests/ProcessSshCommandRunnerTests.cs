using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

/// <summary>
/// UM-T43: a passphrase-protected private key must actually reach ssh, via a one-shot
/// SSH_ASKPASS helper, since BatchMode=yes (the no-passphrase default) disables ssh's passphrase
/// prompt outright. No real sshd is involved -- <see cref="FakeProcessRunner"/> stands in for the
/// system <c>ssh</c> binary.
/// </summary>
[TestFixture]
public class ProcessSshCommandRunnerTests
{
    private static readonly SshTarget NoPassphrase = new("203.0.113.9", 22, "root", "/key", null);
    private static readonly SshTarget WithPassphrase = new("203.0.113.9", 22, "root", "/key", "hunter2");

    [Test]
    public async Task RunAsync_with_no_passphrase_keeps_BatchMode_and_feeds_no_askpass_env()
    {
        var processes = new FakeProcessRunner();
        var runner = new ProcessSshCommandRunner(processes);

        await runner.RunAsync(NoPassphrase, "echo hi", CancellationToken.None);

        var call = processes.Calls.Single();
        Assert.That(call.Arguments, Does.Contain("BatchMode=yes"));
        Assert.That(call.EnvironmentVariables, Is.Null);
    }

    [Test]
    public async Task RunAsync_with_a_passphrase_drops_BatchMode_and_forces_askpass()
    {
        var processes = new FakeProcessRunner();
        var runner = new ProcessSshCommandRunner(processes);

        await runner.RunAsync(WithPassphrase, "echo hi", CancellationToken.None);

        var call = processes.Calls.Single();
        Assert.That(call.Arguments, Does.Not.Contain("BatchMode=yes"));
        Assert.That(call.EnvironmentVariables, Is.Not.Null);
        Assert.That(call.EnvironmentVariables!["SSH_ASKPASS_REQUIRE"], Is.EqualTo("force"));
        Assert.That(call.EnvironmentVariables!, Does.ContainKey("SSH_ASKPASS"));

        // The passphrase itself never appears on the command line (visible to every other
        // process via ps/Task Manager) -- only in an env var scoped to this one ssh process.
        Assert.That(call.Arguments, Has.None.Contains("hunter2"));
        Assert.That(call.EnvironmentVariables![SshAskPass.PassphraseEnvVar], Is.EqualTo("hunter2"));
    }

    [Test]
    public async Task RunAsync_with_a_passphrase_still_blocks_other_interactive_prompts()
    {
        var processes = new FakeProcessRunner();
        var runner = new ProcessSshCommandRunner(processes);

        await runner.RunAsync(WithPassphrase, "echo hi", CancellationToken.None);

        var call = processes.Calls.Single();
        Assert.That(call.Arguments, Does.Contain("PasswordAuthentication=no"));
        Assert.That(call.Arguments, Does.Contain("KbdInteractiveAuthentication=no"));
    }

    [Test]
    public async Task RunAsync_with_a_passphrase_writes_a_helper_script_that_echoes_the_passphrase_var_and_deletes_it_afterward()
    {
        var processes = new FakeProcessRunner();
        var runner = new ProcessSshCommandRunner(processes);

        string? scriptPathDuringRun = null;
        string? scriptContentDuringRun = null;
        processes.OnRun = call =>
        {
            scriptPathDuringRun = call.EnvironmentVariables?["SSH_ASKPASS"];
            if (scriptPathDuringRun is not null && File.Exists(scriptPathDuringRun))
            {
                scriptContentDuringRun = File.ReadAllText(scriptPathDuringRun);
            }
        };

        await runner.RunAsync(WithPassphrase, "echo hi", CancellationToken.None);

        Assert.That(scriptPathDuringRun, Is.Not.Null, "SSH_ASKPASS should have pointed at a real helper script while ssh was \"running\".");
        Assert.That(scriptContentDuringRun, Is.Not.Null.And.Contains(SshAskPass.PassphraseEnvVar),
            "the helper script must reference the passphrase env var, not a hardcoded secret.");
        Assert.That(scriptContentDuringRun, Does.Not.Contain("hunter2"), "the passphrase itself must never be written into the script file.");

        Assert.That(File.Exists(scriptPathDuringRun!), Is.False, "the helper script must be deleted once the ssh call is done.");
    }

    [Test]
    public void UploadTextAsync_cleans_up_the_helper_script_even_when_the_upload_fails()
    {
        var processes = new FakeProcessRunner { Outcome = new ProcessOutcome(1, "", "permission denied") };
        var runner = new ProcessSshCommandRunner(processes);

        string? scriptPathDuringRun = null;
        processes.OnRun = call => scriptPathDuringRun = call.EnvironmentVariables?["SSH_ASKPASS"];

        Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.UploadTextAsync(WithPassphrase, "/remote/file", "content", CancellationToken.None));

        Assert.That(scriptPathDuringRun, Is.Not.Null);
        Assert.That(File.Exists(scriptPathDuringRun!), Is.False,
            "the helper script must be cleaned up on the failure path too, not just on success.");
    }

    [Test]
    public async Task UploadTextAsync_with_a_passphrase_also_forces_askpass()
    {
        var processes = new FakeProcessRunner();
        var runner = new ProcessSshCommandRunner(processes);

        await runner.UploadTextAsync(WithPassphrase, "/remote/file", "content", CancellationToken.None);

        var call = processes.Calls.Single();
        Assert.That(call.EnvironmentVariables, Is.Not.Null);
        Assert.That(call.EnvironmentVariables![SshAskPass.PassphraseEnvVar], Is.EqualTo("hunter2"));
    }
}
