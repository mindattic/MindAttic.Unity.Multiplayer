using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host credentials set-ghcr --username &lt;u&gt; --token &lt;pat&gt;</c>,
/// <c>mp-host credentials set-runpod --key &lt;apikey&gt;</c>,
/// <c>mp-host credentials set-ssh --name &lt;server&gt; --host &lt;h&gt; [--port &lt;p&gt;] --user &lt;u&gt; --key &lt;path&gt; [--passphrase &lt;p&gt;]</c>.
///
/// The one place a secret is ever typed at mp-host: it's written straight into
/// <see cref="HostingCredentialStore"/> (<c>%APPDATA%\MindAttic\Hosting\</c>) and nowhere in the
/// repo, matching every other command's "read from the vault, never the command line" contract.
/// </summary>
public static class CredentialsCommand
{
    public static Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials)
    {
        if (rawArgs.Count < 2)
        {
            log.Error("Usage: mp-host credentials <set-ghcr|set-runpod|set-ssh> ...");
            return Task.FromResult(1);
        }

        ArgumentReader args = new(rawArgs, 2);
        switch (rawArgs[1])
        {
            case "set-ghcr":
                credentials.SetGhcrCreds(new HostingCredentialStore.GhcrCreds(args.Require("username"), args.Require("token")));
                log.Info("GHCR credential stored.");
                return Task.FromResult(0);

            case "set-runpod":
                credentials.SetRunPodCreds(args.Require("key"));
                log.Info("RunPod API key stored.");
                return Task.FromResult(0);

            case "set-ssh":
                string name = args.Require("name");
                int port = args.GetInt("port", 22);
                credentials.SetSshCreds(name, new HostingCredentialStore.SshCreds(
                    args.Require("host"), port, args.Require("user"), args.Require("key"), args.Get("passphrase")));
                log.Info($"SSH target for '{name}' stored.");
                return Task.FromResult(0);

            default:
                log.Error($"Unknown credentials subcommand '{rawArgs[1]}'.");
                return Task.FromResult(1);
        }
    }
}
