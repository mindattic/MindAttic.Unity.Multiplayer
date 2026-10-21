using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;

/// <summary>
/// Dry-run <see cref="IAzureVmApiClient"/>: prints the ARM call it would have made (NSG, public IP,
/// NIC, VM) and returns synthesized resources so the rest of the pipeline still has something to
/// narrate against. No network call is made and no Entra ID token is requested.
/// </summary>
public sealed class DryRunAzureVmApiClient : IAzureVmApiClient
{
    private const string SimulatedIp = "203.0.113.50";
    private readonly IDeployLog _log;

    public DryRunAzureVmApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<string> CreateOrUpdateNetworkSecurityGroupAsync(string resourceGroup, string nsgName, string location, IReadOnlyList<AzureSecurityRule> rules, CancellationToken ct)
    {
        _log.DryRun($"PUT https://management.azure.com/.../networkSecurityGroups/{nsgName} rules={JsonSerializer.Serialize(rules)}");
        return Task.FromResult($"/dryrun/nsg/{nsgName}");
    }

    /// <inheritdoc />
    public Task<string> CreateOrUpdatePublicIpAsync(string resourceGroup, string ipName, string location, CancellationToken ct)
    {
        _log.DryRun($"PUT https://management.azure.com/.../publicIPAddresses/{ipName}");
        return Task.FromResult($"/dryrun/ip/{ipName}");
    }

    /// <inheritdoc />
    public Task<string?> GetPublicIpAddressAsync(string resourceGroup, string ipName, CancellationToken ct)
    {
        _log.DryRun($"GET https://management.azure.com/.../publicIPAddresses/{ipName}");
        return Task.FromResult<string?>(SimulatedIp);
    }

    /// <inheritdoc />
    public Task<string> CreateOrUpdateNetworkInterfaceAsync(string resourceGroup, string nicName, string location, string subnetId, string publicIpId, string nsgId, CancellationToken ct)
    {
        _log.DryRun($"PUT https://management.azure.com/.../networkInterfaces/{nicName} subnet={subnetId} publicIp={publicIpId} nsg={nsgId}");
        return Task.FromResult($"/dryrun/nic/{nicName}");
    }

    /// <inheritdoc />
    public Task<AzureVmInfo> CreateOrUpdateVirtualMachineAsync(AzureCreateVmRequest request, CancellationToken ct)
    {
        _log.DryRun($"PUT https://management.azure.com/.../virtualMachines/{request.Name} size={request.VmSize} image={request.Image}");
        return Task.FromResult(new AzureVmInfo(request.Name, "Succeeded", "running"));
    }

    /// <inheritdoc />
    public Task<AzureVmInfo> GetVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct)
    {
        _log.DryRun($"GET https://management.azure.com/.../virtualMachines/{vmName}?$expand=instanceView");
        return Task.FromResult(new AzureVmInfo(vmName, "Succeeded", "running"));
    }

    /// <inheritdoc />
    public Task DeleteVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://management.azure.com/.../virtualMachines/{vmName}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteNetworkInterfaceAsync(string resourceGroup, string nicName, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://management.azure.com/.../networkInterfaces/{nicName}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeletePublicIpAsync(string resourceGroup, string ipName, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://management.azure.com/.../publicIPAddresses/{ipName}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteNetworkSecurityGroupAsync(string resourceGroup, string nsgName, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://management.azure.com/.../networkSecurityGroups/{nsgName}");
        return Task.CompletedTask;
    }
}
