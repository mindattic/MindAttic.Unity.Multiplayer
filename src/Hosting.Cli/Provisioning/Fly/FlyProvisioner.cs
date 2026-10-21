using Unity.Multiplayer.Hosting.Cli.CloudInit;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>
/// <see cref="IProvisioner"/> for Fly.io (UM-T28): creates a Fly app and a Machine running the
/// pushed GHCR image straight through Fly's Machines API (<see cref="IFlyApiClient"/>) -- mp-host
/// never shells out to <c>flyctl deploy</c>, though <see cref="FlyTomlGenerator"/> can still render a
/// <c>fly.toml</c> describing the same shape for operators who want one. A dedicated IPv4 is
/// allocated for the app because Fly's free shared (anycast) address only proxies HTTP(S)/TCP; UDP
/// needs a dedicated one. The game machine's environment carries <c>MINDATTIC_BIND_HOST=fly-global-services</c>
/// -- Fly's edge only delivers UDP packets to a machine that binds that hostname, never <c>0.0.0.0</c>.
///
/// <para>Rendezvous runs one of two ways, chosen by the <c>rendezvousMode</c>
/// <see cref="ProviderConnection.ProviderOptions"/> key (default <see cref="FlyTomlGenerator.ProcessGroupRendezvousMode"/>):
/// <list type="bullet">
/// <item><b>process-group</b>: a second Machine in the <i>same</i> Fly app, tagged with the
/// <c>rendezvous</c> process group (Fly's dashboard/<c>fly status</c> group machines by this field);
/// it runs <c>rendezvousImage</c> rather than the game image -- the Machines API lets each machine in
/// an app carry its own image, unlike a <c>fly.toml</c>-driven <c>flyctl deploy</c>, which is limited
/// to one image per app.</item>
/// <item><b>app</b>: a wholly separate Fly app, named <c>&lt;name&gt;-rendezvous</c>, with its own
/// shared IPv4 (sufficient for a TCP-only service) and its own lifecycle; <see cref="DestroyAsync"/>
/// tears it down alongside the game app.</item>
/// </list></para>
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys: <c>orgSlug</c> (required -- the Fly
/// organization to create the app(s) under), <c>rendezvousMode</c> (<c>process-group</c> or
/// <c>app</c>, default <c>process-group</c>), <c>rendezvousImage</c> (defaults to
/// <c>ghcr.io/mindattic/rendezvous:latest</c>). No provider secret travels through
/// <see cref="ProviderConnection"/> -- the Fly API token is baked into the injected
/// <see cref="IFlyApiClient"/> by <see cref="Composition.HostingComposer"/> from the vault, the same
/// split every other provider uses.</para>
///
/// <para>The server id this provisioner hands back and expects on every later call is the Fly app
/// name (not a machine id): status/logs/update resolve the app's current machine(s) fresh on every
/// call, since Fly machine ids are only known right after they're created.</para>
/// </summary>
public sealed class FlyProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "Fly";

    /// <summary>Fly's own default process group name for a fly.toml with no <c>[processes]</c> section.</summary>
    public const string GameProcessGroup = "app";

    /// <summary>The process group name the co-located rendezvous machine is tagged with.</summary>
    public const string RendezvousProcessGroup = "rendezvous";

    /// <summary>Ashburn, Virginia -- Fly's long-standing default region.</summary>
    public const string DefaultRegion = "iad";

    /// <summary>Fly's smallest shared-CPU machine size; plenty for a small dedicated game server.</summary>
    public const string DefaultSize = "shared-cpu-1x";

    private const string DefaultRendezvousImage = "ghcr.io/mindattic/rendezvous:latest";

    private static readonly IReadOnlyDictionary<string, FlyGuest> GuestSizes = new Dictionary<string, FlyGuest>(StringComparer.OrdinalIgnoreCase)
    {
        ["shared-cpu-1x"] = new("shared", 1, 256),
        ["shared-cpu-2x"] = new("shared", 2, 512),
        ["shared-cpu-4x"] = new("shared", 4, 1024),
        ["shared-cpu-8x"] = new("shared", 8, 2048),
        ["performance-1x"] = new("performance", 1, 2048),
        ["performance-2x"] = new("performance", 2, 4096),
        ["performance-4x"] = new("performance", 4, 8192),
    };

    private readonly IFlyApiClient _api;
    private readonly IFlyCli _cli;

    public FlyProvisioner(IFlyApiClient api, IFlyCli cli)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "Fly.io (Machines API, dedicated IPv4 UDP)";

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string region = request.Region ?? DefaultRegion;
        string size = request.Size ?? DefaultSize;
        string appName = request.ServerName;
        string orgSlug = RequireOrgSlug(request.Connection);
        string rendezvousMode = request.Connection.ProviderOptions.GetValueOrDefault("rendezvousMode", FlyTomlGenerator.ProcessGroupRendezvousMode);
        string rendezvousImage = request.Connection.ProviderOptions.GetValueOrDefault("rendezvousImage", DefaultRendezvousImage);
        var gamePort = request.Manifest.GamePort ?? throw new InvalidOperationException("The manifest has no declared game port.");

        log.Info($"Creating Fly app {appName} (org {orgSlug}) in {region} ({size}) from {request.ImageReference}.");
        await _api.EnsureAppAsync(appName, orgSlug, ct);

        FlyIpAddress ip = await _api.AllocateDedicatedIpv4Async(appName, region, ct);

        var env = new Dictionary<string, string>(request.EnvironmentVariables) { ["MINDATTIC_BIND_HOST"] = FlyTomlGenerator.UdpBindHost };
        var gameServices = new List<FlyServicePort> { new(gamePort.Port, gamePort.Protocol, gamePort.Port) };
        await _api.CreateMachineAsync(appName, new FlyCreateMachineRequest(
            $"{appName}-game", region, request.ImageReference, GameProcessGroup, GuestFor(size), env, gameServices), ct);

        var mappings = new Dictionary<int, int> { [gamePort.Port] = gamePort.Port };

        if (string.Equals(rendezvousMode, FlyTomlGenerator.ProcessGroupRendezvousMode, StringComparison.OrdinalIgnoreCase))
        {
            log.Info($"Creating a co-located rendezvous process group in {appName} from {rendezvousImage}.");
            await _api.CreateMachineAsync(appName, RendezvousMachineRequest($"{appName}-rendezvous", region, size, rendezvousImage, request.EnvironmentVariables, RendezvousProcessGroup), ct);
            mappings[CloudInitRenderer.RendezvousPort] = CloudInitRenderer.RendezvousPort;
        }
        else if (string.Equals(rendezvousMode, FlyTomlGenerator.AppRendezvousMode, StringComparison.OrdinalIgnoreCase))
        {
            string rendezvousAppName = RendezvousAppName(appName);
            log.Info($"Creating a separate Fly app {rendezvousAppName} for rendezvous from {rendezvousImage}.");
            await _api.EnsureAppAsync(rendezvousAppName, orgSlug, ct);
            await _api.AllocateSharedIpv4Async(rendezvousAppName, ct);
            await _api.CreateMachineAsync(rendezvousAppName, RendezvousMachineRequest($"{rendezvousAppName}-game", region, size, rendezvousImage, request.EnvironmentVariables, GameProcessGroup), ct);
        }
        else
        {
            throw new ArgumentException(
                $"Unknown Fly rendezvousMode '{rendezvousMode}'; expected '{FlyTomlGenerator.ProcessGroupRendezvousMode}' or '{FlyTomlGenerator.AppRendezvousMode}'.");
        }

        return new ProvisionResult(appName, ip.Address, mappings);
    }

    /// <inheritdoc />
    /// <remarks>Fly opens the declared ports at machine creation (its <c>services</c> config); there is no separate firewall rule to open.</remarks>
    public Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        log.Info("Fly opens ports at machine creation; no separate firewall step is needed.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Polls the game process group's machine state rather than curling a health endpoint: mp-host
    /// runs outside Fly's private network (6PN), so it cannot reach a machine's internal health port
    /// directly the way the SSH-based providers can. Fly's own <c>tcp_checks</c>
    /// (<see cref="FlyTomlGenerator"/> renders one when the manifest declares a health port) are the
    /// authoritative health signal once the app is live; this wait is only for "did the machine come
    /// up at all".
    /// </remarks>
    public async Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var machines = await _api.ListMachinesAsync(serverId, ct);
            var game = FindGameMachine(machines);
            if (game is not null && string.Equals(game.State, "started", StringComparison.OrdinalIgnoreCase))
            {
                log.Info($"{serverId}'s game machine is started.");
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        log.Warn($"{serverId} did not reach 'started' within {timeout}.");
        return false;
    }

    /// <inheritdoc />
    public async Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        var machines = await _api.ListMachinesAsync(serverId, ct);
        var game = FindGameMachine(machines) ?? machines.FirstOrDefault();
        if (game is null)
        {
            return new ServerStatus(serverId, ServerState.Gone, null, "no machines found");
        }

        ServerState state = game.State.ToLowerInvariant() switch
        {
            "started" => ServerState.Running,
            "starting" or "created" or "replacing" => ServerState.Provisioning,
            "stopping" or "stopped" => ServerState.Stopped,
            "destroying" or "destroyed" => ServerState.Gone,
            _ => ServerState.Unknown,
        };

        var ips = await _api.ListIpAddressesAsync(serverId, ct);
        string? address = ips.FirstOrDefault(i => string.Equals(i.Type, "v4", StringComparison.OrdinalIgnoreCase))?.Address;
        return new ServerStatus(serverId, state, address, $"state={game.State}, machines={machines.Count}");
    }

    /// <inheritdoc />
    /// <remarks>Fly's Machines API has no simple REST log endpoint; this shells to <c>flyctl logs</c> instead (<see cref="IFlyCli"/>), the one operation in this provisioner that is not pure Machines API.</remarks>
    public Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct) =>
        _cli.GetLogsAsync(serverId, tailLines, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Replaces the game machine's config in place through the Machines API: Fly stops it and starts
    /// it again on the new image, keeping the same machine id -- an in-place rolling restart like
    /// GenericVps's <c>docker compose up -d</c>, not a RunPod-style destroy-and-recreate, but still a
    /// brief connection drop since this profile runs a single game machine per app. Scale to two
    /// machines behind Fly's own load balancer if a zero-downtime update matters more than cost.
    /// </remarks>
    public async Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var machines = await _api.ListMachinesAsync(serverId, ct);
        var game = FindGameMachine(machines) ?? throw new InvalidOperationException($"No game machine found in Fly app {serverId}.");

        string region = request.Region ?? game.Region ?? DefaultRegion;
        string size = request.Size ?? DefaultSize;
        var gamePort = request.Manifest.GamePort ?? throw new InvalidOperationException("The manifest has no declared game port.");
        var env = new Dictionary<string, string>(request.EnvironmentVariables) { ["MINDATTIC_BIND_HOST"] = FlyTomlGenerator.UdpBindHost };
        var services = new List<FlyServicePort> { new(gamePort.Port, gamePort.Protocol, gamePort.Port) };

        log.Info($"Rolling {serverId}'s game machine onto {request.ImageReference} (Fly stops then starts it in place).");
        await _api.UpdateMachineAsync(serverId, game.Id, new FlyCreateMachineRequest(
            game.Name, region, request.ImageReference, GameProcessGroup, GuestFor(size), env, services), ct);
    }

    /// <inheritdoc />
    public async Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        log.Info($"Destroying Fly app {serverId}.");
        await DestroyAppAndMachinesAsync(serverId, ct);

        string rendezvousMode = connection.ProviderOptions.GetValueOrDefault("rendezvousMode", FlyTomlGenerator.ProcessGroupRendezvousMode);
        if (string.Equals(rendezvousMode, FlyTomlGenerator.AppRendezvousMode, StringComparison.OrdinalIgnoreCase))
        {
            string rendezvousAppName = RendezvousAppName(serverId);
            log.Info($"Destroying its separate rendezvous app {rendezvousAppName}.");
            await DestroyAppAndMachinesAsync(rendezvousAppName, ct);
        }
    }

    private async Task DestroyAppAndMachinesAsync(string appName, CancellationToken ct)
    {
        var machines = await _api.ListMachinesAsync(appName, ct);
        foreach (var machine in machines)
        {
            await _api.DestroyMachineAsync(appName, machine.Id, ct);
        }

        await _api.DestroyAppAsync(appName, ct);
    }

    private FlyCreateMachineRequest RendezvousMachineRequest(
        string machineName, string region, string size, string rendezvousImage, IReadOnlyDictionary<string, string> environment, string processGroup)
    {
        var rendezvousEnv = new Dictionary<string, string>
        {
            ["Rendezvous__RegistrationSecret"] = environment.GetValueOrDefault("MINDATTIC_RENDEZVOUS_SECRET", ""),
        };
        var rendezvousServices = new List<FlyServicePort> { new(CloudInitRenderer.RendezvousPort, "tcp", CloudInitRenderer.RendezvousPort) };
        return new FlyCreateMachineRequest(machineName, region, rendezvousImage, processGroup, GuestFor(size), rendezvousEnv, rendezvousServices);
    }

    private static FlyMachine? FindGameMachine(IReadOnlyList<FlyMachine> machines) =>
        machines.FirstOrDefault(m => string.Equals(m.ProcessGroup, GameProcessGroup, StringComparison.OrdinalIgnoreCase));

    private static string RendezvousAppName(string appName) => $"{appName}-rendezvous";

    private static string RequireOrgSlug(ProviderConnection connection) =>
        connection.ProviderOptions.GetValueOrDefault("orgSlug")
            ?? throw new ArgumentException("ProviderOptions['orgSlug'] is required to create a Fly app (your Fly organization slug, e.g. 'personal').");

    private static FlyGuest GuestFor(string size) =>
        GuestSizes.TryGetValue(size, out var guest)
            ? guest
            : throw new ArgumentException($"Unknown Fly machine size '{size}'. Known: {string.Join(", ", GuestSizes.Keys)}.");
}
