using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

/// <summary>
/// Dry-run <see cref="IGameLiftFleetApiClient"/>: prints the GameLift API call (or S3 PUT) it would
/// have made and returns a synthesized build/fleet/alias so the rest of the pipeline (firewall wait,
/// health check, join-code wait) still has something to narrate against. No network call is made and
/// no AWS credential is read.
/// </summary>
public sealed class DryRunGameLiftFleetApiClient : IGameLiftFleetApiClient
{
    private const string SimulatedBuildId = "build-dryrun00000000";
    private const string SimulatedFleetId = "fleet-dryrun00000000";
    private const string SimulatedAliasId = "alias-dryrun00000000";

    private readonly IDeployLog _log;

    public DryRunGameLiftFleetApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<GameLiftBuild> CreateBuildAsync(string name, string version, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.CreateBuild Name={name} Version={version}");
        return Task.FromResult(new GameLiftBuild(SimulatedBuildId, "READY", "dry-run-bucket", "dry-run-key", "arn:aws:iam::000000000000:role/dry-run", "AKIADRYRUN", "dryrunsecret", "dryruntoken"));
    }

    /// <inheritdoc />
    public Task UploadBuildAsync(GameLiftBuild build, string zipFilePath, CancellationToken ct)
    {
        _log.DryRun($"PUT https://{{bucket}}.s3.{{region}}.amazonaws.com/{{key}} (build {build.BuildId} <- {zipFilePath})");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<GameLiftBuild> DescribeBuildAsync(string buildId, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.DescribeBuild BuildId={buildId}");
        return Task.FromResult(new GameLiftBuild(buildId, "READY", null, null, null, null, null, null));
    }

    /// <inheritdoc />
    public Task<GameLiftFleet> CreateFleetAsync(GameLiftCreateFleetRequest request, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.CreateFleet Name={request.Name} BuildId={request.BuildId} EC2InstanceType={request.Ec2InstanceType}");
        return Task.FromResult(new GameLiftFleet(SimulatedFleetId, "ACTIVE", "arn:aws:gamelift:us-east-1:000000000000:fleet/" + SimulatedFleetId));
    }

    /// <inheritdoc />
    public Task<GameLiftFleet> DescribeFleetAttributesAsync(string fleetId, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.DescribeFleetAttributes FleetIds.1={fleetId}");
        return Task.FromResult(new GameLiftFleet(fleetId, "ACTIVE", null));
    }

    /// <inheritdoc />
    public Task UpdateFleetPortSettingsAsync(string fleetId, IReadOnlyList<GameLiftPortRange> inboundPermissions, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.UpdateFleetPortSettings FleetId={fleetId} permissions={string.Join(',', inboundPermissions.Select(p => $"{p.FromPort}-{p.ToPort}/{p.Protocol}"))}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> CreateAliasAsync(string name, string fleetId, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.CreateAlias Name={name} FleetId={fleetId}");
        return Task.FromResult(SimulatedAliasId);
    }

    /// <inheritdoc />
    public Task UpdateAliasAsync(string aliasId, string fleetId, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.UpdateAlias AliasId={aliasId} FleetId={fleetId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GameLiftEvent>> DescribeFleetEventsAsync(string fleetId, int limit, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.DescribeFleetEvents FleetId={fleetId} Limit={limit}");
        return Task.FromResult<IReadOnlyList<GameLiftEvent>>(new List<GameLiftEvent>
        {
            new("evt-dryrun", "FLEET_CREATED", "(dry run: no real events)", DateTimeOffset.UtcNow),
        });
    }

    /// <inheritdoc />
    public Task DeleteFleetAsync(string fleetId, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.DeleteFleet FleetId={fleetId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAliasAsync(string aliasId, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.DeleteAlias AliasId={aliasId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteBuildAsync(string buildId, CancellationToken ct)
    {
        _log.DryRun($"POST https://gamelift.{{region}}.amazonaws.com/ X-Amz-Target=GameLift.DeleteBuild BuildId={buildId}");
        return Task.CompletedTask;
    }
}
