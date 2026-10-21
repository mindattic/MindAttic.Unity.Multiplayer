using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="Ec2Provisioner"/> makes and answers canned instances, with no real HTTP.</summary>
public sealed class FakeEc2ApiClient : IEc2ApiClient
{
    public List<(string Name, string? VpcId)> CreateGroupCalls { get; } = new();
    public List<(string GroupId, IReadOnlyList<Ec2IngressRule> Rules)> AuthorizeCalls { get; } = new();
    public List<Ec2RunInstanceRequest> RunCalls { get; } = new();
    public List<string> DescribeCalls { get; } = new();
    public List<string> TerminateCalls { get; } = new();
    public List<string> DeleteGroupCalls { get; } = new();

    public string GroupIdToReturn { get; set; } = "sg-1";
    public Ec2Instance InstanceToReturn { get; set; } = new("i-1", "running", "198.51.100.10");
    public Queue<Ec2Instance>? DescribeSequence { get; set; }

    public Task<string> CreateSecurityGroupAsync(string name, string? vpcId, CancellationToken ct)
    {
        CreateGroupCalls.Add((name, vpcId));
        return Task.FromResult(GroupIdToReturn);
    }

    public Task AuthorizeIngressAsync(string groupId, IReadOnlyList<Ec2IngressRule> rules, CancellationToken ct)
    {
        AuthorizeCalls.Add((groupId, rules));
        return Task.CompletedTask;
    }

    public Task<Ec2Instance> RunInstanceAsync(Ec2RunInstanceRequest request, CancellationToken ct)
    {
        RunCalls.Add(request);
        return Task.FromResult(InstanceToReturn);
    }

    public Task<Ec2Instance> DescribeInstanceAsync(string instanceId, CancellationToken ct)
    {
        DescribeCalls.Add(instanceId);
        var instance = DescribeSequence is { Count: > 0 } ? DescribeSequence.Dequeue() : InstanceToReturn;
        return Task.FromResult(instance);
    }

    public Task TerminateInstanceAsync(string instanceId, CancellationToken ct)
    {
        TerminateCalls.Add(instanceId);
        return Task.CompletedTask;
    }

    public Task DeleteSecurityGroupAsync(string groupId, CancellationToken ct)
    {
        DeleteGroupCalls.Add(groupId);
        return Task.CompletedTask;
    }
}
