using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host credentials set-ghcr --username &lt;u&gt; --token &lt;pat&gt;</c>,
/// <c>mp-host credentials set-runpod --key &lt;apikey&gt;</c>,
/// <c>mp-host credentials set-ssh --name &lt;server&gt; --host &lt;h&gt; [--port &lt;p&gt;] --user &lt;u&gt; --key &lt;path&gt; [--passphrase &lt;p&gt;]</c>,
/// <c>mp-host credentials set-&lt;hetzner|vultr|digitalocean|linode&gt; --token &lt;apikey&gt; --ssh-user &lt;u&gt; --ssh-key &lt;private key path&gt; --ssh-public-key &lt;public key path&gt;</c>,
/// <c>mp-host credentials set-ovh --endpoint &lt;ovh-eu|ovh-us|ovh-ca|...&gt; --service-name &lt;public cloud project id&gt; --app-key &lt;k&gt; --app-secret &lt;s&gt; --consumer-key &lt;c&gt; --ssh-user &lt;u&gt; --ssh-key &lt;private key path&gt; --ssh-public-key &lt;public key path&gt;</c>,
/// <c>mp-host credentials set-aws --access-key-id &lt;id&gt; --secret-access-key &lt;key&gt; --region &lt;r&gt;</c>
/// (shared by <c>AwsEc2</c>, <c>AwsLightsail</c> and <c>GameLift</c>),
/// <c>mp-host credentials set-gce --project &lt;id&gt; --access-token &lt;token&gt;</c>,
/// <c>mp-host credentials set-azure --tenant &lt;id&gt; --client-id &lt;id&gt; --client-secret &lt;secret&gt; --subscription &lt;id&gt;</c>,
/// <c>mp-host credentials set-fly --token &lt;Fly API token&gt; --org &lt;org slug&gt;</c>,
/// <c>mp-host credentials set-oci --tenancy &lt;OCID&gt; --user &lt;OCID&gt; --fingerprint &lt;key fingerprint&gt; --key &lt;PEM private key path&gt; --region &lt;r&gt; --compartment &lt;OCID&gt;</c>,
/// <c>mp-host credentials set-cloud-options --profile &lt;AwsEc2|AwsLightsail|Gce|AzureVm|Fly|GameLift&gt; --name &lt;server&gt; --opt KEY=VALUE [--opt KEY=VALUE ...]</c>
/// (also how a Fly deploy overrides its defaults: <c>--opt rendezvousMode=app</c> or <c>--opt rendezvousImage=...</c>)
/// (the per-server launch options each UM-T27 provisioner's class doc lists, e.g. <c>sshUser</c>,
/// <c>sshKeyPath</c>, <c>sshPublicKeyPath</c>, <c>amiId</c>, <c>keyName</c>, <c>resourceGroup</c>,
/// <c>subnetId</c> -- none of them secret, but none safe to default blindly either),
/// <c>mp-host credentials set-edgegap --token &lt;Edgegap API token&gt; --app-name &lt;app&gt;</c>,
/// <c>mp-host credentials set-gameye --key &lt;Gameye API key&gt; --image-name &lt;image&gt; --location &lt;location&gt;</c>
/// (UM-T29's per-match allocation profiles: mp-host never calls either API itself -- these only give a
/// matchmaking service, or a one-off script, a vault-backed place to read the credential from instead
/// of hard-coding it, for <c>Unity.Multiplayer.Core.EdgegapMatchAllocator</c>/<c>GameyeMatchAllocator</c>).
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
            log.Error("Usage: mp-host credentials <set-ghcr|set-runpod|set-ssh|set-hetzner|set-vultr|set-digitalocean|set-linode|set-ovh|set-aws|set-gce|set-azure|set-fly|set-oci|set-cloud-options|set-edgegap|set-gameye> ...");
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

            case "set-aws":
                credentials.SetAwsCreds(new HostingCredentialStore.AwsCreds(
                    args.Require("access-key-id"), args.Require("secret-access-key"), args.Require("region")));
                log.Info("AWS credential stored (shared by AwsEc2, AwsLightsail and GameLift).");
                return Task.FromResult(0);

            case "set-gce":
                credentials.SetGceCreds(new HostingCredentialStore.GceCreds(args.Require("project"), args.Require("access-token")));
                log.Info("GCE credential stored. Access tokens expire (commonly in 1 hour) -- re-run this before a deploy if it's stale.");
                return Task.FromResult(0);

            case "set-azure":
                credentials.SetAzureCreds(new HostingCredentialStore.AzureCreds(
                    args.Require("tenant"), args.Require("client-id"), args.Require("client-secret"), args.Require("subscription")));
                log.Info("Azure credential stored.");
                return Task.FromResult(0);

            case "set-fly":
                credentials.SetFlyCreds(new HostingCredentialStore.FlyCreds(args.Require("token"), args.Require("org")));
                log.Info("Fly.io credential stored.");
                return Task.FromResult(0);

            case "set-oci":
                credentials.SetOciCreds(new HostingCredentialStore.OciCreds(
                    args.Require("tenancy"), args.Require("user"), args.Require("fingerprint"), args.Require("key"), args.Require("region"), args.Require("compartment")));
                log.Info("OCI credential stored.");
                return Task.FromResult(0);

            case "set-cloud-options":
                string profile = args.Require("profile");
                string serverName = args.Require("name");
                var options = ParseOpts(rawArgs);
                if (options.Count == 0)
                {
                    log.Error("At least one --opt KEY=VALUE is required.");
                    return Task.FromResult(1);
                }

                credentials.SetCloudVmOptions(profile, serverName, options);
                log.Info($"Launch options for '{profile}'/'{serverName}' stored: {string.Join(", ", options.Keys)}.");
                return Task.FromResult(0);

            case "set-edgegap":
                credentials.SetEdgegapCreds(new HostingCredentialStore.EdgegapCreds(args.Require("token"), args.Require("app-name")));
                log.Info("Edgegap credential stored.");
                return Task.FromResult(0);

            case "set-gameye":
                credentials.SetGameyeCreds(new HostingCredentialStore.GameyeCreds(
                    args.Require("key"), args.Require("image-name"), args.Require("location")));
                log.Info("Gameye credential stored.");
                return Task.FromResult(0);

            default:
                log.Error($"Unknown credentials subcommand '{rawArgs[1]}'.");
                return Task.FromResult(1);
        }
    }

    private static HostingCredentialStore.CloudVpsCreds ReadCloudVpsCreds(ArgumentReader args) =>
        new(args.Require("token"), args.GetOrDefault("ssh-user", "root"), args.Require("ssh-key"), ReadPublicKey(args));

    private static string ReadPublicKey(ArgumentReader args) => File.ReadAllText(args.Require("ssh-public-key")).Trim();

    private static Dictionary<string, string> ParseOpts(IReadOnlyList<string> rawArgs)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i < rawArgs.Count; i++)
        {
            if (rawArgs[i] != "--opt" || i + 1 >= rawArgs.Count) continue;
            string pair = rawArgs[++i];
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                throw new ArgumentException($"--opt '{pair}' must be KEY=VALUE.");
            }

            options[pair[..eq]] = pair[(eq + 1)..];
        }

        return options;
    }
}
