using Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="OciProvisioner"/> makes and answers canned instances/rules, with no real HTTP.</summary>
public sealed class FakeOciApiClient : IOciApiClient
{
    public List<OciLaunchInstanceRequest> LaunchCalls { get; } = new();
    public List<string> GetCalls { get; } = new();
    public List<string> TerminateCalls { get; } = new();
    public List<string> GetRulesCalls { get; } = new();
    public List<(string SecurityListId, IReadOnlyList<OciSecurityRule> Rules)> SetRulesCalls { get; } = new();

    public OciInstance InstanceToReturn { get; set; } = new("ocid1.instance.oc1..srv", "RUNNING", "198.51.100.50");
    public Queue<OciInstance>? GetSequence { get; set; }
    public IReadOnlyList<OciSecurityRule> ExistingRules { get; set; } = Array.Empty<OciSecurityRule>();

    public Task<OciInstance> LaunchInstanceAsync(OciLaunchInstanceRequest request, CancellationToken ct)
    {
        LaunchCalls.Add(request);
        return Task.FromResult(InstanceToReturn);
    }

    public Task<OciInstance> GetInstanceAsync(string instanceId, CancellationToken ct)
    {
        GetCalls.Add(instanceId);
        var instance = GetSequence is { Count: > 0 } ? GetSequence.Dequeue() : InstanceToReturn;
        return Task.FromResult(instance);
    }

    public Task TerminateInstanceAsync(string instanceId, CancellationToken ct)
    {
        TerminateCalls.Add(instanceId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OciSecurityRule>> GetIngressSecurityRulesAsync(string securityListId, CancellationToken ct)
    {
        GetRulesCalls.Add(securityListId);
        return Task.FromResult(ExistingRules);
    }

    public Task SetIngressSecurityRulesAsync(string securityListId, IReadOnlyList<OciSecurityRule> rules, CancellationToken ct)
    {
        SetRulesCalls.Add((securityListId, rules));
        return Task.CompletedTask;
    }
}
