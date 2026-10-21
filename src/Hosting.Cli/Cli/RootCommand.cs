using Unity.Multiplayer.Hosting.Cli.Commands;
using Unity.Multiplayer.Hosting.Cli.Credentials;

namespace Unity.Multiplayer.Hosting.Cli.Cli;

/// <summary>Dispatches <c>mp-host &lt;verb&gt; ...</c> to its command, printing usage for an unknown or missing verb.</summary>
public static class RootCommand
{
    private const string Usage = """
        mp-host -- the MindAttic.Unity.Multiplayer hosting deploy CLI (UM-T25)

        Usage:
          mp-host deploy      --profile <GenericVps|RunPod|Hetzner|Vultr|DigitalOcean|Linode|OvhCloud|AwsEc2|AwsLightsail|Gce|AzureVm> --name <server> --image <owner/repo> ...
          mp-host status      --profile <p> --name <server> --server-id <id>
          mp-host logs         --profile <p> --name <server> --server-id <id> [--tail <n>]
          mp-host update       --profile <p> --name <server> --server-id <id> --image <owner/repo> ...
          mp-host destroy      --profile <p> --name <server> --server-id <id> --yes
          mp-host credentials  set-ghcr | set-runpod | set-ssh | set-hetzner | set-vultr | set-digitalocean | set-linode | set-ovh | set-aws | set-gce | set-azure | set-cloud-options ...

        Hetzner/Vultr/DigitalOcean/Linode/OvhCloud (UM-T26) and AwsEc2/AwsLightsail/Gce/AzureVm
        (UM-T27) each create their own VM plus a security group/firewall/NSG for the game port and
        rendezvous; see docs/articles/hosting/ for each one's setup and sizing guide, and -- for the
        UM-T27 big-cloud profiles -- the per-player-hour egress cost estimate before using one for a
        public launch.

        Every command accepts --dry-run, which prints every API/SSH/docker call it would make
        instead of making it. Credentials are read from MindAttic.Vault (see 'credentials'); none
        are ever read from, or written to, this repo.
        """;

    public static async Task<int> RunAsync(string[] args, IDeployLog log, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            log.Info(Usage);
            return 1;
        }

        try
        {
            HostingCredentialStore credentials = HostingCredentialStore.Default;
            return args[0] switch
            {
                "deploy" => await DeployCommand.RunAsync(args, log, credentials, ct),
                "status" => await StatusCommand.RunAsync(args, log, credentials, ct),
                "logs" => await LogsCommand.RunAsync(args, log, credentials, ct),
                "update" => await UpdateCommand.RunAsync(args, log, credentials, ct),
                "destroy" => await DestroyCommand.RunAsync(args, log, credentials, ct),
                "credentials" => await CredentialsCommand.RunAsync(args, log, credentials),
                "help" or "--help" or "-h" => Help(log),
                _ => Unknown(args[0], log),
            };
        }
        catch (ArgumentException ex)
        {
            log.Error(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            log.Error(ex.Message);
            return 1;
        }
    }

    private static int Help(IDeployLog log)
    {
        log.Info(Usage);
        return 0;
    }

    private static int Unknown(string verb, IDeployLog log)
    {
        log.Error($"Unknown command '{verb}'.");
        log.Info(Usage);
        return 1;
    }
}
