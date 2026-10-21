using System.Text.RegularExpressions;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVm;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;

/// <summary>
/// <see cref="IProvisioner"/> for <c>AzureVm</c> (UM-T27): creates the four ARM resources a Linux VM
/// needs -- an NSG, a static public IP, a NIC wiring them (plus an existing VNet subnet) together,
/// and the VM itself -- running the GenericVps-shaped compose stack, booted via
/// <see cref="CloudInitRenderer.RenderCloudConfig"/> passed as <c>customData</c> (Azure's Linux
/// agent runs cloud-init just like AWS's). Once the VM has a public IP, status/logs/health/update
/// are identical to GenericVps's SSH commands.
///
/// <para>All four resources are named deterministically from the server name
/// (<c>mp-host-&lt;name&gt;-nsg</c>/<c>-ip</c>/<c>-nic</c>, and the VM itself as the sanitized name),
/// so every call can recompute them from <see cref="ProviderConnection.ServerName"/> alone; the
/// server id this provisioner hands back is just the VM name.</para>
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys: <c>resourceGroup</c> (required -- an
/// already-existing resource group), <c>subnetId</c> (required -- the full ARM id of an
/// already-existing VNet subnet; mp-host does not create a VNet), <c>sshUser</c> (default
/// <c>azureuser</c>), <c>sshKeyPath</c> (required), <c>sshPublicKeyPath</c> (required),
/// <c>vmSize</c> (default from <see cref="ProvisionRequest.Size"/> or <c>Standard_B1s</c>),
/// <c>location</c> (default from <see cref="ProvisionRequest.Region"/> or <c>eastus</c>),
/// <c>rendezvousImage</c>.</para>
/// </summary>
public sealed class AzureVmProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "AzureVm";

    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";
    private const string DefaultVmSize = "Standard_B1s";
    private const string DefaultLocation = "eastus";
    private const string DefaultSshUser = "azureuser";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublicIpTimeout = TimeSpan.FromMinutes(5);
    private static readonly Regex InvalidNameChars = new("[^a-zA-Z0-9-]", RegexOptions.Compiled);

    private readonly IAzureVmApiClient _api;
    private readonly ISshCommandRunner _ssh;

    public AzureVmProvisioner(IAzureVmApiClient api, ISshCommandRunner ssh)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "Azure VM (NSG + public IP + NIC + VM, cloud-init)";

    private static string VmNameFor(string serverName) => InvalidNameChars.Replace(serverName, "-").Trim('-') is { Length: > 0 } n ? (n.Length > 64 ? n[..64] : n) : "mp-host-vm";
    private static string NsgNameFor(string serverName) => $"mp-host-{VmNameFor(serverName)}-nsg";
    private static string IpNameFor(string serverName) => $"mp-host-{VmNameFor(serverName)}-ip";
    private static string NicNameFor(string serverName) => $"mp-host-{VmNameFor(serverName)}-nic";

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Connection.ProviderOptions;
        string resourceGroup = options.GetValueOrDefault("resourceGroup") ?? throw new ArgumentException("ProviderOptions['resourceGroup'] is required for AzureVm.");
        string subnetId = options.GetValueOrDefault("subnetId") ?? throw new ArgumentException("ProviderOptions['subnetId'] is required for AzureVm (create a VNet/subnet once; mp-host does not create one).");
        string sshUser = options.GetValueOrDefault("sshUser", DefaultSshUser);
        string sshPublicKeyPath = options.GetValueOrDefault("sshPublicKeyPath") ?? throw new ArgumentException("ProviderOptions['sshPublicKeyPath'] is required for AzureVm.");
        string location = request.Region ?? options.GetValueOrDefault("location", DefaultLocation);
        string vmSize = request.Size ?? options.GetValueOrDefault("vmSize", DefaultVmSize);
        string rendezvousImage = options.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);
        string vmName = VmNameFor(request.ServerName);
        string nsgName = NsgNameFor(request.ServerName);
        string ipName = IpNameFor(request.ServerName);
        string nicName = NicNameFor(request.ServerName);

        log.Info($"Creating NSG, public IP and NIC for {request.ServerName} in {resourceGroup}/{location}.");
        string nsgId = await _api.CreateOrUpdateNetworkSecurityGroupAsync(resourceGroup, nsgName, location, Array.Empty<AzureSecurityRule>(), ct);
        string ipId = await _api.CreateOrUpdatePublicIpAsync(resourceGroup, ipName, location, ct);
        string nicId = await _api.CreateOrUpdateNetworkInterfaceAsync(resourceGroup, nicName, location, subnetId, ipId, nsgId, ct);

        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        string bootstrap = CloudInitRenderer.RenderBootstrapScript(request.GhcrRegistry, request.GhcrUsername);
        string cloudConfig = CloudInitRenderer.RenderCloudConfig(sshUser, compose, bootstrap, request.GhcrToken);
        string customData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cloudConfig));
        string sshPublicKey = File.ReadAllText(sshPublicKeyPath).Trim();

        log.Info($"Creating a {vmSize} Azure VM {vmName}.");
        await _api.CreateOrUpdateVirtualMachineAsync(
            new AzureCreateVmRequest(vmName, resourceGroup, location, vmSize, AzureImageReference.Ubuntu2204, nicId, sshUser, sshPublicKey, customData), ct);

        string? publicIp = await WaitForPublicIpAsync(resourceGroup, ipName, log, ct);
        var mappings = request.Manifest.Ports.ToDictionary(p => p.Port, p => p.Port);
        return new ProvisionResult(vmName, publicIp ?? "", mappings);
    }

    /// <inheritdoc />
    /// <remarks>mp-host's NSG has no rules until this runs; without it nothing, including mp-host's own later SSH calls, can reach the box.</remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string resourceGroup = RequireOption(request.Connection, "resourceGroup");
        string location = request.Region ?? request.Connection.ProviderOptions.GetValueOrDefault("location", DefaultLocation);
        string nsgName = NsgNameFor(request.ServerName);

        var rules = new List<AzureSecurityRule> { new("ssh", 100, "Tcp", 22), new("rendezvous", 110, "Tcp", 8080) };
        int priority = 200;
        foreach (var port in request.Manifest.Ports.Where(p => !p.IsHealthPort))
        {
            rules.Add(new AzureSecurityRule($"game-{port.Port}", priority, ToAzureProtocol(port.Protocol), port.Port));
            priority += 10;
        }

        log.Info($"Authorizing NSG {nsgName} for {string.Join(", ", rules.Select(r => $"{r.Port}/{r.Protocol}"))}.");
        await _api.CreateOrUpdateNetworkSecurityGroupAsync(resourceGroup, nsgName, location, rules, ct);
    }

    /// <inheritdoc />
    public async Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SshTarget? target = await ResolveSshTargetAsync(serverId, request.Connection, ct);
        if (target is null)
        {
            log.Warn($"{serverId} has no public IP yet; cannot health-check it.");
            return false;
        }

        return await CloudVmSshOperations.WaitForHealthyAsync(_ssh, target, request.Manifest, timeout, log, ct);
    }

    /// <inheritdoc />
    public async Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        string resourceGroup = RequireOption(connection, "resourceGroup");
        AzureVmInfo vm = await _api.GetVirtualMachineAsync(resourceGroup, serverId, ct);
        ServerState cloudState = MapState(vm.ProvisioningState, vm.PowerState);
        string? publicIp = await _api.GetPublicIpAddressAsync(resourceGroup, IpNameFor(connection.ServerName), ct);
        if (cloudState != ServerState.Running || publicIp is null)
        {
            return new ServerStatus(serverId, cloudState, publicIp, $"provisioningState={vm.ProvisioningState}, powerState={vm.PowerState}");
        }

        var (user, keyPath) = ResolveSsh(connection);
        return await CloudVmSshOperations.GetComposeStatusAsync(_ssh, new SshTarget(publicIp, 22, user, keyPath, null), serverId, log, ct);
    }

    /// <inheritdoc />
    public async Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        SshTarget? target = await ResolveSshTargetAsync(serverId, connection, ct);
        if (target is null) return "";
        return await CloudVmSshOperations.GetLogsAsync(_ssh, target, tailLines, ct);
    }

    /// <inheritdoc />
    /// <remarks>An Azure VM persists across an image swap, same as the other UM-T27 VM providers: an in-place rolling restart over SSH.</remarks>
    public async Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SshTarget? target = await ResolveSshTargetAsync(serverId, request.Connection, ct);
        if (target is null) throw new InvalidOperationException($"{serverId} has no public IP; cannot reach it to update.");
        string rendezvousImage = request.Connection.ProviderOptions.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);
        await CloudVmSshOperations.UpdateAsync(_ssh, target, request, rendezvousImage, log, ct);
    }

    /// <inheritdoc />
    public async Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        string resourceGroup = RequireOption(connection, "resourceGroup");
        SshTarget? target = await ResolveSshTargetAsync(serverId, connection, ct);
        if (target is not null)
        {
            await CloudVmSshOperations.TryComposeDownAsync(_ssh, target, log, ct);
        }

        // Deletion order matters: the NIC references the public IP and NSG, and the VM references the NIC.
        log.Info($"Deleting Azure VM {serverId} and its NIC/public IP/NSG.");
        await _api.DeleteVirtualMachineAsync(resourceGroup, serverId, ct);
        await _api.DeleteNetworkInterfaceAsync(resourceGroup, NicNameFor(connection.ServerName), ct);
        try
        {
            await _api.DeletePublicIpAsync(resourceGroup, IpNameFor(connection.ServerName), ct);
            await _api.DeleteNetworkSecurityGroupAsync(resourceGroup, NsgNameFor(connection.ServerName), ct);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not delete the public IP/NSG for {connection.ServerName} yet ({ex.Message}); ARM sometimes needs the NIC deletion to finish propagating first -- delete them yourself once it has.");
        }
    }

    private async Task<string?> WaitForPublicIpAsync(string resourceGroup, string ipName, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + PublicIpTimeout;
        string? ip = await _api.GetPublicIpAddressAsync(resourceGroup, ipName, ct);
        while (ip is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);
            ip = await _api.GetPublicIpAddressAsync(resourceGroup, ipName, ct);
        }

        if (ip is null)
        {
            log.Warn($"{ipName} did not get a public IP within {PublicIpTimeout}.");
        }

        return ip;
    }

    private async Task<SshTarget?> ResolveSshTargetAsync(string serverId, ProviderConnection connection, CancellationToken ct)
    {
        string resourceGroup = RequireOption(connection, "resourceGroup");
        string? publicIp = await _api.GetPublicIpAddressAsync(resourceGroup, IpNameFor(connection.ServerName), ct);
        if (publicIp is null) return null;
        var (user, keyPath) = ResolveSsh(connection);
        return new SshTarget(publicIp, 22, user, keyPath, null);
    }

    private static (string User, string KeyPath) ResolveSsh(ProviderConnection connection)
    {
        string user = connection.ProviderOptions.GetValueOrDefault("sshUser", DefaultSshUser);
        string keyPath = connection.ProviderOptions.GetValueOrDefault("sshKeyPath")
            ?? throw new ArgumentException("ProviderOptions['sshKeyPath'] is required for AzureVm.");
        return (user, keyPath);
    }

    private static string RequireOption(ProviderConnection connection, string key) =>
        connection.ProviderOptions.GetValueOrDefault(key) ?? throw new ArgumentException($"ProviderOptions['{key}'] is required for AzureVm.");

    private static string ToAzureProtocol(string manifestProtocol) =>
        manifestProtocol.Equals("udp", StringComparison.OrdinalIgnoreCase) ? "Udp" : "Tcp";

    private static ServerState MapState(string provisioningState, string? powerState)
    {
        if (string.Equals(provisioningState, "Deleting", StringComparison.OrdinalIgnoreCase)) return ServerState.Gone;
        if (string.Equals(powerState, "running", StringComparison.OrdinalIgnoreCase)) return ServerState.Running;
        if (string.Equals(powerState, "stopped", StringComparison.OrdinalIgnoreCase) || string.Equals(powerState, "deallocated", StringComparison.OrdinalIgnoreCase)) return ServerState.Stopped;
        if (string.Equals(provisioningState, "Creating", StringComparison.OrdinalIgnoreCase) || string.Equals(provisioningState, "Updating", StringComparison.OrdinalIgnoreCase)) return ServerState.Provisioning;
        return ServerState.Unknown;
    }
}
