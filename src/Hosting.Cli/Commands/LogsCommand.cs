using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Composition;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Provisioning;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary><c>mp-host logs --profile &lt;p&gt; --name &lt;server&gt; --server-id &lt;id&gt; [--tail &lt;n&gt;] [--dry-run]</c></summary>
public static class LogsCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct)
    {
        ArgumentReader args = new(rawArgs, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dry-run" });
        string profile = args.Require("profile");
        string serverName = args.Require("name");
        string serverId = args.Require("server-id");
        int tail = args.GetInt("tail", 200);

        var composer = new HostingComposer(credentials, args.Has("dry-run"));
        IProvisioner provisioner = composer.BuildProvisioner(profile, log);
        ProviderConnection connection = composer.BuildConnection(profile, serverName, log);

        string logs = await provisioner.GetLogsAsync(serverId, tail, connection, log, ct);
        log.Info(logs);
        return 0;
    }
}
