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
        var outcome = await _processes.RunAsync("ssh", BuildArgs(target).Append($"{target.User}@{target.Host}").Append(command).ToArray(), null, null, ct);
        return new SshCommandResult(outcome.ExitCode, outcome.StandardOutput, outcome.StandardError);
    }

    /// <inheritdoc />
    public async Task UploadTextAsync(SshTarget target, string remotePath, string content, CancellationToken ct)
    {
        // `cat > remotePath` fed over stdin avoids a second (scp) binary and keeps the content out
        // of the command line / process list.
        string command = $"cat > {ShellQuote(remotePath)}";
        var args = BuildArgs(target).Append($"{target.User}@{target.Host}").Append(command).ToArray();
        var outcome = await _processes.RunAsync("ssh", args, null, content, ct);
        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException($"Uploading '{remotePath}' to {target.Host} failed (exit {outcome.ExitCode}): {outcome.StandardError}");
        }
    }

    private static IEnumerable<string> BuildArgs(SshTarget target)
    {
        yield return "-p";
        yield return target.Port.ToString();
        yield return "-i";
        yield return target.PrivateKeyPath;
        yield return "-o";
        yield return "BatchMode=yes";
        yield return "-o";
        yield return "StrictHostKeyChecking=accept-new";
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
