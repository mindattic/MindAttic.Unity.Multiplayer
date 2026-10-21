using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>Real <see cref="IFlyCli"/>: shells to the <c>flyctl</c> CLI, which must already be installed and authenticated (<c>flyctl auth token</c>) on the machine running mp-host.</summary>
public sealed class ProcessFlyCli : IFlyCli
{
    private readonly IProcessRunner _processes;

    public ProcessFlyCli(IProcessRunner processes)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    /// <inheritdoc />
    public async Task<string> GetLogsAsync(string appName, int tailLines, CancellationToken ct)
    {
        var result = await _processes.RunAsync("flyctl", new[] { "logs", "--app", appName, "--no-tail" }, null, null, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"flyctl logs --app {appName} failed (exit {result.ExitCode}): {result.StandardError}");
        }

        var lines = result.StandardOutput.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Length > tailLines ? lines[^tailLines..] : lines);
    }
}
