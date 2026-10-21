namespace Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVm;

/// <summary>
/// Every big-cloud <see cref="IProvisioner"/> (UM-T27) hands back one opaque <c>serverId</c> string
/// that later <c>status</c>/<c>logs</c>/<c>update</c>/<c>destroy</c> calls pass straight back in,
/// the same way <see cref="GenericVps.GenericVpsProvisioner"/> packs <c>user@host:port</c> into its
/// own serverId -- there is nowhere else for mp-host to remember "which security group/firewall/NSG
/// did I create for this instance" between CLI invocations. <see cref="SecondaryId"/> is empty for
/// a provider with no separate firewall resource to track (AWS Lightsail: the firewall lives on the
/// instance itself).
/// </summary>
public static class CloudVmServerId
{
    private const char Separator = '|';

    /// <summary>Packs the cloud instance id and its associated firewall/security-group/NSG id (or "") into one serverId.</summary>
    public static string Combine(string instanceId, string secondaryId) => $"{instanceId}{Separator}{secondaryId}";

    /// <summary>Unpacks a serverId produced by <see cref="Combine"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="serverId"/> was not produced by <see cref="Combine"/>.</exception>
    public static (string InstanceId, string SecondaryId) Split(string serverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        int at = serverId.IndexOf(Separator);
        if (at < 0)
        {
            throw new ArgumentException($"'{serverId}' is not a serverId this provisioner produced (expected 'instanceId{Separator}secondaryId').");
        }

        return (serverId[..at], serverId[(at + 1)..]);
    }
}
