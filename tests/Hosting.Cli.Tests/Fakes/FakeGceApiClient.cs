using Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="GceProvisioner"/> makes and answers canned instances, with no real HTTP.</summary>
public sealed class FakeGceApiClient : IGceApiClient
{
    public List<(string Name, string Network, string Tag, IReadOnlyList<GceFirewallAllow> Allowed)> CreateFirewallCalls { get; } = new();
    public List<GceInsertInstanceRequest> InsertCalls { get; } = new();
    public List<(string Zone, string Name)> GetCalls { get; } = new();
    public List<(string Zone, string Name)> DeleteInstanceCalls { get; } = new();
    public List<string> DeleteFirewallCalls { get; } = new();

    public GceInstance InstanceToReturn { get; set; } = new("srv", "RUNNING", "198.51.100.30");
    public Queue<GceInstance>? GetSequence { get; set; }

    public Task CreateFirewallRuleAsync(string name, string network, string targetTag, IReadOnlyList<GceFirewallAllow> allowed, CancellationToken ct)
    {
        CreateFirewallCalls.Add((name, network, targetTag, allowed));
        return Task.CompletedTask;
    }

    public Task<GceInstance> InsertInstanceAsync(GceInsertInstanceRequest request, CancellationToken ct)
    {
        InsertCalls.Add(request);
        return Task.FromResult(InstanceToReturn);
    }

    public Task<GceInstance> GetInstanceAsync(string zone, string instanceName, CancellationToken ct)
    {
        GetCalls.Add((zone, instanceName));
        var instance = GetSequence is { Count: > 0 } ? GetSequence.Dequeue() : InstanceToReturn;
        return Task.FromResult(instance);
    }

    public Task DeleteInstanceAsync(string zone, string instanceName, CancellationToken ct)
    {
        DeleteInstanceCalls.Add((zone, instanceName));
        return Task.CompletedTask;
    }

    public Task DeleteFirewallRuleAsync(string name, CancellationToken ct)
    {
        DeleteFirewallCalls.Add(name);
        return Task.CompletedTask;
    }
}
