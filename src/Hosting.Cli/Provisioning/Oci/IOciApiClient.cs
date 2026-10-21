namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

/// <summary>
/// The slice of Oracle Cloud Infrastructure's Core Services API <see cref="OciProvisioner"/> needs.
/// The only seam between the provisioner and the network, so NUnit mocks it directly instead of
/// faking OCI's request-signing scheme (<see cref="OciRequestSigner"/> is tested separately, on its
/// own). A security list, unlike a GCE firewall rule or an Azure NSG, is a shared, subnet-wide
/// resource that already exists on every VCN subnet -- "opening the firewall" here means reading its
/// current rules and writing back a superset, never creating or deleting the list itself.
/// </summary>
public interface IOciApiClient
{
    /// <summary>Launches one instance and waits for OCI to accept the request (not for it to finish booting).</summary>
    Task<OciInstance> LaunchInstanceAsync(OciLaunchInstanceRequest request, CancellationToken ct);

    /// <summary>The instance's current lifecycle state and public IP (resolved from its primary VNIC), or null before one is assigned.</summary>
    Task<OciInstance> GetInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>Terminates an instance (and, by default, its attached boot volume). Safe to call on an already-terminated one.</summary>
    Task TerminateInstanceAsync(string instanceId, CancellationToken ct);

    /// <summary>The security list's current ingress rules.</summary>
    Task<IReadOnlyList<OciSecurityRule>> GetIngressSecurityRulesAsync(string securityListId, CancellationToken ct);

    /// <summary>Replaces the security list's ingress rules wholesale (OCI's <c>UpdateSecurityList</c> has no "append").</summary>
    Task SetIngressSecurityRulesAsync(string securityListId, IReadOnlyList<OciSecurityRule> rules, CancellationToken ct);
}
