namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

/// <summary>
/// <see cref="GameLiftProvisioner"/> hands back one opaque <c>serverId</c> string packing the fleet,
/// alias and build ids a later <c>status</c>/<c>logs</c>/<c>update</c>/<c>destroy</c> call needs back
/// -- the same idea as <see cref="CloudVm.CloudVmServerId"/>, with a third id because GameLift's
/// cleanup must also delete the build, not just the fleet and its alias.
/// </summary>
public static class GameLiftServerId
{
    private const char Separator = '|';

    /// <summary>Packs a fleet id, its alias id and the build id it runs into one serverId.</summary>
    public static string Combine(string fleetId, string aliasId, string buildId) => $"{fleetId}{Separator}{aliasId}{Separator}{buildId}";

    /// <summary>Unpacks a serverId produced by <see cref="Combine"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="serverId"/> was not produced by <see cref="Combine"/>.</exception>
    public static (string FleetId, string AliasId, string BuildId) Split(string serverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        string[] parts = serverId.Split(Separator);
        if (parts.Length != 3)
        {
            throw new ArgumentException($"'{serverId}' is not a serverId GameLiftProvisioner produced (expected 'fleetId{Separator}aliasId{Separator}buildId').");
        }

        return (parts[0], parts[1], parts[2]);
    }
}
