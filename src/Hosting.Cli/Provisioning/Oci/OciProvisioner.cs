using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVm;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

/// <summary>
/// <see cref="IProvisioner"/> for <c>Oci</c> (UM-T31): Oracle Cloud's Always Free tier. Launches an
/// instance running the GenericVps-shaped compose stack, booted via
/// <see cref="CloudInitRenderer.RenderCloudConfig"/> passed as base64 <c>user_data</c> metadata
/// (OCI's Oracle Cloud Agent runs cloud-init the same way AWS/Azure's Linux images do). Once the
/// instance has a public IP, status/logs/health/update are identical to GenericVps's SSH commands.
///
/// <para>Two Always Free shapes matter here: <c>VM.Standard.A1.Flex</c> (Ampere A1, ARM64, up to 4
/// OCPUs/24 GB across an account) and <c>VM.Standard.E2.1.Micro</c> (AMD, x86_64, fixed 1/8 shared
/// OCPU, two instances per account). mp-host defaults to the AMD micro shape
/// (<see cref="DefaultShape"/>) because, as of this writing, Unity's Linux Dedicated Server build
/// target (<c>UnityEditor.BuildTarget.StandaloneLinux64</c>) is x86_64 only -- there is no Linux
/// ARM64 Standalone/Server <c>BuildTarget</c> in the Unity Editor, so an Ampere A1 image has no
/// matching Unity server build to run. Pass <c>--opt shape=VM.Standard.A1.Flex --opt ocpus=4 --opt
/// memoryInGBs=24</c> once Unity ships (or a project cross-compiles) a Linux ARM64 dedicated server
/// build; see <c>docs/articles/hosting/oracle-oci.md</c> for the verification this finding is based
/// on and how to re-check it against a newer Unity release.</para>
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys: <c>compartmentId</c> (filled in from
/// <see cref="Credentials.HostingCredentialStore.OciCreds"/> by <see cref="Composition.HostingComposer"/>),
/// <c>availabilityDomain</c> (required), <c>subnetId</c> (required -- an already-existing VCN
/// subnet; mp-host does not create a VCN), <c>securityListId</c> (required -- that subnet's security
/// list; mp-host widens its ingress rules rather than creating a new list, since a list is
/// subnet-wide, not per-instance), <c>imageId</c> (required -- the boot image's OCID; Canonical
/// Ubuntu's OCID is region- and shape-specific, so mp-host does not default it), <c>shape</c>
/// (default <see cref="DefaultShape"/>, or from <see cref="ProvisionRequest.Size"/>), <c>ocpus</c>/
/// <c>memoryInGBs</c> (Flex shapes only, default 1/6), <c>sshUser</c> (default <c>ubuntu</c>),
/// <c>sshKeyPath</c> (required), <c>sshPublicKeyPath</c> (required), <c>rendezvousImage</c>.</para>
/// </summary>
public sealed class OciProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "Oci";

    /// <summary>The Always Free AMD micro shape mp-host defaults to (see the class doc for why not Ampere A1).</summary>
    public const string DefaultShape = "VM.Standard.E2.1.Micro";

    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";
    private const string DefaultSshUser = "ubuntu";
    private const int DefaultOcpus = 1;
    private const int DefaultMemoryInGBs = 6;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublicIpTimeout = TimeSpan.FromMinutes(5);

    private readonly IOciApiClient _api;
    private readonly ISshCommandRunner _ssh;

    public OciProvisioner(IOciApiClient api, ISshCommandRunner ssh)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "Oracle Cloud Infrastructure (Always Free instance, cloud-init)";

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Connection.ProviderOptions;
        string compartmentId = RequireOption(options, "compartmentId");
        string availabilityDomain = RequireOption(options, "availabilityDomain");
        string subnetId = RequireOption(options, "subnetId");
        string imageId = RequireOption(options, "imageId");
        string sshUser = options.GetValueOrDefault("sshUser", DefaultSshUser);
        string sshPublicKeyPath = RequireOption(options, "sshPublicKeyPath");
        string shape = request.Size ?? options.GetValueOrDefault("shape", DefaultShape);
        int ocpus = int.TryParse(options.GetValueOrDefault("ocpus"), out int o) ? o : DefaultOcpus;
        int memoryInGBs = int.TryParse(options.GetValueOrDefault("memoryInGBs"), out int m) ? m : DefaultMemoryInGBs;
        string rendezvousImage = options.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);

        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        string bootstrap = CloudInitRenderer.RenderBootstrapScript(request.GhcrRegistry, request.GhcrUsername);
        string cloudConfig = CloudInitRenderer.RenderCloudConfig(sshUser, compose, bootstrap, request.GhcrToken);
        string userDataBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cloudConfig));
        string sshPublicKey = File.ReadAllText(sshPublicKeyPath).Trim();

        log.Info($"Launching a {shape} OCI instance for {request.ServerName} in {availabilityDomain}.");
        OciInstance instance = await _api.LaunchInstanceAsync(
            new OciLaunchInstanceRequest(compartmentId, availabilityDomain, subnetId, shape, ocpus, memoryInGBs, imageId, request.ServerName, sshPublicKey, userDataBase64), ct);

        instance = await WaitForPublicIpAsync(instance, log, ct);
        var mappings = request.Manifest.Ports.ToDictionary(p => p.Port, p => p.Port);
        return new ProvisionResult(instance.Id, instance.PublicIp ?? "", mappings);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Widens the subnet's existing security list rather than replacing it (other instances on the
    /// same subnet may depend on its current rules), so this only ever adds mp-host's own rules --
    /// never removes one it did not add itself.
    /// </remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string securityListId = RequireOption(request.Connection.ProviderOptions, "securityListId");
        var wanted = new List<OciSecurityRule> { new("tcp", 22, "mp-host SSH"), new("tcp", 8080, "mp-host rendezvous") };
        foreach (var port in request.Manifest.Ports.Where(p => !p.IsHealthPort))
        {
            wanted.Add(new OciSecurityRule(port.Protocol.ToLowerInvariant(), port.Port, $"mp-host {port.Name}"));
        }

        IReadOnlyList<OciSecurityRule> current = await _api.GetIngressSecurityRulesAsync(securityListId, ct);
        var merged = new List<OciSecurityRule>(current);
        foreach (var rule in wanted)
        {
            if (!merged.Any(r => r.Protocol.Equals(rule.Protocol, StringComparison.OrdinalIgnoreCase) && r.Port == rule.Port))
            {
                merged.Add(rule);
            }
        }

        log.Info($"Widening security list {securityListId} for {string.Join(", ", wanted.Select(r => $"{r.Port}/{r.Protocol}"))}.");
        await _api.SetIngressSecurityRulesAsync(securityListId, merged, ct);
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
        OciInstance instance = await _api.GetInstanceAsync(serverId, ct);
        ServerState cloudState = MapState(instance.LifecycleState);
        if (cloudState != ServerState.Running || instance.PublicIp is null)
        {
            return new ServerStatus(serverId, cloudState, instance.PublicIp, $"lifecycleState={instance.LifecycleState}");
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
    /// <remarks>An OCI instance persists across an image swap, same as the other cloud VM providers: an in-place rolling restart over SSH.</remarks>
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

        log.Info($"Terminating OCI instance {serverId}. Its security list rules are left in place (other instances on the subnet may use the same list).");
        await _api.TerminateInstanceAsync(serverId, ct);
    }

    private async Task<OciInstance> WaitForPublicIpAsync(OciInstance instance, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + PublicIpTimeout;
        while (instance.PublicIp is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);
            instance = await _api.GetInstanceAsync(instance.Id, ct);
        }

        if (instance.PublicIp is null)
        {
            log.Warn($"{instance.Id} did not get a public IP within {PublicIpTimeout}.");
        }

        return instance;
    }

    private async Task<SshTarget?> ResolveSshTargetAsync(string serverId, ProviderConnection connection, CancellationToken ct)
    {
        OciInstance instance = await _api.GetInstanceAsync(serverId, ct);
        if (instance.PublicIp is null) return null;
        var (user, keyPath) = ResolveSsh(connection);
        return new SshTarget(instance.PublicIp, 22, user, keyPath, null);
    }

    private static (string User, string KeyPath) ResolveSsh(ProviderConnection connection)
    {
        string user = connection.ProviderOptions.GetValueOrDefault("sshUser", DefaultSshUser);
        string keyPath = RequireOption(connection.ProviderOptions, "sshKeyPath");
        return (user, keyPath);
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string key) =>
        options.GetValueOrDefault(key) ?? throw new ArgumentException($"ProviderOptions['{key}'] is required for Oci.");

    private static ServerState MapState(string lifecycleState) => lifecycleState.ToUpperInvariant() switch
    {
        "PROVISIONING" or "STARTING" => ServerState.Provisioning,
        "RUNNING" => ServerState.Running,
        "STOPPING" or "STOPPED" => ServerState.Stopped,
        "TERMINATING" or "TERMINATED" => ServerState.Gone,
        _ => ServerState.Unknown,
    };
}
