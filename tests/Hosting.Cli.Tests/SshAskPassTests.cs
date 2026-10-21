using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class SshAskPassTests
{
    [TestCase(null)]
    [TestCase("")]
    public void CreateIfNeeded_returns_null_when_there_is_no_passphrase(string? passphrase)
    {
        Assert.That(SshAskPass.CreateIfNeeded(passphrase), Is.Null);
    }

    [Test]
    public void CreateIfNeeded_writes_an_executable_helper_script_and_Dispose_removes_it()
    {
        using SshAskPass? askPass = SshAskPass.CreateIfNeeded("hunter2");
        Assert.That(askPass, Is.Not.Null);

        string scriptPath = askPass!.EnvironmentVariables["SSH_ASKPASS"];
        Assert.That(File.Exists(scriptPath), Is.True);

        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(scriptPath);
            Assert.That(mode & UnixFileMode.UserExecute, Is.EqualTo(UnixFileMode.UserExecute),
                "the helper must be executable on POSIX since ssh execs it directly, with no shell wrapper.");
        }

        string content = File.ReadAllText(scriptPath);
        Assert.That(content, Does.Contain(SshAskPass.PassphraseEnvVar));
        Assert.That(content, Does.Not.Contain("hunter2"));

        askPass.Dispose();
        Assert.That(File.Exists(scriptPath), Is.False);
    }

    [Test]
    public void Dispose_is_idempotent_and_never_throws_if_the_file_is_already_gone()
    {
        SshAskPass? askPass = SshAskPass.CreateIfNeeded("hunter2");
        string scriptPath = askPass!.EnvironmentVariables["SSH_ASKPASS"];

        File.Delete(scriptPath);

        Assert.DoesNotThrow(() => askPass.Dispose());
        Assert.DoesNotThrow(() => askPass.Dispose());
    }

    [Test]
    public void EnvironmentVariables_carry_the_passphrase_itself_for_the_script_to_echo_back()
    {
        using SshAskPass? askPass = SshAskPass.CreateIfNeeded("hunter2");
        Assert.That(askPass!.EnvironmentVariables[SshAskPass.PassphraseEnvVar], Is.EqualTo("hunter2"));
        Assert.That(askPass.EnvironmentVariables["SSH_ASKPASS_REQUIRE"], Is.EqualTo("force"));
    }
}
