using Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="AzureVmProvisioner"/> makes and answers canned resources, with no real HTTP.</summary>
public sealed class FakeAzureVmApiClient : IAzureVmApiClient
{
    public List<(string ResourceGroup, string Name, IReadOnlyList<AzureSecurityRule> Rules)> NsgCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> PublicIpCreateCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> PublicIpGetCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> NicCalls { get; } = new();
    public List<AzureCreateVmRequest> VmCreateCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> VmGetCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> DeleteVmCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> DeleteNicCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> DeleteIpCalls { get; } = new();
    public List<(string ResourceGroup, string Name)> DeleteNsgCalls { get; } = new();

    public string? PublicIpToReturn { get; set; } = "198.51.100.40";
    public Queue<string?>? PublicIpSequence { get; set; }
    public AzureVmInfo VmToReturn { get; set; } = new("srv", "Succeeded", "running");

    public Task<string> CreateOrUpdateNetworkSecurityGroupAsync(string resourceGroup, string nsgName, string location, IReadOnlyList<AzureSecurityRule> rules, CancellationToken ct)
    {
        NsgCalls.Add((resourceGroup, nsgName, rules));
        return Task.FromResult($"/nsg/{nsgName}");
    }

    public Task<string> CreateOrUpdatePublicIpAsync(string resourceGroup, string ipName, string location, CancellationToken ct)
    {
        PublicIpCreateCalls.Add((resourceGroup, ipName));
        return Task.FromResult($"/ip/{ipName}");
    }

    public Task<string?> GetPublicIpAddressAsync(string resourceGroup, string ipName, CancellationToken ct)
    {
        PublicIpGetCalls.Add((resourceGroup, ipName));
        string? ip = PublicIpSequence is { Count: > 0 } ? PublicIpSequence.Dequeue() : PublicIpToReturn;
        return Task.FromResult(ip);
    }

    public Task<string> CreateOrUpdateNetworkInterfaceAsync(string resourceGroup, string nicName, string location, string subnetId, string publicIpId, string nsgId, CancellationToken ct)
    {
        NicCalls.Add((resourceGroup, nicName));
        return Task.FromResult($"/nic/{nicName}");
    }

    public Task<AzureVmInfo> CreateOrUpdateVirtualMachineAsync(AzureCreateVmRequest request, CancellationToken ct)
    {
        VmCreateCalls.Add(request);
        return Task.FromResult(VmToReturn);
    }

    public Task<AzureVmInfo> GetVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct)
    {
        VmGetCalls.Add((resourceGroup, vmName));
        return Task.FromResult(VmToReturn);
    }

    public Task DeleteVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct)
    {
        DeleteVmCalls.Add((resourceGroup, vmName));
        return Task.CompletedTask;
    }

    public Task DeleteNetworkInterfaceAsync(string resourceGroup, string nicName, CancellationToken ct)
    {
        DeleteNicCalls.Add((resourceGroup, nicName));
        return Task.CompletedTask;
    }

    public Task DeletePublicIpAsync(string resourceGroup, string ipName, CancellationToken ct)
    {
        DeleteIpCalls.Add((resourceGroup, ipName));
        return Task.CompletedTask;
    }

    public Task DeleteNetworkSecurityGroupAsync(string resourceGroup, string nsgName, CancellationToken ct)
    {
        DeleteNsgCalls.Add((resourceGroup, nsgName));
        return Task.CompletedTask;
    }
}
