using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>Dry-run <see cref="IFlyCli"/>: prints the <c>flyctl</c> call it would have made and returns a synthesized log line. No process is started.</summary>
public sealed class DryRunFlyCli : IFlyCli
{
    private readonly IDeployLog _log;

    public DryRunFlyCli(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<string> GetLogsAsync(string appName, int tailLines, CancellationToken ct)
    {
        _log.DryRun($"flyctl logs --app {appName} --no-tail (tail {tailLines})");
        return Task.FromResult("[MindAttic.Multiplayer] Join code: SIMU-LATE-DRYR\n");
    }
}
