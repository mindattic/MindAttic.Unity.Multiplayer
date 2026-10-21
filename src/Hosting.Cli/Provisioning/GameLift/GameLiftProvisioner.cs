using System.IO.Compression;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

/// <summary>
/// <see cref="IProvisioner"/> for <c>GameLift</c>: zips the server package Unity built for the
/// <c>GameLift</c> hosting profile (<c>Unity.Multiplayer.Core.GameLiftProfile</c> -- no Docker image,
/// so <see cref="RequiresContainerImage"/> is false), uploads it as a GameLift build, waits for it to
/// go <c>READY</c>, then creates a fleet of EC2 instances running it (one dedicated-server process per
/// instance, from the manifest's declared game port) plus a simple-routing alias so a later
/// <c>mp-host update</c> can swap fleets without the alias id changing. A matchmaker allocates the
/// actual game sessions on that fleet afterwards with <c>Unity.Multiplayer.Core.GameLiftMatchAllocator</c>
/// -- this provisioner's job ends once the fleet is <c>ACTIVE</c>.
///
/// <para><see cref="ProviderConnection.ProviderOptions"/> keys at provision time: <c>ec2InstanceType</c>
/// (default <c>c5.large</c>), <c>fleetType</c> (default <c>ON_DEMAND</c>; <c>SPOT</c> is cheaper but can
/// be reclaimed), <c>concurrentExecutions</c> (default <c>1</c> -- one game server process per instance;
/// running more needs distinct ports per process, a later extension this provisioner does not attempt).
/// The AWS account credential is the same <c>HostingCredentialStore.AwsCreds</c> AwsEc2/AwsLightsail
/// share (<c>mp-host credentials set-aws</c>).</para>
///
/// <para>GameLift has no SSH box to tail, so <see cref="GetLogsAsync"/> returns the fleet's own
/// activation/capacity/error events instead of container logs, and the pipeline's join-code wait
/// (<see cref="Deploy.JoinCodeWatcher"/>) degrades to a warning: there is no single "the" join code for
/// a fleet, only one per game session once a matchmaker allocates one.</para>
///
/// <para>The <c>serverId</c> this provisioner hands back packs the fleet, alias and build ids
/// (<see cref="GameLiftServerId"/>) -- there is nowhere else for mp-host to remember them between CLI
/// invocations.</para>
/// </summary>
public sealed class GameLiftProvisioner : IProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "GameLift";

    private const string DefaultEc2InstanceType = "c5.large";
    private const string DefaultFleetType = "ON_DEMAND";
    private const int DefaultConcurrentExecutions = 1;
    private const int DefaultGamePort = 7777;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BuildReadyTimeout = TimeSpan.FromMinutes(10);

    private readonly IGameLiftFleetApiClient _api;

    public GameLiftProvisioner(IGameLiftFleetApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public string DisplayName => "AWS GameLift (managed fleet, build upload)";

    /// <inheritdoc />
    public bool RequiresContainerImage => false;

    /// <inheritdoc />
    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PackageDirectory))
        {
            throw new ArgumentException("ProvisionRequest.PackageDirectory is required for GameLift (it zips the raw build, not a Docker image).");
        }

        var options = request.Connection.ProviderOptions;
        string instanceType = request.Size ?? options.GetValueOrDefault("ec2InstanceType", DefaultEc2InstanceType);
        string fleetType = options.GetValueOrDefault("fleetType", DefaultFleetType);
        int concurrentExecutions = int.TryParse(options.GetValueOrDefault("concurrentExecutions"), out int n) && n > 0 ? n : DefaultConcurrentExecutions;
        int gamePort = request.Manifest.GamePort?.Port ?? DefaultGamePort;
        string gamePortProtocol = request.Manifest.GamePort?.Protocol ?? "udp";
        string version = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        string zipPath = Path.Combine(Path.GetTempPath(), $"mp-host-gamelift-{request.ServerName}-{Guid.NewGuid():N}.zip");
        log.Info($"Zipping the GameLift build package at '{request.PackageDirectory}' -> {zipPath}.");
        CreateZip(request.PackageDirectory, zipPath, request.Manifest);

        GameLiftBuild build;
        try
        {
            log.Info($"Creating a GameLift build for {request.ServerName} ({version}).");
            build = await _api.CreateBuildAsync(request.ServerName, version, ct);
            log.Info($"Uploading the build to {build.BuildId}'s S3 location.");
            await _api.UploadBuildAsync(build, zipPath, ct);
            build = await WaitForBuildReadyAsync(build.BuildId, log, ct);
        }
        finally
        {
            TryDeleteFile(zipPath);
        }

        string launchPath = "/local/game/" + NormalizeRelativePath(request.Manifest.Start);
        string launchParameters = $"-batchmode -nographics -mode server -port {gamePort}";
        var inboundPermissions = new List<GameLiftPortRange> { new(gamePort, gamePort, gamePortProtocol) };

        log.Info($"Creating a {instanceType} ({fleetType}) GameLift fleet for {request.ServerName} from build {build.BuildId}.");
        GameLiftFleet fleet = await _api.CreateFleetAsync(
            new GameLiftCreateFleetRequest(request.ServerName, build.BuildId, instanceType, fleetType, launchPath, launchParameters, concurrentExecutions, inboundPermissions),
            ct);

        string aliasId = await _api.CreateAliasAsync(request.ServerName + "-alias", fleet.FleetId, ct);

        var mappings = new Dictionary<int, int> { [gamePort] = gamePort };
        return new ProvisionResult(GameLiftServerId.Combine(fleet.FleetId, aliasId, build.BuildId), fleet.FleetId, mappings);
    }

    /// <inheritdoc />
    /// <remarks>GameLift's inbound permissions are already set at fleet creation; this re-asserts the same set so a changed manifest port is picked up on a later deploy of the same server name.</remarks>
    public async Task OpenFirewallAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (fleetId, _, _) = GameLiftServerId.Split(serverId);
        int gamePort = request.Manifest.GamePort?.Port ?? DefaultGamePort;
        string protocol = request.Manifest.GamePort?.Protocol ?? "udp";
        log.Info($"Confirming inbound permissions on {fleetId} for {gamePort}/{protocol}.");
        await _api.UpdateFleetPortSettingsAsync(fleetId, new[] { new GameLiftPortRange(gamePort, gamePort, protocol) }, ct);
    }

    /// <inheritdoc />
    public async Task<bool> WaitForHealthyAsync(string serverId, ProvisionRequest request, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        var (fleetId, _, _) = GameLiftServerId.Split(serverId);
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            GameLiftFleet fleet = await _api.DescribeFleetAttributesAsync(fleetId, ct);
            if (string.Equals(fleet.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(fleet.Status, "ERROR", StringComparison.OrdinalIgnoreCase) || DateTime.UtcNow >= deadline)
            {
                log.Warn($"{fleetId} is {fleet.Status}, not ACTIVE, after waiting {timeout}.");
                return false;
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <inheritdoc />
    public async Task<ServerStatus> GetStatusAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        var (fleetId, _, _) = GameLiftServerId.Split(serverId);
        GameLiftFleet fleet = await _api.DescribeFleetAttributesAsync(fleetId, ct);
        return new ServerStatus(serverId, MapState(fleet.Status), null, $"gameliftFleetStatus={fleet.Status}");
    }

    /// <inheritdoc />
    /// <remarks>A fleet has no single process to tail; this returns its recent activation/capacity/error events instead.</remarks>
    public async Task<string> GetLogsAsync(string serverId, int tailLines, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        var (fleetId, _, _) = GameLiftServerId.Split(serverId);
        IReadOnlyList<GameLiftEvent> events = await _api.DescribeFleetEventsAsync(fleetId, tailLines, ct);
        return string.Join('\n', events.Select(e => $"{e.EventTime:u} [{e.EventCode}] {e.Message}"));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Blue/green: uploads the freshly-built package as a new build, creates a new fleet from it and
    /// waits for it to go ACTIVE, then repoints the alias at the new fleet. The old fleet is left
    /// running (and its build kept) -- delete it yourself with <c>mp-host destroy</c> once you have
    /// confirmed the new one serves matches correctly; GameLift's own in-flight game sessions on it
    /// are not interrupted by this update.
    /// </remarks>
    public async Task UpdateAsync(string serverId, ProvisionRequest request, IDeployLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, aliasId, _) = GameLiftServerId.Split(serverId);
        ProvisionResult provisioned = await ProvisionAsync(request, log, ct);
        var (newFleetId, newAliasId, _) = GameLiftServerId.Split(provisioned.ServerId);
        log.Info($"Repointing alias {aliasId} at the new fleet {newFleetId} (dropping the fleet's own new alias {newAliasId}).");
        await _api.UpdateAliasAsync(aliasId, newFleetId, ct);
        await _api.DeleteAliasAsync(newAliasId, ct);
    }

    /// <inheritdoc />
    public async Task DestroyAsync(string serverId, ProviderConnection connection, IDeployLog log, CancellationToken ct)
    {
        var (fleetId, aliasId, buildId) = GameLiftServerId.Split(serverId);
        log.Info($"Deleting GameLift alias {aliasId}, fleet {fleetId} and build {buildId}.");
        await _api.DeleteAliasAsync(aliasId, ct);
        await _api.DeleteFleetAsync(fleetId, ct);
        await _api.DeleteBuildAsync(buildId, ct);
    }

    private async Task<GameLiftBuild> WaitForBuildReadyAsync(string buildId, IDeployLog log, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + BuildReadyTimeout;
        while (true)
        {
            GameLiftBuild build = await _api.DescribeBuildAsync(buildId, ct);
            if (string.Equals(build.Status, "READY", StringComparison.OrdinalIgnoreCase))
            {
                return build;
            }

            if (string.Equals(build.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"GameLift build {buildId} failed validation.");
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"GameLift build {buildId} did not become READY within {BuildReadyTimeout}.");
            }

            log.Info($"Build {buildId} is {build.Status}; waiting for READY.");
            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>
    /// Zips <paramref name="sourceDirectory"/> into <paramref name="destinationZipPath"/>, marking the
    /// start script and the dedicated-server executable itself as Unix-executable (mode 0755) in the
    /// zip's external file attributes -- <see cref="ZipFile.CreateFromDirectory(string, string)"/> does
    /// not preserve that bit, and mp-host is often run from a Windows machine that never set it in the
    /// first place, but GameLift's Linux fleet needs it to run the build at all.
    /// </summary>
    private static void CreateZip(string sourceDirectory, string destinationZipPath, ServerPackageManifest manifest)
    {
        if (File.Exists(destinationZipPath))
        {
            File.Delete(destinationZipPath);
        }

        string startScript = NormalizeRelativePath(manifest.Start);
        string executable = NormalizeRelativePath(manifest.Executable);

        using ZipArchive zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
        foreach (string filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            ZipArchiveEntry entry = zip.CreateEntryFromFile(filePath, relative, CompressionLevel.Optimal);
            bool executableBit = relative.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relative, startScript, StringComparison.Ordinal)
                || string.Equals(relative, executable, StringComparison.Ordinal);
            if (executableBit)
            {
                entry.ExternalAttributes = 0x81ED << 16; // Unix mode 0100755: regular file, rwxr-xr-x.
            }
        }
    }

    private static string NormalizeRelativePath(string path) => (path ?? "").TrimStart('.', '/');

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp zip; a locked/already-gone file is not worth failing the deploy over.
        }
    }

    private static ServerState MapState(string fleetStatus) => fleetStatus.ToUpperInvariant() switch
    {
        "NEW" or "DOWNLOADING" or "VALIDATING" or "BUILDING" or "ACTIVATING" => ServerState.Provisioning,
        "ACTIVE" => ServerState.Running,
        "ERROR" => ServerState.Unhealthy,
        "DELETING" or "TERMINATED" => ServerState.Gone,
        _ => ServerState.Unknown,
    };
}
