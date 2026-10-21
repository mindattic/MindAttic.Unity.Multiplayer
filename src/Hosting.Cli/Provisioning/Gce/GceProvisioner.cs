using System.Text.RegularExpressions;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVm;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

/// <summary>
/// <see cref="IProvisioner"/> for <c>Gce</c> (UM-T27): creates a GCE firewall rule plus an instance
/// running the GenericVps-shaped compose stack, booted via <see cref="CloudInitRenderer.RenderStartupScript"/>
/// passed as the <c>startup-script</c> metadata key (GCE's guest agent runs this literally, unlike
/// AWS/Azure's cloud-init-parsed user-data). Once the instance has a public (NAT) IP,
/// status/logs/health/update are identical to GenericVps's SSH commands.
///
/// <para>Both the instance and the firewall rule are tagged/named <c>mp-host-&lt;server name&gt;</c>
/// (sanitized to GCE's resource-name rules), so <c>DestroyAsync</c> can find the firewall rule from
/// <see cref="ProviderConnection.ServerName"/> alone without packing a second id into the server id;
/// the server id this provisioner hands back is just <c>zone|instanceName</c>.</para>
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys: <c>sshUser</c> (default <c>ubuntu</c>),
/// <c>sshKeyPath</c> (required -- the local private half of <c>sshPublicKeyPath</c>),
/// <c>sshPublicKeyPath</c> (required -- its content is embedded in instance metadata so the guest
/// agent provisions the login), <c>sourceImage</c> (default Ubuntu 22.04 LTS), <c>network</c>
/// (default <c>global/networks/default</c>), <c>machineType</c> (default from
/// <see cref="ProvisionRequest.Size"/> or <c>e2-small</c>), <c>zone</c> (default from
/// <see cref="ProvisionRequest.Region"/> or <c>us-central1-a</c>), <c>rendezvousImage</c>.</para>
/// </summary>
public sealed class GceProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "Gce";

    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";
    private const string DefaultMachineType = "e2-small";
    private const string DefaultZone = "us-central1-a";
    private const string DefaultSourceImage = "projects/ubuntu-os-cloud/global/images/family/ubuntu-2204-lts";
    private const string DefaultNetwork = "global/networks/default";
    private const string DefaultSshUser = "ubuntu";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublicIpTimeout = TimeSpan.FromMinutes(5);
    private static readonly Regex InvalidTagChars = new("[^a-z0-9-]", RegexOptions.Compiled);

    private readonly IGceApiClient _api;
    private readonly ISshCommandRunner _ssh;

    public GceProvisioner(IGceApiClient api, ISshCommandRunner ssh)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "Google Compute Engine (firewall rule + instance, startup-script)";

    /// <summary>The deterministic firewall rule name (and instance network tag) mp-host uses for <paramref name="serverName"/>.</summary>
    public static string FirewallNameFor(string serverName) => "mp-host-" + InvalidTagChars.Replace(serverName.ToLowerInvariant(), "-").Trim('-');

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Connection.ProviderOptions;
        string sshUser = options.GetValueOrDefault("sshUser", DefaultSshUser);
        string sshPublicKeyPath = options.GetValueOrDefault("sshPublicKeyPath") ?? throw new ArgumentException("ProviderOptions['sshPublicKeyPath'] is required for Gce.");
        string zone = request.Region ?? options.GetValueOrDefault("zone", DefaultZone);
        string machineType = request.Size ?? options.GetValueOrDefault("machineType", DefaultMachineType);
        string sourceImage = options.GetValueOrDefault("sourceImage", DefaultSourceImage);
        string network = options.GetValueOrDefault("network", DefaultNetwork);
        string rendezvousImage = options.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);
        string tag = FirewallNameFor(request.ServerName);

        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        string bootstrap = CloudInitRenderer.RenderBootstrapScript(request.GhcrRegistry, request.GhcrUsername);
        string startupScript = CloudInitRenderer.RenderStartupScript(sshUser, compose, bootstrap, request.GhcrToken);
        string sshPublicKey = File.ReadAllText(sshPublicKeyPath).Trim();

        log.Info($"Creating a {machineType} GCE instance for {request.ServerName} in {zone} from {sourceImage}.");
        GceInstance instance = await _api.InsertInstanceAsync(
            new GceInsertInstanceRequest(request.ServerName, zone, machineType, sourceImage, network, tag, sshUser, sshPublicKey, startupScript), ct);

        instance = await WaitForPublicIpAsync(zone, instance, log, ct);
        var mappings = request.Manifest.Ports.ToDictionary(p => p.Port, p => p.Port);
        return new ProvisionResult(CloudVmServerId.Combine(zone, instance.Name), instance.PublicIp ?? "", mappings);
    }

    /// <inheritdoc />
    /// <remarks>mp-host's firewall rule has no rules until this runs; without it nothing, including mp-host's own later SSH calls, can reach the box.</remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tag = FirewallNameFor(request.ServerName);
        string network = request.Connection.ProviderOptions.GetValueOrDefault("network", DefaultNetwork);
        var allowed = new List<GceFirewallAllow> { new("tcp", new[] { "22", "8080" }) };
        foreach (var group in request.Manifest.Ports.Where(p => !p.IsHealthPort).GroupBy(p => p.Protocol, StringComparer.OrdinalIgnoreCase))
        {
            allowed.Add(new GceFirewallAllow(group.Key.ToLowerInvariant(), group.Select(p => p.Port.ToString()).ToList()));
        }

        log.Info($"Creating firewall rule {tag} for {string.Join(", ", allowed.Select(a => $"{a.IpProtocol}:{string.Join('/', a.Ports)}"))}.");
        await _api.CreateFirewallRuleAsync(tag, network, tag, allowed, ct);
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
        var (zone, instanceName) = CloudVmServerId.Split(serverId);
        GceInstance instance = await _api.GetInstanceAsync(zone, instanceName, ct);
        ServerState cloudState = MapState(instance.Status);
        if (cloudState != ServerState.Running || instance.PublicIp is null)
        {
            return new ServerStatus(serverId, cloudState, instance.PublicIp, $"gceStatus={instance.Status}");
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
    /// <remarks>A GCE instance persists across an image swap, same as the other UM-T27 VM providers: an in-place rolling restart over SSH.</remarks>
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
        var (zone, instanceName) = CloudVmServerId.Split(serverId);
        SshTarget? target = await ResolveSshTargetAsync(serverId, connection, ct);
        if (target is not null)
        {
            await CloudVmSshOperations.TryComposeDownAsync(_ssh, target, log, ct);
        }

        log.Info($"Deleting GCE instance {instanceName} in {zone}.");
        await _api.DeleteInstanceAsync(zone, instanceName, ct);

        string tag = FirewallNameFor(connection.ServerName);
        try
        {
            await _api.DeleteFirewallRuleAsync(tag, ct);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not delete firewall rule {tag} ({ex.Message}); delete it yourself if no other instance uses it.");
        }
    }

    private async Task<GceInstance> WaitForPublicIpAsync(string zone, GceInstance instance, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + PublicIpTimeout;
        while (instance.PublicIp is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);
            instance = await _api.GetInstanceAsync(zone, instance.Name, ct);
        }

        if (instance.PublicIp is null)
        {
            log.Warn($"{instance.Name} did not get a public IP within {PublicIpTimeout}.");
        }

        return instance;
    }

    private async Task<SshTarget?> ResolveSshTargetAsync(string serverId, ProviderConnection connection, CancellationToken ct)
    {
        var (zone, instanceName) = CloudVmServerId.Split(serverId);
        GceInstance instance = await _api.GetInstanceAsync(zone, instanceName, ct);
        if (instance.PublicIp is null) return null;
        var (user, keyPath) = ResolveSsh(connection);
        return new SshTarget(instance.PublicIp, 22, user, keyPath, null);
    }

    private static (string User, string KeyPath) ResolveSsh(ProviderConnection connection)
    {
        string user = connection.ProviderOptions.GetValueOrDefault("sshUser", DefaultSshUser);
        string keyPath = connection.ProviderOptions.GetValueOrDefault("sshKeyPath")
            ?? throw new ArgumentException("ProviderOptions['sshKeyPath'] is required for Gce.");
        return (user, keyPath);
    }

    private static ServerState MapState(string gceStatus) => gceStatus switch
    {
        "PROVISIONING" or "STAGING" => ServerState.Provisioning,
        "RUNNING" => ServerState.Running,
        "STOPPING" or "STOPPED" or "SUSPENDED" or "SUSPENDING" => ServerState.Stopped,
        "TERMINATED" => ServerState.Gone,
        _ => ServerState.Unknown,
    };
}
