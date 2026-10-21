namespace Unity.Multiplayer.Hosting.Cli.Processes;

/// <summary>What running an external process (docker, ssh, scp, Unity's editor) produced.</summary>
public sealed record ProcessOutcome(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>True when <see cref="ExitCode"/> is 0.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs an external executable and waits for it, capturing both streams. The only seam the real
/// Docker CLI, SSH and Unity batch-mode launchers go through, so every one of them is mockable in
/// NUnit without a real docker/ssh/Unity install.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> in <paramref name="workingDirectory"/>
    /// (process default when null), optionally feeding <paramref name="standardInput"/> to its stdin.
    /// </summary>
    Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken ct);
}
