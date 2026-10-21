using System.Text.RegularExpressions;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;

namespace Unity.Multiplayer.Hosting.Cli.Deploy;

/// <summary>
/// Step 7, "register with rendezvous -&gt; print join code": the server package self-registers
/// with rendezvous from inside the container (UM-T13's <c>NetSession</c>, driven by the
/// <c>MINDATTIC_RENDEZVOUS_*</c> environment mp-host set in step 3), and logs
/// <c>"[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ"</c> once it has. mp-host doesn't
/// re-implement registration; it watches the container's own logs (the same logs <c>mp-host
/// logs</c> shows) for that line.
/// </summary>
public static class JoinCodeWatcher
{
    private static readonly Regex JoinCodeLine = new(
        @"\[MindAttic\.Multiplayer\]\s*Join code:\s*(?<code>[A-Za-z0-9\-]+)",
        RegexOptions.Compiled);

    /// <summary>Polls <paramref name="provisioner"/>'s logs until the join-code line appears or <paramref name="timeout"/> elapses.</summary>
    public static async Task<string?> WaitForJoinCodeAsync(
        IProvisioner provisioner,
        string serverId,
        ProviderConnection connection,
        TimeSpan timeout,
        TimeSpan pollInterval,
        IDeployLog log,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provisioner);
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            string logs = await provisioner.GetLogsAsync(serverId, 200, connection, log, ct);
            Match match = JoinCodeLine.Matches(logs).LastOrDefault() ?? Match.Empty;
            if (match.Success)
            {
                return match.Groups["code"].Value;
            }

            await Task.Delay(pollInterval, ct);
        }

        log.Warn($"No join code seen in {serverId}'s logs within {timeout}; check 'mp-host logs' once it's finished starting.");
        return null;
    }
}
