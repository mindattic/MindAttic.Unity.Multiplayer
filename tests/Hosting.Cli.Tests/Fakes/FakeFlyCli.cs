using Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="FlyProvisioner"/> makes for logs, with no real <c>flyctl</c> process.</summary>
public sealed class FakeFlyCli : IFlyCli
{
    public List<string> LogCalls { get; } = new();
    public string LogsToReturn { get; set; } = string.Empty;

    public Task<string> GetLogsAsync(string appName, int tailLines, CancellationToken ct)
    {
        LogCalls.Add(appName);
        return Task.FromResult(LogsToReturn);
    }
}
