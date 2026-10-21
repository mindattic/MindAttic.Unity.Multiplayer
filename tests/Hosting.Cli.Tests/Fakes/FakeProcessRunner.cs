using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every process <see cref="DockerCliImageTool"/>/<see cref="ProcessSshCommandRunner"/> would run, with no real process started.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public sealed record Call(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory, string? StandardInput);

    public List<Call> Calls { get; } = new();
    public ProcessOutcome Outcome { get; set; } = new(0, string.Empty, string.Empty);

    public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, string? standardInput, CancellationToken ct)
    {
        Calls.Add(new Call(fileName, arguments, workingDirectory, standardInput));
        return Task.FromResult(Outcome);
    }
}
