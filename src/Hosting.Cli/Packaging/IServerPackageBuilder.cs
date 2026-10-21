using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Packaging;

/// <summary>
/// What the deploy pipeline needs to build (or locate) a server package before it docker-builds it.
/// <para>The optional tail (UM-T58) is what <c>mp-host game</c> fills in so a game needs no build script of its
/// own: <paramref name="ScenePath"/> is the scene Unity opens before building (passed as <c>-serverScene</c>, so the
/// package's <c>server.json</c> comes from that scene's NetBootstrap), <paramref name="OutputDirectory"/> keeps the
/// package in the project (<c>Builds/ServerPackage/&lt;profile&gt;</c>) instead of a temp folder,
/// <paramref name="LogFile"/> is Unity's <c>-logFile</c>, and <paramref name="Development"/> a development build.</para>
/// </summary>
public sealed record ServerPackageBuildRequest(
    string ProfileId,
    string? PackageDirectory,
    string? UnityExecutablePath,
    string? UnityProjectPath,
    string? ScenePath = null,
    string? OutputDirectory = null,
    string? LogFile = null,
    bool Development = false);

/// <summary>A built server package: where it lives on disk, and its manifest.</summary>
public sealed record BuiltServerPackage(string Directory, ServerPackageManifest Manifest);

/// <summary>
/// Step 1 of the pipeline. Two ways in: an already-built package directory (the common path --
/// built once from Unity's "Build Server Package" menu, or by CI), or a Unity install to build it
/// from, via <see cref="Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine"/>'s
/// batch-mode contract.
/// </summary>
public interface IServerPackageBuilder
{
    /// <exception cref="ArgumentException">Neither a package directory nor a Unity executable was given.</exception>
    /// <exception cref="FileNotFoundException">The package directory has no manifest.</exception>
    Task<BuiltServerPackage> BuildAsync(ServerPackageBuildRequest request, IDeployLog log, CancellationToken ct);
}
