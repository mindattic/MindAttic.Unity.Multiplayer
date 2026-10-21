namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

/// <summary>
/// The slice of Google Compute Engine's REST API <see cref="GceProvisioner"/> needs. The only seam
/// between the provisioner and the network, so NUnit mocks it directly instead of faking OAuth2 +
/// HTTP/JSON. Unlike EC2's server-assigned security-group id, a GCE firewall rule's name is
/// client-chosen, so there is nothing to hand back here beyond success/failure.
/// </summary>
public interface IGceApiClient
{
    /// <summary>Creates (or replaces) a firewall rule named <paramref name="name"/> targeting <paramref name="targetTag"/>'s instances.</summary>
    Task CreateFirewallRuleAsync(string name, string network, string targetTag, IReadOnlyList<GceFirewallAllow> allowed, CancellationToken ct);

    /// <summary>Inserts one instance and waits for GCE to accept the request (not for it to finish booting).</summary>
    Task<GceInstance> InsertInstanceAsync(GceInsertInstanceRequest request, CancellationToken ct);

    /// <summary>The instance's current status and public (NAT) IP, or null before one is assigned.</summary>
    Task<GceInstance> GetInstanceAsync(string zone, string instanceName, CancellationToken ct);

    /// <summary>Deletes an instance. Safe to call on an already-deleted one.</summary>
    Task DeleteInstanceAsync(string zone, string instanceName, CancellationToken ct);

    /// <summary>Deletes a firewall rule. Safe to call on an already-deleted one.</summary>
    Task DeleteFirewallRuleAsync(string name, CancellationToken ct);
}
