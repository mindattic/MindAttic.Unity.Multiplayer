namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

/// <summary>What running a command over SSH produced.</summary>
public sealed record SshCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>True when <see cref="ExitCode"/> is 0.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// The only way <see cref="GenericVpsProvisioner"/> reaches a host: run a command, or upload a
/// text file. Real implementation shells to the system <c>ssh</c>/<c>scp</c> binaries; dry-run and
/// test doubles never open a socket.
/// </summary>
public interface ISshCommandRunner
{
    /// <summary>Runs <paramref name="command"/> on <paramref name="target"/> and waits for it.</summary>
    Task<SshCommandResult> RunAsync(SshTarget target, string command, CancellationToken ct);

    /// <summary>Writes <paramref name="content"/> to <paramref name="remotePath"/> on <paramref name="target"/>.</summary>
    Task UploadTextAsync(SshTarget target, string remotePath, string content, CancellationToken ct);
}
