using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host credentials set-ghcr --username &lt;u&gt; --token &lt;pat&gt;</c>,
/// <c>mp-host credentials set-runpod --key &lt;apikey&gt;</c>,
/// <c>mp-host credentials set-ssh --name &lt;server&gt; --host &lt;h&gt; [--port &lt;p&gt;] --user &lt;u&gt; --key &lt;path&gt; [--passphrase &lt;p&gt;]</c>,
/// <c>mp-host credentials set-&lt;hetzner|vultr|digitalocean|linode&gt; --token &lt;apikey&gt; --ssh-user &lt;u&gt; --ssh-key &lt;private key path&gt; --ssh-public-key &lt;public key path&gt;</c>,
/// <c>mp-host credentials set-ovh --endpoint &lt;ovh-eu|ovh-us|ovh-ca|...&gt; --service-name &lt;public cloud project id&gt; --app-key &lt;k&gt; --app-secret &lt;s&gt; --consumer-key &lt;c&gt; --ssh-user &lt;u&gt; --ssh-key &lt;private key path&gt; --ssh-public-key &lt;public key path&gt;</c>.
///
/// The one place a secret is ever typed at mp-host: it's written straight into
/// <see cref="HostingCredentialStore"/> (<c>%APPDATA%\MindAttic\Hosting\</c>) and nowhere in the
/// repo, matching every other command's "read from the vault, never the command line" contract. A
/// <c>--ssh-public-key</c> path is read once here and its text stored in the vault, since the UM-T26
/// cloud providers need the key's own text (not just its file path) to register it with their API.
/// </summary>
public static class CredentialsCommand
{
    public static Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials)
    {
        if (rawArgs.Count < 2)
        {
            log.Error("Usage: mp-host credentials <set-ghcr|set-runpod|set-ssh|set-hetzner|set-vultr|set-digitalocean|set-linode|set-ovh> ...");
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

            case "set-hetzner":
                credentials.SetHetznerCreds(ReadCloudVpsCreds(args));
                log.Info("Hetzner Cloud credential stored.");
                return Task.FromResult(0);

            case "set-vultr":
                credentials.SetVultrCreds(ReadCloudVpsCreds(args));
                log.Info("Vultr credential stored.");
                return Task.FromResult(0);

            case "set-digitalocean":
                credentials.SetDigitalOceanCreds(ReadCloudVpsCreds(args));
                log.Info("DigitalOcean credential stored.");
                return Task.FromResult(0);

            case "set-linode":
                credentials.SetLinodeCreds(ReadCloudVpsCreds(args));
                log.Info("Linode credential stored.");
                return Task.FromResult(0);

            case "set-ovh":
                credentials.SetOvhCreds(new HostingCredentialStore.OvhCreds(
                    args.Require("endpoint"), args.Require("service-name"), args.Require("app-key"), args.Require("app-secret"), args.Require("consumer-key"),
                    args.GetOrDefault("ssh-user", "root"), args.Require("ssh-key"), ReadPublicKey(args)));
                log.Info("OVHcloud credential stored.");
                return Task.FromResult(0);

            default:
                log.Error($"Unknown credentials subcommand '{rawArgs[1]}'.");
                return Task.FromResult(1);
        }
    }

    private static HostingCredentialStore.CloudVpsCreds ReadCloudVpsCreds(ArgumentReader args) =>
        new(args.Require("token"), args.GetOrDefault("ssh-user", "root"), args.Require("ssh-key"), ReadPublicKey(args));

    private static string ReadPublicKey(ArgumentReader args) => File.ReadAllText(args.Require("ssh-public-key")).Trim();
}
