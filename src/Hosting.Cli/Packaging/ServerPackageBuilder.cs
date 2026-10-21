using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Packaging;

/// <summary>
/// Default <see cref="IServerPackageBuilder"/>. When <see cref="ServerPackageBuildRequest.PackageDirectory"/>
/// is given, just reads its manifest (the package was already built). When
/// <see cref="ServerPackageBuildRequest.UnityExecutablePath"/> is given instead, builds it first by
/// shelling to Unity in batch mode with the exact arguments
/// <c>ServerPackageBuild.BuildFromCommandLine</c> reads: <c>-batchmode -quit -projectPath &lt;p&gt;
/// -executeMethod Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine
/// -serverProfile &lt;id&gt; -serverOutput &lt;dir&gt;</c>, then reads the manifest it produced.
/// </summary>
public sealed class ServerPackageBuilder : IServerPackageBuilder
{
    private const string BatchMethod = "Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine";

    private readonly IProcessRunner _processes;

    public ServerPackageBuilder(IProcessRunner processes)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    /// <inheritdoc />
    public async Task<BuiltServerPackage> BuildAsync(ServerPackageBuildRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(log);

        if (!string.IsNullOrWhiteSpace(request.PackageDirectory))
        {
            log.Info($"Using already-built server package at '{request.PackageDirectory}'.");
            return Load(request.PackageDirectory);
        }

        if (string.IsNullOrWhiteSpace(request.UnityExecutablePath) || string.IsNullOrWhiteSpace(request.UnityProjectPath))
        {
            throw new ArgumentException(
                "Either --package <built package dir>, or both --unity <Unity executable> and --unity-project <project dir>, is required.");
        }

        string output = Path.Combine(Path.GetTempPath(), "mp-host-package-" + Guid.NewGuid().ToString("N"));
        var arguments = new[]
        {
            "-batchmode", "-quit", "-nographics",
            "-projectPath", request.UnityProjectPath,
            "-executeMethod", BatchMethod,
            "-serverProfile", request.ProfileId,
            "-serverOutput", output,
        };

        log.Info($"Building the {request.ProfileId} server package with Unity ({request.UnityExecutablePath}) -> {output}");
        ProcessOutcome result = await _processes.RunAsync(request.UnityExecutablePath, arguments, request.UnityProjectPath, null, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unity batch-mode server package build failed (exit {result.ExitCode}).\n{result.StandardOutput}\n{result.StandardError}");
        }

        return Load(output);
    }

    private static BuiltServerPackage Load(string directory) => new(directory, ServerPackageManifest.Load(directory));
}
