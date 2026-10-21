using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Packaging;

/// <summary>
/// Dry-run <see cref="IServerPackageBuilder"/>: an already-built <see cref="ServerPackageBuildRequest.PackageDirectory"/>
/// is just read (a harmless disk read, same as the real builder -- nothing about it is a side effect
/// a dry run needs to suppress), but a <see cref="ServerPackageBuildRequest.UnityExecutablePath"/> is
/// never actually launched: Unity batch mode is a real, possibly minutes-long external process with
/// its own side effects (a Library/ cache, temp output on disk), exactly what <c>--dry-run</c>
/// promises not to do. Prints the command it would have run and returns a synthesized package (no
/// directory on disk) so the rest of the pipeline still has something to narrate against, the same
/// way <see cref="CloudVps.DryRunCloudVpsApiClient"/>'s synthesized server does.
/// </summary>
public sealed class DryRunServerPackageBuilder : IServerPackageBuilder
{
    private const string BatchMethod = "Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine";
    private const string SimulatedDirectory = "(dry run: no package built)";

    /// <inheritdoc />
    public Task<BuiltServerPackage> BuildAsync(ServerPackageBuildRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(log);

        if (!string.IsNullOrWhiteSpace(request.PackageDirectory))
        {
            log.Info($"Using already-built server package at '{request.PackageDirectory}'.");
            return Task.FromResult(new BuiltServerPackage(request.PackageDirectory, ServerPackageManifest.Load(request.PackageDirectory)));
        }

        if (string.IsNullOrWhiteSpace(request.UnityExecutablePath) || string.IsNullOrWhiteSpace(request.UnityProjectPath))
        {
            throw new ArgumentException(
                "Either --package <built package dir>, or both --unity <Unity executable> and --unity-project <project dir>, is required.");
        }

        log.DryRun(
            $"{request.UnityExecutablePath} -batchmode -quit -nographics -projectPath {request.UnityProjectPath} "
            + $"-executeMethod {BatchMethod} -serverProfile {request.ProfileId} -serverOutput <temp dir>");
        return Task.FromResult(new BuiltServerPackage(SimulatedDirectory, SyntheticManifest(request.ProfileId)));
    }

    private static ServerPackageManifest SyntheticManifest(string profileId) => new(
        Format: 1,
        Profile: profileId,
        Executable: "Game.x86_64",
        Start: "./start.sh",
        Config: "server.json",
        Transport: "udp",
        Ports: new List<ManifestPort> { new("game", 7777, "udp", null) },
        Logs: "Logs",
        Saves: "Saves",
        StopSignal: "SIGTERM",
        StopGraceSeconds: 10);
}
