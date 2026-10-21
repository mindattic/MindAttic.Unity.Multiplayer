using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVm;

/// <summary>
/// The post-boot half of every big-cloud VM provisioner (AWS EC2/Lightsail, GCE, Azure VM): once
/// cloud-init/the startup script has installed Docker and brought the compose stack up, status,
/// logs, health and a rolling update look exactly like <see cref="GenericVpsProvisioner"/>'s --
/// the cloud API only ever owns the VM and its firewall, never the container inside it. Shared here
/// so none of the four UM-T27 provisioners reimplement the same SSH commands.
/// </summary>
public static class CloudVmSshOperations
{
    /// <summary>Polls <c>GET http://127.0.0.1:&lt;healthPort&gt;/healthz</c> on the box over SSH, same as GenericVps.</summary>
    public static async Task<bool> WaitForHealthyAsync(ISshCommandRunner ssh, SshTarget target, Packaging.ServerPackageManifest manifest, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        var health = manifest.HealthPort;
        if (health is null)
        {
            log.Info("No health port declared; skipping the health check.");
            return true;
        }

        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            SshCommandResult probe = await ssh.RunAsync(target,
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

    /// <summary>Runs <c>docker compose ps</c> over SSH to tell Running from Stopped, same as GenericVps.</summary>
    public static async Task<ServerStatus> GetComposeStatusAsync(ISshCommandRunner ssh, SshTarget target, string serverId, IDeployLog log, CancellationToken ct)
    {
        SshCommandResult result = await ssh.RunAsync(target, $"docker compose -f {CloudInitRenderer.ComposeFileName} ps --status running --services", ct);
        if (!result.Succeeded)
        {
            return new ServerStatus(serverId, ServerState.Unknown, target.Host, result.StandardError);
        }

        bool gameRunning = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.Equals("game", StringComparison.OrdinalIgnoreCase));
        return new ServerStatus(serverId, gameRunning ? ServerState.Running : ServerState.Stopped, target.Host, result.StandardOutput.Trim());
    }

    /// <summary>Tails <c>docker compose logs game</c> over SSH, same as GenericVps.</summary>
    public static async Task<string> GetLogsAsync(ISshCommandRunner ssh, SshTarget target, int tailLines, CancellationToken ct)
    {
        SshCommandResult result = await ssh.RunAsync(target, $"docker compose -f {CloudInitRenderer.ComposeFileName} logs --no-color --tail {tailLines} game", ct);
        return result.StandardOutput;
    }

    /// <summary>
    /// Re-renders and re-uploads the compose file, then pulls and restarts in place over SSH --
    /// unlike RunPod's "terminate and recreate" update, a cloud VM's whole point is that it persists
    /// across an image swap, the same way a GenericVps box does.
    /// </summary>
    public static async Task UpdateAsync(ISshCommandRunner ssh, SshTarget target, ProvisionRequest request, string rendezvousImage, IDeployLog log, CancellationToken ct)
    {
        log.Info($"Rolling {target.Host} onto {request.ImageReference}.");
        string compose = CloudInitRenderer.RenderCompose(request.Manifest, request.ImageReference, rendezvousImage, request.EnvironmentVariables);
        await ssh.UploadTextAsync(target, CloudInitRenderer.ComposeFileName, compose, ct);
        SshCommandResult result = await ssh.RunAsync(target,
            $"docker compose -f {CloudInitRenderer.ComposeFileName} pull && docker compose -f {CloudInitRenderer.ComposeFileName} up -d", ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Rolling restart on {target.Host} failed (exit {result.ExitCode}): {result.StandardError}");
        }
    }

    /// <summary>
    /// Best-effort <c>docker compose down -v</c> before the cloud API terminates the VM underneath
    /// it. Failures (the VM is already gone, SSH times out, ...) are swallowed: the VM is being
    /// destroyed either way, so a clean container shutdown is a courtesy, not a requirement.
    /// </summary>
    public static async Task TryComposeDownAsync(ISshCommandRunner ssh, SshTarget target, IDeployLog log, CancellationToken ct)
    {
        try
        {
            await ssh.RunAsync(target, $"docker compose -f {CloudInitRenderer.ComposeFileName} down -v", ct);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not stop the compose stack on {target.Host} before destroying it ({ex.Message}); destroying the VM anyway.");
        }
    }
}
