using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Docker;

/// <summary>
/// Dry-run <see cref="IContainerImageTool"/>: prints every call it would have made (the ticket's
/// "dry-run mode prints every API call") and does nothing -- no docker process is started, no
/// token ever leaves the process, nothing is pushed anywhere.
/// </summary>
public sealed class DryRunImageTool : IContainerImageTool
{
    /// <inheritdoc />
    public Task LoginAsync(string registry, string username, string token, IDeployLog log, CancellationToken ct)
    {
        log.DryRun($"docker login {registry} --username {username} --password-stdin (token withheld)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task BuildAsync(string contextDirectory, string imageReference, IDeployLog log, CancellationToken ct)
    {
        log.DryRun($"docker build -t {imageReference} {contextDirectory}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PushAsync(string imageReference, IDeployLog log, CancellationToken ct)
    {
        log.DryRun($"docker push {imageReference}");
        return Task.CompletedTask;
    }
}
