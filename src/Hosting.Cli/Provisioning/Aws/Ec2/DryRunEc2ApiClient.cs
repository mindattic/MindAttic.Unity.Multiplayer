using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;

/// <summary>
/// Dry-run <see cref="IEc2ApiClient"/>: prints the EC2 API call it would have made and returns a
/// synthesized instance/group so the rest of the pipeline (firewall, health check, join-code wait)
/// still has something to narrate against. No network call is made and no AWS credential is read.
/// </summary>
public sealed class DryRunEc2ApiClient : IEc2ApiClient
{
    private const string SimulatedGroupId = "sg-dryrun00000000";
    private const string SimulatedInstanceId = "i-dryrun000000000";
    private const string SimulatedIp = "203.0.113.20";

    private readonly IDeployLog _log;

    public DryRunEc2ApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<string> CreateSecurityGroupAsync(string name, string? vpcId, CancellationToken ct)
    {
        _log.DryRun($"POST https://ec2.{{region}}.amazonaws.com/ Action=CreateSecurityGroup GroupName={name} VpcId={vpcId}");
        return Task.FromResult(SimulatedGroupId);
    }

    /// <inheritdoc />
    public Task AuthorizeIngressAsync(string groupId, IReadOnlyList<Ec2IngressRule> rules, CancellationToken ct)
    {
        _log.DryRun($"POST https://ec2.{{region}}.amazonaws.com/ Action=AuthorizeSecurityGroupIngress GroupId={groupId} rules={JsonSerializer.Serialize(rules)}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Ec2Instance> RunInstanceAsync(Ec2RunInstanceRequest request, CancellationToken ct)
    {
        _log.DryRun($"POST https://ec2.{{region}}.amazonaws.com/ Action=RunInstances ImageId={request.ImageId} InstanceType={request.InstanceType} KeyName={request.KeyName} SecurityGroupId={request.SecurityGroupId}");
        return Task.FromResult(new Ec2Instance(SimulatedInstanceId, "running", SimulatedIp));
    }

    /// <inheritdoc />
    public Task<Ec2Instance> DescribeInstanceAsync(string instanceId, CancellationToken ct)
    {
        _log.DryRun($"POST https://ec2.{{region}}.amazonaws.com/ Action=DescribeInstances InstanceId.1={instanceId}");
        return Task.FromResult(new Ec2Instance(instanceId, "running", SimulatedIp));
    }

    /// <inheritdoc />
    public Task TerminateInstanceAsync(string instanceId, CancellationToken ct)
    {
        _log.DryRun($"POST https://ec2.{{region}}.amazonaws.com/ Action=TerminateInstances InstanceId.1={instanceId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteSecurityGroupAsync(string groupId, CancellationToken ct)
    {
        _log.DryRun($"POST https://ec2.{{region}}.amazonaws.com/ Action=DeleteSecurityGroup GroupId={groupId}");
        return Task.CompletedTask;
    }
}
