using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

/// <summary>
/// <see cref="IProvisioner"/> for <c>RunPod</c>: the UM-T23 WebSocket-transport profile. One pod
/// runs the pushed GHCR image directly (no compose file, no rendezvous side-car); RunPod maps the
/// declared TCP port to a public one at creation, so "open the firewall" is folded into
/// provisioning and is a documented no-op here, not a second call. The game still needs a
/// reachable rendezvous service (one deployed separately, e.g. via GenericVps) -- its URL and
/// secret travel in <see cref="ProvisionRequest.EnvironmentVariables"/> like any other profile.
///
/// <para>The server id this provisioner hands back is the RunPod pod id.</para>
/// </summary>
public sealed class RunPodProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "RunPod";

    private const int DefaultContainerDiskGb = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IRunPodApiClient _api;

    public RunPodProvisioner(IRunPodApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "RunPod (WebSocket, mapped TCP port)";

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tcpPorts = request.Manifest.Ports.Select(p => p.Port).ToList();
        log.Info($"Creating a RunPod pod for {request.ServerName} from {request.ImageReference} (ports {string.Join(", ", tcpPorts)}/tcp).");
        var created = await _api.CreatePodAsync(
            new RunPodCreatePodRequest(request.ServerName, request.ImageReference, request.EnvironmentVariables, tcpPorts, DefaultContainerDiskGb), ct);

        var mappings = created.PortMappings.ToDictionary(m => m.ContainerPort, m => m.PublicPort);
        foreach (var port in tcpPorts.Where(p => !mappings.ContainsKey(p)))
        {
            mappings[port] = port; // not yet reported by RunPod; filled in once the pod finishes starting.
        }

        return new ProvisionResult(created.Id, created.PublicIp ?? "", mappings);
    }

    /// <inheritdoc />
    /// <remarks>RunPod exposes the declared ports at pod creation; there is no separate firewall rule to open.</remarks>
    public Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        log.Info("RunPod maps ports at pod creation; no separate firewall step is needed.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            RunPodPod pod = await _api.GetPodAsync(serverId, ct);
            if (string.Equals(pod.RuntimeStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                log.Info($"Pod {serverId} is running.");
                return true;
            }

            await Task.Delay(PollInterval, ct);
        }

        log.Warn($"Pod {serverId} did not reach RUNNING within {timeout}.");
        return false;
    }

    /// <inheritdoc />
    public async Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        RunPodPod pod = await _api.GetPodAsync(serverId, ct);
        ServerState state = pod.RuntimeStatus switch
        {
            "RUNNING" => ServerState.Running,
            "EXITED" or "TERMINATED" => ServerState.Gone,
            null => ServerState.Provisioning,
            _ => ServerState.Unknown,
        };
        return new ServerStatus(serverId, state, pod.PublicIp, $"desiredStatus={pod.DesiredStatus}, runtimeStatus={pod.RuntimeStatus}");
    }

    /// <inheritdoc />
    public Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct) =>
        _api.GetPodLogsAsync(serverId, tailLines, ct);

    /// <inheritdoc />
    /// <remarks>
    /// RunPod pods are immutable once created: there is no in-place container-image swap, so a
    /// "rolling restart" here is terminate-the-old-pod-then-create-a-new-one. Players on the old
    /// pod are dropped during the swap (unlike GenericVps's in-place compose restart); the provider
    /// doc page calls this out.
    /// </remarks>
    public async Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        log.Info($"Replacing pod {serverId} with a new pod running {request.ImageReference} (RunPod has no in-place image swap).");
        await _api.TerminatePodAsync(serverId, ct);
        await ProvisionAsync(request, log, ct);
    }

    /// <inheritdoc />
    public Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        log.Info($"Terminating pod {serverId}.");
        return _api.TerminatePodAsync(serverId, ct);
    }
}
