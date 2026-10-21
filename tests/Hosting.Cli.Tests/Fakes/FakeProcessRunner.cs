using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every process <see cref="DockerCliImageTool"/>/<see cref="ProcessSshCommandRunner"/> would run, with no real process started.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public sealed record Call(
        string FileName,
        IReadOnlyList<string> Arguments,
        string? WorkingDirectory,
        string? StandardInput,
        IReadOnlyDictionary<string, string>? EnvironmentVariables);

    public List<Call> Calls { get; } = new();
    public ProcessOutcome Outcome { get; set; } = new(0, string.Empty, string.Empty);

    /// <summary>Fires synchronously once the call is recorded, before <see cref="RunAsync"/> returns -- lets a test observe state (e.g. a generated temp file) that only exists while the "process" is "running".</summary>
    public Action<Call>? OnRun { get; set; }

    public Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var call = new Call(fileName, arguments, workingDirectory, standardInput, environmentVariables);
        Calls.Add(call);
        OnRun?.Invoke(call);
        return Task.FromResult(Outcome);
    }
}
