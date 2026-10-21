using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Docker;

/// <summary>Step 2 of the pipeline: build the server package's Docker image and push it to GHCR.</summary>
public interface IContainerImageTool
{
    /// <summary>Logs into <paramref name="registry"/> (e.g. <c>ghcr.io</c>) with a token over stdin -- the token is never put on the command line.</summary>
    Task LoginAsync(string registry, string username, string token, IDeployLog log, CancellationToken ct);

    /// <summary>Builds the image at <paramref name="contextDirectory"/> (the built server package, which carries its own Dockerfile) and tags it <paramref name="imageReference"/>.</summary>
    Task BuildAsync(string contextDirectory, string imageReference, IDeployLog log, CancellationToken ct);

    /// <summary>Pushes an already-built, already-tagged image.</summary>
    Task PushAsync(string imageReference, IDeployLog log, CancellationToken ct);
}
