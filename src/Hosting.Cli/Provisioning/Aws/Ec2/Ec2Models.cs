namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;

/// <summary>An EC2 instance, as reported by <c>RunInstances</c>/<c>DescribeInstances</c>.</summary>
public sealed record Ec2Instance(string InstanceId, string State, string? PublicIp);

/// <summary>What to ask EC2's <c>RunInstances</c> to create.</summary>
public sealed record Ec2RunInstanceRequest(
    string Name,
    string ImageId,
    string InstanceType,
    string KeyName,
    string SecurityGroupId,
    string? SubnetId,
    string UserDataBase64);

/// <summary>One ingress rule: a port and the protocol it's opened for (<c>udp</c> or <c>tcp</c>).</summary>
public sealed record Ec2IngressRule(int Port, string Protocol);
