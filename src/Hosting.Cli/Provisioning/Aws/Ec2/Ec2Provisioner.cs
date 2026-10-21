using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVm;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;

/// <summary>
/// <see cref="IProvisioner"/> for <c>AwsEc2</c> (UM-T27): creates a VPC security group mp-host owns
/// plus an EC2 instance running the GenericVps-shaped compose stack (image + rendezvous side-car),
/// booted via a <see cref="CloudInitRenderer.RenderCloudConfig"/> cloud-init document passed as
/// instance <c>UserData</c>. Once the instance has a public IP, status/logs/health/update are
/// identical to GenericVps's SSH commands (<see cref="CloudVmSshOperations"/>) -- the cloud API only
/// ever owns the instance and its security group.
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys at provision time: <c>amiId</c>
/// (required -- AMI ids are region-specific, so there is no safe universal default), <c>keyName</c>
/// (required -- an EC2 key pair already created in this account/region), <c>sshUser</c> (default
/// <c>ubuntu</c>, matching Canonical's official Ubuntu AMIs), <c>sshKeyPath</c> (required -- the
/// local private half of <c>keyName</c>), <c>vpcId</c>/<c>subnetId</c> (optional), <c>instanceType</c>
/// (default from <see cref="ProvisionRequest.Size"/> or <c>t3.micro</c>), <c>rendezvousImage</c>
/// (default <c>ghcr.io/mindattic/rendezvous:latest</c>).</para>
///
/// <para>The server id this provisioner hands back is <c>instanceId|securityGroupId</c>
/// (<see cref="CloudVmServerId"/>) -- there is nowhere else for a later <c>status</c>/<c>logs</c>/
/// <c>destroy</c> invocation to learn which security group it owns.</para>
/// </summary>
public sealed class Ec2Provisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "AwsEc2";

    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";
    private const string DefaultInstanceType = "t3.micro";
    private const string DefaultSshUser = "ubuntu";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PublicIpTimeout = TimeSpan.FromMinutes(5);

    private readonly IEc2ApiClient _api;
    private readonly ISshCommandRunner _ssh;

    public Ec2Provisioner(IEc2ApiClient api, ISshCommandRunner ssh)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "AWS EC2 (VPC security group + instance, cloud-init)";

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Connection.ProviderOptions;
        string amiId = options.GetValueOrDefault("amiId") ?? throw new ArgumentException("ProviderOptions['amiId'] is required for AwsEc2 (AMI ids are region-specific).");
        string keyName = options.GetValueOrDefault("keyName") ?? throw new ArgumentException("ProviderOptions['keyName'] is required for AwsEc2.");
        string sshUser = options.GetValueOrDefault("sshUser", DefaultSshUser);
        string instanceType = request.Size ?? options.GetValueOrDefault("instanceType", DefaultInstanceType);
        string rendezvousImage = options.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);

        log.Info($"Creating a security group for {request.ServerName} (AwsEc2).");
        string groupId = await _api.CreateSecurityGroupAsync($"mp-host-{request.ServerName}", options.GetValueOrDefault("vpcId"), ct);

        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        string bootstrap = CloudInitRenderer.RenderBootstrapScript(request.GhcrRegistry, request.GhcrUsername);
        string cloudConfig = CloudInitRenderer.RenderCloudConfig(sshUser, compose, bootstrap, request.GhcrToken);
        string userData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cloudConfig));

        log.Info($"Launching a {instanceType} EC2 instance for {request.ServerName} from {amiId}.");
        Ec2Instance instance = await _api.RunInstanceAsync(
            new Ec2RunInstanceRequest(request.ServerName, amiId, instanceType, keyName, groupId, options.GetValueOrDefault("subnetId"), userData), ct);

        instance = await WaitForPublicIpAsync(instance, log, ct);
        var mappings = request.Manifest.Ports.ToDictionary(p => p.Port, p => p.Port);
        return new ProvisionResult(CloudVmServerId.Combine(instance.InstanceId, groupId), instance.PublicIp ?? "", mappings);
    }

    /// <inheritdoc />
    /// <remarks>
    /// mp-host created this security group with no rules at all, so opening the firewall here is a
    /// hard requirement (unlike GenericVps's best-effort <c>ufw</c>): without it, nothing -- not
    /// even mp-host's own later SSH calls -- can reach the box.
    /// </remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, groupId) = CloudVmServerId.Split(serverId);
        var rules = new List<Ec2IngressRule> { new(22, "tcp"), new(8080, "tcp") };
        rules.AddRange(request.Manifest.Ports.Where(p => !p.IsHealthPort).Select(p => new Ec2IngressRule(p.Port, p.Protocol)));
        log.Info($"Authorizing ingress on {groupId} for {string.Join(", ", rules.Select(r => $"{r.Port}/{r.Protocol}"))}.");
        await _api.AuthorizeIngressAsync(groupId, rules, ct);
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
        var (instanceId, _) = CloudVmServerId.Split(serverId);
        Ec2Instance instance = await _api.DescribeInstanceAsync(instanceId, ct);
        ServerState cloudState = MapState(instance.State);
        if (cloudState != ServerState.Running || instance.PublicIp is null)
        {
            return new ServerStatus(serverId, cloudState, instance.PublicIp, $"ec2State={instance.State}");
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
    /// <remarks>An EC2 instance persists across an image swap, unlike RunPod's pods: this is an in-place rolling restart over SSH, same as GenericVps.</remarks>
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
        var (instanceId, groupId) = CloudVmServerId.Split(serverId);
        SshTarget? target = await ResolveSshTargetAsync(serverId, connection, ct);
        if (target is not null)
        {
            await CloudVmSshOperations.TryComposeDownAsync(_ssh, target, log, ct);
        }

        log.Info($"Terminating EC2 instance {instanceId}.");
        await _api.TerminateInstanceAsync(instanceId, ct);

        if (!string.IsNullOrEmpty(groupId))
        {
            try
            {
                await _api.DeleteSecurityGroupAsync(groupId, ct);
            }
            catch (Exception ex)
            {
                log.Warn($"Could not delete security group {groupId} yet ({ex.Message}); EC2 often needs the instance to finish terminating first -- delete it yourself once it has.");
            }
        }
    }

    private async Task<Ec2Instance> WaitForPublicIpAsync(Ec2Instance instance, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + PublicIpTimeout;
        while (instance.PublicIp is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct);
            instance = await _api.DescribeInstanceAsync(instance.InstanceId, ct);
        }

        if (instance.PublicIp is null)
        {
            log.Warn($"{instance.InstanceId} did not get a public IP within {PublicIpTimeout}.");
        }

        return instance;
    }

    private async Task<SshTarget?> ResolveSshTargetAsync(string serverId, ProviderConnection connection, CancellationToken ct)
    {
        var (instanceId, _) = CloudVmServerId.Split(serverId);
        Ec2Instance instance = await _api.DescribeInstanceAsync(instanceId, ct);
        if (instance.PublicIp is null) return null;
        var (user, keyPath) = ResolveSsh(connection);
        return new SshTarget(instance.PublicIp, 22, user, keyPath, null);
    }

    private static (string User, string KeyPath) ResolveSsh(ProviderConnection connection)
    {
        string user = connection.ProviderOptions.GetValueOrDefault("sshUser", DefaultSshUser);
        string keyPath = connection.ProviderOptions.GetValueOrDefault("sshKeyPath")
            ?? throw new ArgumentException("ProviderOptions['sshKeyPath'] is required for AwsEc2.");
        return (user, keyPath);
    }

    private static ServerState MapState(string ec2State) => ec2State switch
    {
        "pending" => ServerState.Provisioning,
        "running" => ServerState.Running,
        "stopping" or "stopped" => ServerState.Stopped,
        "shutting-down" or "terminated" => ServerState.Gone,
        _ => ServerState.Unknown,
    };
}
