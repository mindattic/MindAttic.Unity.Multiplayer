using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>Dry-run <see cref="ILocalComposeRunner"/>: prints the docker compose calls it would have made. No file is written and no process is started.</summary>
public sealed class DryRunLocalComposeRunner : ILocalComposeRunner
{
    private readonly IDeployLog _log;

    public DryRunLocalComposeRunner(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task UpAsync(string workingDirectory, string composeFileName, string composeYaml, IDeployLog log, CancellationToken ct)
    {
        _log.DryRun($"write {Path.Combine(workingDirectory, composeFileName)}");
        _log.DryRun($"docker compose -f {composeFileName} up -d --build (in {workingDirectory})");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DownAsync(string workingDirectory, string composeFileName, IDeployLog log, CancellationToken ct)
    {
        _log.DryRun($"docker compose -f {composeFileName} down -v (in {workingDirectory})");
        return Task.CompletedTask;
    }
}
