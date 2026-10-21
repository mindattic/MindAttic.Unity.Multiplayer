namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;

/// <summary>
/// The slice of Azure Resource Manager's REST API <see cref="AzureVmProvisioner"/> needs. The only
/// seam between the provisioner and the network, so NUnit mocks it directly instead of faking OAuth2
/// client-credentials + ARM HTTP/JSON. A VM needs four ARM resources (NSG, public IP, NIC, VM
/// itself); <see cref="AzureVmProvisioner"/> names all four deterministically from the server name,
/// so there is nothing to pack into a server id -- it is the VM name alone.
/// </summary>
public interface IAzureVmApiClient
{
    /// <summary>Creates (or replaces) a network security group with <paramref name="rules"/> (empty is valid -- mp-host authorizes them in a later call).</summary>
    Task<string> CreateOrUpdateNetworkSecurityGroupAsync(string resourceGroup, string nsgName, string location, IReadOnlyList<AzureSecurityRule> rules, CancellationToken ct);

    /// <summary>Creates (or reuses) a static public IP address. Unlike the other UM-T27 providers, Azure assigns this IP immediately -- before the VM that will use it even exists.</summary>
    Task<string> CreateOrUpdatePublicIpAsync(string resourceGroup, string ipName, string location, CancellationToken ct);

    /// <summary>The public IP address's current value, or null if Azure has not assigned one yet.</summary>
    Task<string?> GetPublicIpAddressAsync(string resourceGroup, string ipName, CancellationToken ct);

    /// <summary>Creates (or replaces) the NIC that wires the public IP, the subnet and the NSG together.</summary>
    Task<string> CreateOrUpdateNetworkInterfaceAsync(string resourceGroup, string nicName, string location, string subnetId, string publicIpId, string nsgId, CancellationToken ct);

    /// <summary>Creates the VM itself, attached to an already-created NIC.</summary>
    Task<AzureVmInfo> CreateOrUpdateVirtualMachineAsync(AzureCreateVmRequest request, CancellationToken ct);

    /// <summary>The VM's current provisioning and power state.</summary>
    Task<AzureVmInfo> GetVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct);

    /// <summary>Deletes the VM. Safe to call on an already-deleted one.</summary>
    Task DeleteVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct);

    /// <summary>Deletes the NIC. Safe to call on an already-deleted one.</summary>
    Task DeleteNetworkInterfaceAsync(string resourceGroup, string nicName, CancellationToken ct);

    /// <summary>Deletes the public IP. Safe to call on an already-deleted one.</summary>
    Task DeletePublicIpAsync(string resourceGroup, string ipName, CancellationToken ct);

    /// <summary>Deletes the NSG. Safe to call on an already-deleted one.</summary>
    Task DeleteNetworkSecurityGroupAsync(string resourceGroup, string nsgName, CancellationToken ct);
}
