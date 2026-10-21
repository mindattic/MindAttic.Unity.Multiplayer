using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>A trivial <see cref="IProvisioner"/> whose every answer is pre-set, for pipeline-level and watcher tests.</summary>
public sealed class FakeProvisioner : IProvisioner
{
    public string ProviderId => "Fake";
    public string DisplayName => "Fake";

    public ProvisionResult ProvisionResult { get; set; } = new("server-1", "198.51.100.1", new Dictionary<int, int>());
    public bool Healthy { get; set; } = true;
    public Queue<string> LogsSequence { get; set; } = new();
    public string Logs { get; set; } = string.Empty;
    public int LogCallCount { get; private set; }
    public int ProvisionCallCount { get; private set; }
    public int OpenFirewallCallCount { get; private set; }

    public Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ProvisionCallCount++;
        return Task.FromResult(ProvisionResult);
    }

    public Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        OpenFirewallCallCount++;
        return Task.CompletedTask;
    }

    public Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct) =>
        Task.FromResult(Healthy);

    public Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct) =>
        Task.FromResult(new ServerStatus(serverId, ServerState.Running, "198.51.100.1", "ok"));

    public Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        LogCallCount++;
        return Task.FromResult(LogsSequence.Count > 0 ? LogsSequence.Dequeue() : Logs);
    }

    public Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct) => Task.CompletedTask;

    public Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct) => Task.CompletedTask;
}
