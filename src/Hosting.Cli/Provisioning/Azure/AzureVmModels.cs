namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;

/// <summary>An Azure VM, as reported by <c>virtualMachines/{name}?$expand=instanceView</c>.</summary>
public sealed record AzureVmInfo(string Name, string ProvisioningState, string? PowerState);

/// <summary>One inbound NSG rule (one per declared port, plus SSH and rendezvous).</summary>
public sealed record AzureSecurityRule(string Name, int Priority, string Protocol, int Port);

/// <summary>A Marketplace image: <c>publisher:offer:sku:version</c>.</summary>
public sealed record AzureImageReference(string Publisher, string Offer, string Sku, string Version)
{
    /// <summary>Ubuntu 22.04 LTS (Gen2), Canonical's current default.</summary>
    public static AzureImageReference Ubuntu2204 { get; } = new("Canonical", "0001-com-ubuntu-server-jammy", "22_04-lts-gen2", "latest");
}

/// <summary>What to ask <c>virtualMachines</c> PUT to create.</summary>
public sealed record AzureCreateVmRequest(
    string Name,
    string ResourceGroup,
    string Location,
    string VmSize,
    AzureImageReference Image,
    string NetworkInterfaceId,
    string AdminUsername,
    string SshPublicKey,
    string CustomDataBase64);
