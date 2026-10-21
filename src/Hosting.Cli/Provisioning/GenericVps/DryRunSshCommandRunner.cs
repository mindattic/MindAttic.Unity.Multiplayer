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
        return Task.FromResult(new SshCommandResult(0, SimulatedOutputFor(command), string.Empty));
    }

    /// <summary>
    /// A plausible stand-in for the polling/one-shot checks callers run commands through this runner
    /// for: the health probe (<c>WaitForHealthyAsync</c>'s curl, matched by its <c>http_code</c>
    /// format string), the container log watch (<c>GetLogsAsync</c> / <c>JoinCodeWatcher</c>'s
    /// <c>docker compose ... logs</c>), and the running-services check (<c>GetStatusAsync</c> /
    /// <c>GetComposeStatusAsync</c>'s <c>docker compose ... ps --status running --services</c>, which
    /// looks for a line equal to <c>game</c>). Without this, every SSH-based provisioner's dry run
    /// either blocked for the real health/join-code timeout (minutes) polling an empty response and
    /// then warned "did not report healthy" / "no join code seen", or (status) immediately reported
    /// Stopped and exited 1 -- RunPod's <c>DryRunRunPodApiClient</c> already synthesizes the
    /// equivalent (a running pod, a join-code log line) for its own profile.
    /// </summary>
    private static string SimulatedOutputFor(string command)
    {
        if (command.Contains("http_code", StringComparison.Ordinal))
        {
            return "200";
        }

        if (command.Contains(" logs ", StringComparison.Ordinal))
        {
            return "[MindAttic.Multiplayer] Join code: SIMU-LATE-DRYR\n";
        }

        if (command.Contains(" ps --status running --services", StringComparison.Ordinal))
        {
            return "game\n";
        }

        return string.Empty;
    }

    /// <inheritdoc />
    public Task UploadTextAsync(SshTarget target, string remotePath, string content, CancellationToken ct)
    {
        _log.DryRun($"upload {content.Length} bytes to {target.User}@{target.Host}:{remotePath}");
        return Task.CompletedTask;
    }
}
