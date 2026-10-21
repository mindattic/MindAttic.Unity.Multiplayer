namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;

/// <summary>
/// The slice of EC2's Query API <see cref="Ec2Provisioner"/> needs. The only seam between the
/// provisioner and the network, so NUnit mocks it directly instead of faking signed HTTP/XML.
/// </summary>
public interface IEc2ApiClient
{
    /// <summary>Creates an (initially empty) VPC security group mp-host owns for this server.</summary>
    Task<string> CreateSecurityGroupAsync(string name, string? vpcId, CancellationToken ct);

    /// <summary>Opens ingress for every <paramref name="rules"/> entry, from anywhere (<c>0.0.0.0/0</c>).</summary>
    Task AuthorizeIngressAsync(string groupId, IReadOnlyList<Ec2IngressRule> rules, CancellationToken ct);

    /// <summary>Launches one instance and waits for EC2 to accept the request (not for it to finish booting).</summary>
    Task<Ec2Instance> RunInstanceAsync(Ec2RunInstanceRequest request, CancellationToken ct);

    /// <summary>The instance's current state and public IP (null until EC2 has assigned one).</summary>
    Task<Ec2Instance> DescribeInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>Terminates an instance. Safe to call on an already-terminated one.</summary>
    Task TerminateInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>Deletes a security group. Safe to call on an already-deleted one.</summary>
    Task DeleteSecurityGroupAsync(string groupId, CancellationToken ct);
}
