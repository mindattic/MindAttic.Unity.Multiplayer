using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning;

/// <summary>
/// One hosting provider's half of the pipeline (steps 3-7): provision the host, open its
/// firewall, run the container plus rendezvous, health-check it, and answer status/logs/update/
/// destroy afterwards. A new provider is a new <see cref="IProvisioner"/> registered with
/// <see cref="ProvisionerRegistry"/>; nothing else in the pipeline changes.
/// </summary>
public interface IProvisioner
{
    /// <summary>The hosting profile id this provisioner serves (matches <c>server-package.json</c>'s <c>profile</c>, e.g. <c>GenericVps</c>, <c>RunPod</c>).</summary>
    string ProviderId { get; }

    /// <summary>A name for people.</summary>
    string DisplayName { get; }

    /// <summary>Creates (or reuses) the host and starts the container running the pushed image plus its environment.</summary>
    Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct);

    /// <summary>
    /// Opens the declared ports in the provider's firewall. Some providers (RunPod) have no
    /// separate firewall step -- ports are exposed at pod creation -- and implement this as a
    /// documented no-op.
    /// </summary>
    Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct);

    /// <summary>
    /// Polls until the server's health endpoint (or provider-equivalent) reports healthy, or
    /// <paramref name="timeout"/> elapses. Returns the final health observation either way.
    /// </summary>
    Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct);

    /// <summary>A point-in-time status report. Needs only a connection, not a manifest or image.</summary>
    Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct);

    /// <summary>The container's recent stdout/stderr (newest last), up to <paramref name="tailLines"/> lines.</summary>
    Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct);

    /// <summary>A rolling restart onto a newly pushed image.</summary>
    Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct);

    /// <summary>Tears the server down. Always safe to call on an already-gone server.</summary>
    Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct);
}
