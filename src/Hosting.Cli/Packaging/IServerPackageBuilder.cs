using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Packaging;

/// <summary>What the deploy pipeline needs to build (or locate) a server package before it docker-builds it.</summary>
public sealed record ServerPackageBuildRequest(
    string ProfileId,
    string? PackageDirectory,
    string? UnityExecutablePath,
    string? UnityProjectPath);

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
