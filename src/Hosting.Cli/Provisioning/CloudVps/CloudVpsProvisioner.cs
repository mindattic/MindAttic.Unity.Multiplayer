using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

/// <summary>
/// <see cref="IProvisioner"/> shared by every UM-T26 plain-VPS cloud profile (Hetzner, Vultr,
/// DigitalOcean, Linode, OVHcloud): create an Ubuntu server from the provider's REST API with
/// <see cref="CloudInitRenderer.RenderUserData"/> as its cloud-init user-data (so the container
/// plus rendezvous come up on first boot with no SSH call needed), open the declared ports in the
/// provider's firewall, then fall back to SSH -- the same mechanism <c>GenericVpsProvisioner</c>
/// uses -- for the health probe, logs, and a rolling restart, once the box has a public IP.
///
/// <para>One instance is shared logic; <c>Provisioning/&lt;Provider&gt;/&lt;Provider&gt;Provisioner</c>
/// is a thin subclass that only supplies its own <see cref="ICloudVpsApiClient"/> and
/// <see cref="CloudVpsProviderSpec"/>, matching <c>GenericVpsProvisioner</c>/<c>RunPodProvisioner</c>'s
/// <c>Id</c> convention for <see cref="HostingComposer"/> and the CLI's <c>--profile</c> flag.</para>
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys: <c>sshPublicKey</c> (the OpenSSH
/// public key text registered with the provider and baked into the new server's cloud-init --
/// required), <c>sshUser</c> (defaults to the spec's <see cref="CloudVpsProviderSpec.DefaultSshUser"/>),
/// <c>sshKeyPath</c> (the matching private key, required for the SSH fallback calls),
/// <c>sshPort</c> (defaults to 22), <c>rendezvousImage</c> (defaults to
/// <c>ghcr.io/mindattic/rendezvous:latest</c>). <see cref="ProviderConnection.ProviderSecrets"/>
/// key: <c>sshPassphrase</c> (optional). Provider API authentication never travels through
/// <see cref="ProviderConnection"/> -- it is baked into the injected <see cref="ICloudVpsApiClient"/>
/// by <see cref="HostingComposer"/> from the vault, the same split <c>RunPodProvisioner</c> uses.</para>
///
/// <para>The server id this provisioner hands back and expects on every later call is the
/// provider's own server/instance id.</para>
/// </summary>
public class CloudVpsProvisioner : IProvisioner
{
    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";

    private readonly ICloudVpsApiClient _api;
    private readonly ISshCommandRunner _ssh;
    private readonly CloudVpsProviderSpec _spec;

    public CloudVpsProvisioner(ICloudVpsApiClient api, ISshCommandRunner ssh, CloudVpsProviderSpec spec)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    /// <inheritdoc />
    public string ProviderId => _spec.ProviderId;

    /// <inheritdoc />
    public string DisplayName => _spec.DisplayName;

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string region = request.Region ?? _spec.DefaultRegion;
        string size = request.Size ?? _spec.DefaultSize;
        string sshPublicKey = RequirePublicKey(request.Connection);
        string rendezvousImage = request.Connection.ProviderOptions.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);

        log.Info($"Creating a {_spec.DisplayName} server for {request.ServerName} in {region} ({size}) from {_spec.ImageSlug}.");
        string userData = CloudInitRenderer.RenderUserData(
            request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables,
            request.GhcrRegistry, request.GhcrUsername, request.GhcrToken, sshPublicKey);

        string sshKeyId = await _api.EnsureSshKeyAsync($"mp-host-{request.ServerName}", sshPublicKey, ct);
        CloudVpsServer server = await _api.CreateServerAsync(
            new CloudVpsCreateRequest(request.ServerName, region, size, _spec.ImageSlug, sshKeyId, userData), ct);

        var mappings = request.Manifest.Ports.ToDictionary(p => p.Port, p => p.Port);
        return new ProvisionResult(server.Id, server.PublicIp ?? "", mappings);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unlike a bring-your-own GenericVps box, mp-host created this server's security group/firewall
    /// itself, so opening it is a hard requirement here, not a best-effort <c>ufw</c> call: the game
    /// port (its own protocol) plus the rendezvous service's TCP port, since both must be reachable
    /// from the internet for players to connect and resolve join codes.
    /// </remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rules = request.Manifest.Ports.Where(p => !p.IsHealthPort)
            .Select(p => new CloudVpsFirewallRule(p.Port, p.Protocol))
            .Append(new CloudVpsFirewallRule(CloudInitRenderer.RendezvousPort, "tcp"))
            .ToList();
        log.Info($"Opening {string.Join(", ", rules.Select(r => $"{r.Port}/{r.Protocol}"))} on {_spec.DisplayName}'s firewall for {serverId}.");
        await _api.EnsureFirewallAsync(serverId, rules, ct);
    }

    /// <inheritdoc />
    public async Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var health = request.Manifest.HealthPort;
        if (health is null)
        {
            log.Info("No health port declared; skipping the health check.");
            return true;
        }

        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            CloudVpsServer server = await _api.GetServerAsync(serverId, ct);
            if (!string.IsNullOrEmpty(server.PublicIp))
            {
                SshTarget target = ResolveTarget(request.Connection, server.PublicIp);
                // Bound to 127.0.0.1 in the compose file (HOSTING.txt), so the probe runs on the box itself.
                SshCommandResult probe = await _ssh.RunAsync(target,
                    $"curl -sf -o /dev/null -w '%{{http_code}}' http://127.0.0.1:{health.Port}{health.Path ?? "/healthz"}", ct);
                if (probe.Succeeded && probe.StandardOutput.Trim() == "200")
                {
                    log.Info($"{server.PublicIp} is healthy.");
                    return true;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        log.Warn($"{serverId} did not report healthy within {timeout}.");
        return false;
    }

    /// <inheritdoc />
    public async Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        CloudVpsServer server = await _api.GetServerAsync(serverId, ct);
        ServerState state = server.Status.ToLowerInvariant() switch
        {
            "running" or "active" or "booted" => ServerState.Running,
            "off" or "stopped" or "shutoff" or "offline" => ServerState.Stopped,
            "" or "deleted" or "archived" or "terminated" => ServerState.Gone,
            "initializing" or "pending" or "provisioning" or "build" or "new" => ServerState.Provisioning,
            _ => ServerState.Unknown,
        };
        return new ServerStatus(serverId, state, server.PublicIp, $"status={server.Status}");
    }

    /// <inheritdoc />
    public async Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        SshTarget target = await ResolveTargetFromServerAsync(serverId, connection, ct);
        SshCommandResult result = await _ssh.RunAsync(target,
            $"docker compose -f {CloudInitRenderer.ComposeFileName} logs --no-color --tail {tailLines} game", ct);
        return result.StandardOutput;
    }

    /// <inheritdoc />
    /// <remarks>An in-place rolling restart over SSH, exactly like <c>GenericVpsProvisioner</c> -- the server mp-host created is as reachable as any bring-your-own box once it has booted.</remarks>
    public async Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SshTarget target = await ResolveTargetFromServerAsync(serverId, request.Connection, ct);
        log.Info($"Rolling {target.Host} onto {request.ImageReference}.");
        string rendezvousImage = request.Connection.ProviderOptions.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);
        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        await _ssh.UploadTextAsync(target, CloudInitRenderer.ComposeFileName, compose, ct);
        SshCommandResult result = await _ssh.RunAsync(target,
            $"docker compose -f {CloudInitRenderer.ComposeFileName} pull && docker compose -f {CloudInitRenderer.ComposeFileName} up -d", ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Rolling restart on {target.Host} failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    /// <inheritdoc />
    public async Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        log.Info($"Destroying {_spec.DisplayName} server {serverId}.");
        await _api.DestroyServerAsync(serverId, ct);
    }

    private async Task<SshTarget> ResolveTargetFromServerAsync(string serverId, ProviderConnection connection, CancellationToken ct)
    {
        CloudVpsServer server = await _api.GetServerAsync(serverId, ct);
        if (string.IsNullOrEmpty(server.PublicIp))
        {
            throw new InvalidOperationException($"{_spec.DisplayName} server {serverId} has no public IP yet.");
        }

        return ResolveTarget(connection, server.PublicIp);
    }

    private SshTarget ResolveTarget(ProviderConnection connection, string host)
    {
        int port = int.TryParse(connection.ProviderOptions.GetValueOrDefault("sshPort"), out int p) ? p : 22;
        string user = connection.ProviderOptions.GetValueOrDefault("sshUser", _spec.DefaultSshUser);
        string keyPath = connection.ProviderOptions.GetValueOrDefault("sshKeyPath")
            ?? throw new ArgumentException($"ProviderOptions['sshKeyPath'] is required for {_spec.ProviderId}.");
        string? passphrase = connection.ProviderSecrets.GetValueOrDefault("sshPassphrase");
        return new SshTarget(host, port, user, keyPath, string.IsNullOrEmpty(passphrase) ? null : passphrase);
    }

    private static string RequirePublicKey(ProviderConnection connection) =>
        connection.ProviderOptions.GetValueOrDefault("sshPublicKey")
            ?? throw new ArgumentException("ProviderOptions['sshPublicKey'] is required to register an SSH key with the provider.");
}
