using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVm;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;

/// <summary>
/// <see cref="IProvisioner"/> for <c>AwsLightsail</c> (UM-T27): the simpler of the two AWS profiles
/// -- one Lightsail instance running the GenericVps-shaped compose stack, booted via
/// <see cref="CloudInitRenderer.RenderCloudConfig"/> passed as <c>userData</c>. Unlike EC2,
/// Lightsail has no separate security-group resource: the firewall lives on the instance itself
/// (<c>PutInstancePublicPorts</c>), so there is no secondary id to track and the server id this
/// provisioner hands back is simply the Lightsail instance name.
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys: <c>keyPairName</c> (required -- a
/// Lightsail key pair already created in this account/region), <c>sshUser</c> (default <c>ubuntu</c>,
/// matching Lightsail's Ubuntu blueprint), <c>sshKeyPath</c> (required), <c>blueprintId</c> (default
/// <c>ubuntu_22_04</c>), <c>bundleId</c> (default from <see cref="ProvisionRequest.Size"/> or
/// <c>nano_3_0</c>, Lightsail's cheapest tier), <c>availabilityZone</c> (default from
/// <see cref="ProvisionRequest.Region"/> + <c>a</c>, e.g. region <c>us-east-1</c> -&gt; zone
/// <c>us-east-1a</c>), <c>rendezvousImage</c>.</para>
/// </summary>
public sealed class LightsailProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "AwsLightsail";

    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";
    private const string DefaultBlueprintId = "ubuntu_22_04";
    private const string DefaultBundleId = "nano_3_0";
    private const string DefaultSshUser = "ubuntu";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublicIpTimeout = TimeSpan.FromMinutes(5);

    private readonly ILightsailApiClient _api;
    private readonly ISshCommandRunner _ssh;

    public LightsailProvisioner(ILightsailApiClient api, ISshCommandRunner ssh)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "AWS Lightsail (instance with its own built-in firewall)";

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Connection.ProviderOptions;
        string keyPairName = options.GetValueOrDefault("keyPairName") ?? throw new ArgumentException("ProviderOptions['keyPairName'] is required for AwsLightsail.");
        string sshUser = options.GetValueOrDefault("sshUser", DefaultSshUser);
        string blueprintId = options.GetValueOrDefault("blueprintId", DefaultBlueprintId);
        string bundleId = request.Size ?? options.GetValueOrDefault("bundleId", DefaultBundleId);
        string zone = options.GetValueOrDefault("availabilityZone") ?? (request.Region is null ? null : $"{request.Region}a")
            ?? throw new ArgumentException("ProviderOptions['availabilityZone'] (or --region) is required for AwsLightsail.");
        string rendezvousImage = options.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);

        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        string bootstrap = CloudInitRenderer.RenderBootstrapScript(request.GhcrRegistry, request.GhcrUsername);
        string userData = CloudInitRenderer.RenderCloudConfig(sshUser, compose, bootstrap, request.GhcrToken);

        log.Info($"Creating a {bundleId} Lightsail instance for {request.ServerName} from {blueprintId} in {zone}.");
        LightsailInstance instance = await _api.CreateInstanceAsync(
            new LightsailCreateInstanceRequest(request.ServerName, zone, blueprintId, bundleId, keyPairName, userData), ct);

        instance = await WaitForPublicIpAsync(instance, log, ct);
        var mappings = request.Manifest.Ports.ToDictionary(p => p.Port, p => p.Port);
        return new ProvisionResult(instance.Name, instance.PublicIp ?? "", mappings);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>PutInstancePublicPorts</c> replaces the instance's whole port list, so mp-host owning the
    /// firewall here is a hard requirement, same as EC2's security group -- there is no best-effort
    /// fallback the way GenericVps's bring-your-own-box <c>ufw</c> has.
    /// </remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ports = new List<LightsailPortRange> { new(22, 22, "tcp"), new(8080, 8080, "tcp") };
        ports.AddRange(request.Manifest.Ports.Where(p => !p.IsHealthPort).Select(p => new LightsailPortRange(p.Port, p.Port, p.Protocol)));
        log.Info($"Opening public ports on {serverId}: {string.Join(", ", ports.Select(p => $"{p.FromPort}/{p.Protocol}"))}.");
        await _api.OpenPublicPortsAsync(serverId, ports, ct);
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
        LightsailInstance instance = await _api.GetInstanceAsync(serverId, ct);
        ServerState cloudState = MapState(instance.State);
        if (cloudState != ServerState.Running || instance.PublicIp is null)
        {
            return new ServerStatus(serverId, cloudState, instance.PublicIp, $"lightsailState={instance.State}");
        }

        var (user, keyPath) = ResolveSsh(connection);
        return await CloudVmSshOperations.GetComposeStatusAsync(_ssh, new SshTarget(instance.PublicIp, 22, user, keyPath, null), serverId, log, ct);
    }

    /// <inheritdoc />
    public async Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        SshTarget? target = await ResolveSshTargetAsync(serverId, connection, ct);
        if (target is null) return "";
        return await CloudVmSshOperations.GetLogsAsync(_ssh, target, tailLines, ct);
    }

    /// <inheritdoc />
    /// <remarks>A Lightsail instance persists across an image swap, same as EC2 and GenericVps: an in-place rolling restart over SSH.</remarks>
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
        SshTarget? target = await ResolveSshTargetAsync(serverId, connection, ct);
        if (target is not null)
        {
            await CloudVmSshOperations.TryComposeDownAsync(_ssh, target, log, ct);
        }

        log.Info($"Deleting Lightsail instance {serverId} (its built-in firewall goes with it).");
        await _api.DeleteInstanceAsync(serverId, ct);
    }

    private async Task<LightsailInstance> WaitForPublicIpAsync(LightsailInstance instance, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + PublicIpTimeout;
        while (instance.PublicIp is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);
            instance = await _api.GetInstanceAsync(instance.Name, ct);
        }

        if (instance.PublicIp is null)
        {
            log.Warn($"{instance.Name} did not get a public IP within {PublicIpTimeout}.");
        }

        return instance;
    }

    private async Task<SshTarget?> ResolveSshTargetAsync(string serverId, ProviderConnection connection, CancellationToken ct)
    {
        LightsailInstance instance = await _api.GetInstanceAsync(serverId, ct);
        if (instance.PublicIp is null) return null;
        var (user, keyPath) = ResolveSsh(connection);
        return new SshTarget(instance.PublicIp, 22, user, keyPath, null);
    }

    private static (string User, string KeyPath) ResolveSsh(ProviderConnection connection)
    {
        string user = connection.ProviderOptions.GetValueOrDefault("sshUser", DefaultSshUser);
        string keyPath = connection.ProviderOptions.GetValueOrDefault("sshKeyPath")
            ?? throw new ArgumentException("ProviderOptions['sshKeyPath'] is required for AwsLightsail.");
        return (user, keyPath);
    }

    private static ServerState MapState(string lightsailState) => lightsailState switch
    {
        "pending" => ServerState.Provisioning,
        "running" => ServerState.Running,
        "stopping" or "stopped" => ServerState.Stopped,
        "terminated" => ServerState.Gone,
        _ => ServerState.Unknown,
    };
}
