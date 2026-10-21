namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>What a UPnP port-mapping attempt found.</summary>
/// <param name="Success">True when the router accepted the mapping.</param>
/// <param name="ExternalIp">The router's external (internet-facing) IP, when it answered <c>GetExternalIPAddress</c>; null otherwise.</param>
/// <param name="FailureReason">
/// Why it did not succeed, for the operator -- "no UPnP gateway answered the discovery broadcast
/// within the timeout" (most HOA/managed networks: UPnP is simply off) or the router's own SOAP
/// fault text (e.g. <c>Action Not Authorized</c>: UPnP is on but port forwarding is blocked by
/// policy). Null when <see cref="Success"/> is true.
/// </param>
public sealed record UpnpMappingResult(bool Success, string? ExternalIp, string? FailureReason);

/// <summary>
/// Best-effort UPnP IGD (Internet Gateway Device) port mapping for <c>mp-host local up/down</c>
/// (UM-T31): ask the LAN's router to forward an external port to this machine, so a listen/dedicated
/// server run from home can be reached from outside the LAN without the operator opening their
/// router's admin page. Never required -- RFC 0001 is explicit that an HOA-controlled network simply
/// refuses this, and that is an expected, clearly-reported outcome, not an error: the owner's own
/// network is exactly this case, so <see cref="Commands.LocalCommand"/> always falls back to
/// "LAN join by IP only, use Relay for a listen server reachable off-LAN" when it fails.
/// </summary>
public interface IUpnpClient
{
    /// <summary>Discovers an IGD on the LAN and asks it to forward <paramref name="externalPort"/> to this machine's <paramref name="internalPort"/>.</summary>
    Task<UpnpMappingResult> TryAddPortMappingAsync(int externalPort, int internalPort, string protocol, string description, CancellationToken ct);

    /// <summary>Best-effort removal of a mapping <see cref="TryAddPortMappingAsync"/> added. Failures are swallowed -- the mapping simply times out on its own otherwise.</summary>
    Task TryRemovePortMappingAsync(int externalPort, string protocol, CancellationToken ct);
}
