using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Packaging;

/// <summary>
/// Default <see cref="IServerPackageBuilder"/>. When <see cref="ServerPackageBuildRequest.PackageDirectory"/>
/// is given, just reads its manifest (the package was already built). When
/// <see cref="ServerPackageBuildRequest.UnityExecutablePath"/> is given instead, builds it first by
/// shelling to Unity in batch mode with the arguments <see cref="UnityBatchCommand"/> assembles (the contract
/// <c>ServerPackageBuild.BuildFromCommandLine</c> reads: <c>-batchmode -quit -projectPath &lt;p&gt;
/// -executeMethod Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine
/// -serverProfile &lt;id&gt; -serverOutput &lt;dir&gt; [-serverScene &lt;scene&gt;]</c>), then reads the manifest it produced.
/// </summary>
public sealed class ServerPackageBuilder : IServerPackageBuilder
{
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

        string output = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Path.GetTempPath(), "mp-host-package-" + Guid.NewGuid().ToString("N"))
            : request.OutputDirectory;
        if (!string.IsNullOrWhiteSpace(request.LogFile))
        {
            string? logDirectory = Path.GetDirectoryName(Path.GetFullPath(request.LogFile));
            if (logDirectory is not null) Directory.CreateDirectory(logDirectory);
        }

        IReadOnlyList<string> arguments = UnityBatchCommand.Arguments(request, output);
        log.Info($"Building the {PackageProfiles.For(request.ProfileId)} server package with Unity ({request.UnityExecutablePath}) -> {output}"
            + (request.ScenePath is null ? string.Empty : $" from {request.ScenePath}")
            + (request.LogFile is null ? string.Empty : $"; this takes a few minutes, Unity's log: {request.LogFile}"));
        ProcessOutcome result = await _processes.RunAsync(request.UnityExecutablePath, arguments, request.UnityProjectPath, null, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unity batch-mode server package build failed (exit {result.ExitCode}).\n{result.StandardOutput}\n{result.StandardError}{LogTail(request.LogFile)}");
        }

        return Load(output);
    }

    /// <summary>The last lines of Unity's <c>-logFile</c>, where a batch build writes its errors (stdout stays empty then).</summary>
    private static string LogTail(string? logFile)
    {
        if (string.IsNullOrWhiteSpace(logFile) || !File.Exists(logFile)) return string.Empty;
        try
        {
            string[] lines = File.ReadAllLines(logFile);
            return $"\nLast lines of {logFile}:\n" + string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 40)));
        }
        catch (IOException)
        {
            return $"\nSee {logFile}.";
        }
    }

    private static BuiltServerPackage Load(string directory) => new(directory, ServerPackageManifest.Load(directory));
}
