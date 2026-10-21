using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

/// <summary>Dry-run <see cref="ISshCommandRunner"/>: prints what it would have run/uploaded and nothing else.</summary>
public sealed class DryRunSshCommandRunner : ISshCommandRunner
{
    private readonly IDeployLog _log;

    public DryRunSshCommandRunner(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<SshCommandResult> RunAsync(SshTarget target, string command, CancellationToken ct)
    {
        _log.DryRun($"ssh {target.User}@{target.Host}:{target.Port} '{command}'");
        return Task.FromResult(new SshCommandResult(0, string.Empty, string.Empty));
    }

    /// <inheritdoc />
    public Task UploadTextAsync(SshTarget target, string remotePath, string content, CancellationToken ct)
    {
        _log.DryRun($"upload {content.Length} bytes to {target.User}@{target.Host}:{remotePath}");
        return Task.CompletedTask;
    }
}
