using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Docker;

/// <summary>Real <see cref="IContainerImageTool"/>: shells to the <c>docker</c> CLI.</summary>
public sealed class DockerCliImageTool : IContainerImageTool
{
    private readonly IProcessRunner _processes;

    public DockerCliImageTool(IProcessRunner processes)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    /// <inheritdoc />
    public async Task LoginAsync(string registry, string username, string token, IDeployLog log, CancellationToken ct)
    {
        log.Info($"docker login {registry} as {username}");
        var result = await _processes.RunAsync("docker",
            new[] { "login", registry, "--username", username, "--password-stdin" }, null, token, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"docker login {registry} failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    /// <inheritdoc />
    public async Task BuildAsync(string contextDirectory, string imageReference, IDeployLog log, CancellationToken ct)
    {
        log.Info($"docker build -t {imageReference} {contextDirectory}");
        var result = await _processes.RunAsync("docker",
            new[] { "build", "-t", imageReference, contextDirectory }, contextDirectory, null, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"docker build failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    /// <inheritdoc />
    public async Task PushAsync(string imageReference, IDeployLog log, CancellationToken ct)
    {
        log.Info($"docker push {imageReference}");
        var result = await _processes.RunAsync("docker", new[] { "push", imageReference }, null, null, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"docker push failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }
}
