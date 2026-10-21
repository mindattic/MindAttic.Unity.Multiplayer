namespace Unity.Multiplayer.Hosting.Cli.Packaging;

/// <summary>
/// The one place the Unity batch-mode command line for a server package build is assembled, shared by
/// <see cref="ServerPackageBuilder"/> (which runs it) and <see cref="DryRunServerPackageBuilder"/> (which prints it),
/// so a dry run narrates exactly what a real run executes. The arguments are the contract
/// <c>Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine</c> reads.
/// </summary>
public static class UnityBatchCommand
{
    /// <summary>The library's batch entry point; every game uses it, none needs its own.</summary>
    public const string BatchMethod = "Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine";

    /// <summary>The library's <c>-serverScene</c> argument (UM-T58): the scene opened before the build reads its NetBootstrap.</summary>
    public const string SceneArg = "-serverScene";

    /// <summary>The batch arguments for <paramref name="request"/>, writing the package to <paramref name="outputDirectory"/>.</summary>
    public static IReadOnlyList<string> Arguments(ServerPackageBuildRequest request, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new List<string>
        {
            "-batchmode", "-quit", "-nographics",
            "-projectPath", request.UnityProjectPath ?? string.Empty,
            "-executeMethod", BatchMethod,
            "-serverProfile", PackageProfiles.For(request.ProfileId),
            "-serverOutput", outputDirectory,
        };

        if (!string.IsNullOrWhiteSpace(request.ScenePath))
        {
            arguments.Add(SceneArg);
            arguments.Add(request.ScenePath);
        }

        if (!string.IsNullOrWhiteSpace(request.LogFile))
        {
            arguments.Add("-logFile");
            arguments.Add(request.LogFile);
        }

        if (request.Development)
        {
            arguments.Add("-serverDevelopment");
        }

        return arguments;
    }

    /// <summary><paramref name="arguments"/> as one printable line, quoting any argument with a space.</summary>
    public static string Format(string executable, IEnumerable<string> arguments) =>
        string.Join(' ', new[] { executable }.Concat(arguments).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}

/// <summary>
/// Which Unity-side hosting profile (the package layout <c>-serverProfile</c> selects) a deploy profile needs. Unity
/// knows three: <c>RunPod</c> (WebSocket on a mapped TCP port), <c>GameLift</c> (the raw managed-fleet build) and
/// <c>GenericVps</c>, the plain Docker package every other provider -- every VPS and cloud VM, Fly, and
/// <c>Local</c> -- runs as is.
/// </summary>
public static class PackageProfiles
{
    public const string GenericVps = "GenericVps";
    public const string RunPod = "RunPod";
    public const string GameLift = "GameLift";

    /// <summary>The package profile for deploy profile <paramref name="deployProfile"/> (case-insensitive).</summary>
    public static string For(string? deployProfile)
    {
        if (string.Equals(deployProfile, RunPod, StringComparison.OrdinalIgnoreCase)) return RunPod;
        if (string.Equals(deployProfile, GameLift, StringComparison.OrdinalIgnoreCase)) return GameLift;
        return GenericVps;
    }
}
