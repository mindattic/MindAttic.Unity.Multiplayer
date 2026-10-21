using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>Real <see cref="ILocalComposeRunner"/>: shells out to the local <c>docker</c> CLI.</summary>
public sealed class ProcessLocalComposeRunner : ILocalComposeRunner
{
    private readonly IProcessRunner _process;

    public ProcessLocalComposeRunner(IProcessRunner process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
    }

    /// <inheritdoc />
    public async Task UpAsync(string workingDirectory, string composeFileName, string composeYaml, IDeployLog log, CancellationToken ct)
    {
        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, composeFileName), composeYaml, ct);
        log.Info($"Building and starting the local compose stack in {workingDirectory}.");
        ProcessOutcome outcome = await _process.RunAsync("docker", new[] { "compose", "-f", composeFileName, "up", "-d", "--build" }, workingDirectory, null, ct);
        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException($"'docker compose up' failed (exit {outcome.ExitCode}): {outcome.StandardError}");
        }
    }

    /// <inheritdoc />
    public async Task DownAsync(string workingDirectory, string composeFileName, IDeployLog log, CancellationToken ct)
    {
        log.Info($"Stopping the local compose stack in {workingDirectory}.");
        ProcessOutcome outcome = await _process.RunAsync("docker", new[] { "compose", "-f", composeFileName, "down", "-v" }, workingDirectory, null, ct);
        if (!outcome.Succeeded)
        {
            log.Warn($"'docker compose down' reported an error (exit {outcome.ExitCode}): {outcome.StandardError}");
        }
    }
}
