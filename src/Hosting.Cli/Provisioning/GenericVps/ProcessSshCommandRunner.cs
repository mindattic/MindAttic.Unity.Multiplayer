using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

/// <summary>Real <see cref="ISshCommandRunner"/>: shells to the system <c>ssh</c> binary (upload via an SSH heredoc, so no separate <c>scp</c> dependency).</summary>
public sealed class ProcessSshCommandRunner : ISshCommandRunner
{
    private readonly IProcessRunner _processes;

    public ProcessSshCommandRunner(IProcessRunner processes)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    /// <inheritdoc />
    public async Task<SshCommandResult> RunAsync(SshTarget target, string command, CancellationToken ct)
    {
        using SshAskPass? askPass = SshAskPass.CreateIfNeeded(target.Passphrase);
        var args = BuildArgs(target, askPass).Append($"{target.User}@{target.Host}").Append(command).ToArray();
        var outcome = await _processes.RunAsync("ssh", args, null, null, ct, askPass?.EnvironmentVariables);
        return new SshCommandResult(outcome.ExitCode, outcome.StandardOutput, outcome.StandardError);
    }

    /// <inheritdoc />
    public async Task UploadTextAsync(SshTarget target, string remotePath, string content, CancellationToken ct)
    {
        using SshAskPass? askPass = SshAskPass.CreateIfNeeded(target.Passphrase);

        // `cat > remotePath` fed over stdin avoids a second (scp) binary and keeps the content out
        // of the command line / process list.
        string command = $"cat > {ShellQuote(remotePath)}";
        var args = BuildArgs(target, askPass).Append($"{target.User}@{target.Host}").Append(command).ToArray();
        var outcome = await _processes.RunAsync("ssh", args, null, content, ct, askPass?.EnvironmentVariables);
        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException($"Uploading '{remotePath}' to {target.Host} failed (exit {outcome.ExitCode}): {outcome.StandardError}");
        }
    }

    private static IEnumerable<string> BuildArgs(SshTarget target, SshAskPass? askPass)
    {
        yield return "-p";
        yield return target.Port.ToString();
        yield return "-i";
        yield return target.PrivateKeyPath;

        if (askPass is null)
        {
            yield return "-o";
            yield return "BatchMode=yes";
        }
        else
        {
            // BatchMode=yes (above) disables OpenSSH's passphrase prompt entirely -- not just the
            // interactive echo, the underlying retry loop never runs -- so a passphrase-protected
            // key needs it left off (UM-T43). Everything else that could still block on a human
            // (a remote account password, keyboard-interactive) stays switched off so a headless
            // run can't hang on a prompt that will never be answered; only the key's own passphrase
            // prompt is left open, and SshAskPass answers that one via SSH_ASKPASS.
            yield return "-o";
            yield return "PreferredAuthentications=publickey";
            yield return "-o";
            yield return "PasswordAuthentication=no";
            yield return "-o";
            yield return "KbdInteractiveAuthentication=no";
        }

        yield return "-o";
        yield return "StrictHostKeyChecking=accept-new";
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
