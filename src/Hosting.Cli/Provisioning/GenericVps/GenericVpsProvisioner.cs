using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

/// <summary>
/// <see cref="IProvisioner"/> for <c>GenericVps</c> -- the UM-T23 default profile: a Docker image
/// plus the rendezvous service on any UDP-capable VPS the operator already has (Hetzner, Vultr,
/// DigitalOcean, OVH, a home box, ...). There is no cloud API to "create" the box; provisioning
/// means configuring an already-running one over SSH with the bootstrap script + compose file
/// <see cref="CloudInitRenderer"/> renders.
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys: <c>sshHost</c>, <c>sshPort</c>,
/// <c>sshUser</c>, <c>sshKeyPath</c>, <c>rendezvousImage</c> (defaults to
/// <c>ghcr.io/mindattic/rendezvous:latest</c>). <see cref="ProviderConnection.ProviderSecrets"/>
/// key: <c>sshPassphrase</c> (optional).</para>
///
/// <para>The server id this provisioner hands back and expects on every later call is
/// <c>sshUser@sshHost:sshPort</c> -- there is nothing else to identify a bring-your-own box by.</para>
/// </summary>
public sealed class GenericVpsProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "GenericVps";

    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";
    private const string RemoteComposePath = "docker-compose.yml";
    private const string RemoteTokenPath = "/tmp/mp-host-ghcr-token";
    private const string RemoteBootstrapPath = "/tmp/mp-host-bootstrap.sh";

    private readonly ISshCommandRunner _ssh;

    public GenericVpsProvisioner(ISshCommandRunner ssh)
    {
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "Generic VPS (Docker + rendezvous over SSH)";

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SshTarget target = ResolveTarget(request.Connection);
        string rendezvousImage = request.Connection.ProviderOptions.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);

        log.Info($"Provisioning {request.ServerName} on {target.User}@{target.Host}:{target.Port} (GenericVps).");
        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        await _ssh.UploadTextAsync(target, RemoteComposePath, compose, ct);
        await _ssh.UploadTextAsync(target, RemoteTokenPath, request.GhcrToken, ct);

        string bootstrap = CloudInitRenderer.RenderBootstrapScript(request.GhcrRegistry, request.GhcrUsername);
        await _ssh.UploadTextAsync(target, RemoteBootstrapPath, bootstrap, ct);

        SshCommandResult result = await _ssh.RunAsync(target, $"chmod +x {RemoteBootstrapPath} && sh {RemoteBootstrapPath}", ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Bootstrapping {target.Host} failed (exit {result.ExitCode}): {result.StandardError}");
        }

        var mappings = request.Manifest.Ports.ToDictionary(p => p.Port, p => p.Port);
        return new ProvisionResult(ServerIdFor(target), target.Host, mappings);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A bring-your-own box's own firewall (ufw/iptables/the hosting panel) is the operator's; this
    /// does a best-effort <c>ufw allow</c> per declared non-health port and only warns on failure
    /// (no ufw, or the operator manages the firewall some other way) rather than failing the
    /// pipeline -- UM-T26/UM-T27's cloud providers are where "open the firewall" is a hard
    /// requirement because mp-host created the security group itself.
    /// </remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SshTarget target = ResolveTarget(request.Connection);
        foreach (var port in request.Manifest.Ports.Where(p => !p.IsHealthPort))
        {
            SshCommandResult result = await _ssh.RunAsync(target, $"command -v ufw >/dev/null 2>&1 && ufw allow {port.Port}/{port.Protocol} || true", ct);
            if (!result.Succeeded)
            {
                log.Warn($"Could not confirm firewall rule for {port.Port}/{port.Protocol} on {target.Host}; open it yourself if this box has a firewall.");
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SshTarget target = ResolveTarget(request.Connection);
        var health = request.Manifest.HealthPort;
        if (health is null)
        {
            log.Info("No health port declared; skipping the health check.");
            return true;
        }

        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // Bound to 127.0.0.1 in the compose file (HOSTING.txt), so the probe runs on the box itself.
            SshCommandResult probe = await _ssh.RunAsync(target,
                $"curl -sf -o /dev/null -w '%{{http_code}}' http://127.0.0.1:{health.Port}{health.Path ?? "/healthz"}", ct);
            if (probe.Succeeded && probe.StandardOutput.Trim() == "200")
            {
                log.Info($"{target.Host} is healthy.");
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        log.Warn($"{target.Host} did not report healthy within {timeout}.");
        return false;
    }

    /// <inheritdoc />
    public async Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        SshTarget target = ResolveTarget(connection);
        SshCommandResult result = await _ssh.RunAsync(target, $"docker compose -f {RemoteComposePath} ps --status running --services", ct);
        if (!result.Succeeded)
        {
            return new ServerStatus(serverId, ServerState.Unknown, target.Host, result.StandardError);
        }

        bool gameRunning = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.Equals("game", StringComparison.OrdinalIgnoreCase));
        return new ServerStatus(serverId, gameRunning ? ServerState.Running : ServerState.Stopped, target.Host, result.StandardOutput.Trim());
    }

    /// <inheritdoc />
    public async Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        SshTarget target = ResolveTarget(connection);
        SshCommandResult result = await _ssh.RunAsync(target, $"docker compose -f {RemoteComposePath} logs --no-color --tail {tailLines} game", ct);
        return result.StandardOutput;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        SshTarget target = ResolveTarget(request.Connection);
        log.Info($"Rolling {target.Host} onto {request.ImageReference}.");
        string rendezvousImage = request.Connection.ProviderOptions.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);
        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        await _ssh.UploadTextAsync(target, RemoteComposePath, compose, ct);
        SshCommandResult result = await _ssh.RunAsync(target,
            $"docker compose -f {RemoteComposePath} pull && docker compose -f {RemoteComposePath} up -d", ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Rolling restart on {target.Host} failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    /// <inheritdoc />
    public async Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        SshTarget target = ResolveTarget(connection);
        log.Info($"Tearing down the compose stack on {target.Host} (the box itself is yours to keep or return).");
        await _ssh.RunAsync(target, $"docker compose -f {RemoteComposePath} down -v", ct);
    }

    private static SshTarget ResolveTarget(ProviderConnection connection)
    {
        string host = connection.ProviderOptions.GetValueOrDefault("sshHost")
            ?? throw new ArgumentException("ProviderOptions['sshHost'] is required for GenericVps.");
        int port = int.TryParse(connection.ProviderOptions.GetValueOrDefault("sshPort"), out int p) ? p : 22;
        string user = connection.ProviderOptions.GetValueOrDefault("sshUser", "root");
        string keyPath = connection.ProviderOptions.GetValueOrDefault("sshKeyPath")
            ?? throw new ArgumentException("ProviderOptions['sshKeyPath'] is required for GenericVps.");
        string? passphrase = connection.ProviderSecrets.GetValueOrDefault("sshPassphrase");
        return new SshTarget(host, port, user, keyPath, string.IsNullOrEmpty(passphrase) ? null : passphrase);
    }

    private static string ServerIdFor(SshTarget target) => $"{target.User}@{target.Host}:{target.Port}";
}
