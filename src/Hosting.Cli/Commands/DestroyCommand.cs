using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Composition;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Provisioning;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host destroy --profile &lt;p&gt; --name &lt;server&gt; --server-id &lt;id&gt; --yes [--dry-run]</c>.
/// Scriptable, not interactive: refuses (without touching anything) unless <c>--yes</c> is given,
/// rather than blocking on a stdin confirmation a CI run would hang on.
/// </summary>
public static class DestroyCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct)
    {
        ArgumentReader args = new(rawArgs, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dry-run", "yes" });
        string profile = args.Require("profile");
        string serverName = args.Require("name");
        string serverId = args.Require("server-id");

        if (!args.Has("yes"))
        {
            log.Error($"Refusing to destroy {serverId} without --yes.");
            return 1;
        }

        var composer = new HostingComposer(credentials, args.Has("dry-run"));
        IProvisioner provisioner = composer.BuildProvisioner(profile, log);
        ProviderConnection connection = composer.BuildConnection(profile, serverName);

        await provisioner.DestroyAsync(serverId, connection, log, ct);
        log.Info($"{serverId} destroyed.");
        return 0;
    }
}
