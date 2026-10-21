using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Local;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host local up --name &lt;server&gt; (--package &lt;dir&gt; | --unity &lt;exe&gt; --unity-project &lt;dir&gt;)
/// [--rendezvous-url &lt;url&gt; [--rendezvous-secret &lt;secret&gt;]] [--public-address &lt;addr&gt;]
/// [--server-password &lt;pwd&gt;] [--env KEY=VALUE ...] [--health-timeout &lt;seconds&gt;] [--no-upnp] [--dry-run]</c>
/// and <c>mp-host local down --name &lt;server&gt; [--dry-run]</c> (UM-T31): runs the already-built server
/// package as a Docker container on the operator's own machine -- no GHCR push, no SSH, no cloud API.
/// Players on the same network join by IP (<see cref="LanAddresses"/> prints it); <c>local up</c> also
/// makes a best-effort UPnP attempt to forward the game port off-LAN, reporting plainly when the
/// router refuses it (RFC 0001: this is the owner's own HOA network, where that refusal is the
/// expected and normal outcome, not a bug -- use Unity Relay for a listen server reachable beyond the
/// LAN in that case). Unlike <c>mp-host deploy</c>, there is no central rendezvous service unless the
/// operator explicitly asks for one (<c>--rendezvous-url</c>): LAN-only play has no use for join
/// codes.
///
/// <para><c>local up</c> remembers the package directory, compose file and whether a UPnP mapping was
/// added (<see cref="HostingCredentialStore.LocalServerState"/>) so a later <c>local down --name
/// &lt;server&gt;</c> needs no other flags.</para>
/// </summary>
public static class LocalCommand
{
    private static readonly HashSet<string> BooleanFlags = new(StringComparer.OrdinalIgnoreCase) { "dry-run", "no-upnp" };

    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct)
    {
        if (rawArgs.Count < 2)
        {
            log.Error("Usage: mp-host local <up|down> --name <server> ...");
            return 1;
        }

        return rawArgs[1] switch
        {
            "up" => await UpAsync(rawArgs, log, credentials, ct),
            "down" => await DownAsync(rawArgs, log, credentials, ct),
            _ => Unknown(rawArgs[1], log),
        };
    }

    private static async Task<int> UpAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct)
    {
        var args = new ArgumentReader(rawArgs, 2, BooleanFlags);
        string name = args.Require("name");
        bool dryRun = args.Has("dry-run");
        bool noUpnp = args.Has("no-upnp");

        var packageBuilder = new ServerPackageBuilder(new ProcessRunner());
        var packageRequest = new ServerPackageBuildRequest(GenericVpsProvisioner.Id, args.Get("package"), args.Get("unity"), args.Get("unity-project"));
        BuiltServerPackage package = await packageBuilder.BuildAsync(packageRequest, log, ct);
        ManifestPort? gamePort = package.Manifest.GamePort;

        IReadOnlyList<string> lanAddresses = LanAddresses.GetLocalIPv4Addresses();
        string defaultAddress = lanAddresses.FirstOrDefault() ?? "127.0.0.1";
        string publicAddress = args.GetOrDefault("public-address", defaultAddress);
        if (lanAddresses.Count > 0 && gamePort is not null)
        {
            log.Info($"LAN join: connect by IP to {string.Join(" or ", lanAddresses)}:{gamePort.Port}.");
        }
        else if (lanAddresses.Count == 0)
        {
            log.Warn("No LAN-facing network interface was found; only localhost join will work.");
        }

        var environment = new Dictionary<string, string> { ["MINDATTIC_PUBLIC_ADDRESS"] = publicAddress };
        string? password = args.Get("server-password");
        if (!string.IsNullOrEmpty(password))
        {
            environment["MINDATTIC_SERVER_PASSWORD"] = password;
        }

        string? rendezvousUrl = args.Get("rendezvous-url");
        string? rendezvousImage = null;
        if (rendezvousUrl is not null)
        {
            environment["MINDATTIC_RENDEZVOUS_URL"] = rendezvousUrl;
            environment["MINDATTIC_RENDEZVOUS_SECRET"] = new Composition.HostingComposer(credentials, dryRun)
                .ResolveRendezvousSecret(args.Get("rendezvous-secret"), log);
            rendezvousImage = args.GetOrDefault("rendezvous-image", "ghcr.io/mindattic/rendezvous:latest");
        }
        else
        {
            log.Info("No --rendezvous-url given; running LAN-only (join by IP, no join code).");
        }

        AddRepeatedEnv(environment, rawArgs);

        string compose = LocalComposeRenderer.Render(package.Manifest, package.Directory, environment, rendezvousImage);
        ILocalComposeRunner composeRunner = dryRun ? new DryRunLocalComposeRunner(log) : new ProcessLocalComposeRunner(new ProcessRunner());
        await composeRunner.UpAsync(package.Directory, LocalComposeRenderer.ComposeFileName, compose, log, ct);

        bool healthy = true;
        if (!dryRun)
        {
            using var http = new HttpClient();
            healthy = await LocalHealthCheck.WaitForHealthyAsync(http, package.Manifest, TimeSpan.FromSeconds(args.GetInt("health-timeout", 60)), log, ct);
        }

        bool upnpMapped = false;
        if (!noUpnp && gamePort is not null)
        {
            IUpnpClient upnp = dryRun ? new DryRunUpnpClient(log) : new SsdpUpnpClient();
            UpnpMappingResult mapping = await upnp.TryAddPortMappingAsync(gamePort.Port, gamePort.Port, gamePort.Protocol, $"mp-host {name}", ct);
            if (mapping.Success)
            {
                upnpMapped = true;
                log.Info(mapping.ExternalIp is not null
                    ? $"UPnP: the router forwarded {gamePort.Port}/{gamePort.Protocol}; your public IP is {mapping.ExternalIp}."
                    : $"UPnP: the router forwarded {gamePort.Port}/{gamePort.Protocol}.");
            }
            else
            {
                log.Warn($"UPnP port mapping was refused ({mapping.FailureReason}). " +
                    "Internet players cannot reach this listen server this way -- use Unity Relay for a listen server reachable off-LAN " +
                    "(RFC 0001), or rent a dedicated host (see docs/articles/hosting/).");
            }
        }

        if (!dryRun)
        {
            credentials.SetLocalState(name, new HostingCredentialStore.LocalServerState(
                package.Directory, LocalComposeRenderer.ComposeFileName, gamePort?.Port ?? 0, gamePort?.Protocol ?? "udp", upnpMapped));
        }

        log.Info($"Healthy: {healthy}");
        return healthy ? 0 : 1;
    }

    private static async Task<int> DownAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct)
    {
        var args = new ArgumentReader(rawArgs, 2, BooleanFlags);
        string name = args.Require("name");
        bool dryRun = args.Has("dry-run");

        HostingCredentialStore.LocalServerState? state = credentials.GetLocalState(name);
        string? packageDirectory = state?.PackageDirectory ?? args.Get("package");
        if (packageDirectory is null)
        {
            log.Error($"No local state stored for '{name}' (was it started with 'mp-host local up --name {name}'?); pass --package <dir> to tear it down anyway.");
            return 1;
        }

        string composeFileName = state?.ComposeFileName ?? LocalComposeRenderer.ComposeFileName;
        ILocalComposeRunner composeRunner = dryRun ? new DryRunLocalComposeRunner(log) : new ProcessLocalComposeRunner(new ProcessRunner());
        await composeRunner.DownAsync(packageDirectory, composeFileName, log, ct);

        if (state is { UpnpMapped: true })
        {
            IUpnpClient upnp = dryRun ? new DryRunUpnpClient(log) : new SsdpUpnpClient();
            await upnp.TryRemovePortMappingAsync(state.Port, state.Protocol, ct);
            log.Info("UPnP port mapping removed.");
        }

        if (!dryRun)
        {
            credentials.ClearLocalState(name);
        }

        log.Info($"Local server '{name}' stopped.");
        return 0;
    }

    private static void AddRepeatedEnv(Dictionary<string, string> environment, IReadOnlyList<string> rawArgs)
    {
        for (int i = 0; i < rawArgs.Count; i++)
        {
            if (rawArgs[i] != "--env" || i + 1 >= rawArgs.Count) continue;
            string pair = rawArgs[++i];
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                throw new ArgumentException($"--env '{pair}' must be KEY=VALUE.");
            }

            environment[pair[..eq]] = pair[(eq + 1)..];
        }
    }

    private static int Unknown(string verb, IDeployLog log)
    {
        log.Error($"Unknown 'local' subcommand '{verb}'. Usage: mp-host local <up|down> --name <server> ...");
        return 1;
    }
}
