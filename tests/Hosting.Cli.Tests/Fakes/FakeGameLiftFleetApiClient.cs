using Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="GameLiftProvisioner"/> makes and answers canned builds/fleets, with no real HTTP or S3 upload.</summary>
public sealed class FakeGameLiftFleetApiClient : IGameLiftFleetApiClient
{
    public List<(string Name, string Version)> CreateBuildCalls { get; } = new();
    public List<(GameLiftBuild Build, string ZipFilePath)> UploadCalls { get; } = new();
    public List<string> DescribeBuildCalls { get; } = new();
    public List<GameLiftCreateFleetRequest> CreateFleetCalls { get; } = new();
    public List<string> DescribeFleetCalls { get; } = new();
    public List<(string FleetId, IReadOnlyList<GameLiftPortRange> Ports)> UpdatePortsCalls { get; } = new();
    public List<(string Name, string FleetId)> CreateAliasCalls { get; } = new();
    public List<(string AliasId, string FleetId)> UpdateAliasCalls { get; } = new();
    public List<string> DeleteFleetCalls { get; } = new();
    public List<string> DeleteAliasCalls { get; } = new();
    public List<string> DeleteBuildCalls { get; } = new();

    public GameLiftBuild BuildToReturn { get; set; } = new("build-1", "READY", "bucket", "key", "role", "AKIA", "secret", "token");
    public Queue<GameLiftBuild>? DescribeBuildSequence { get; set; }
    public GameLiftFleet FleetToReturn { get; set; } = new("fleet-1", "ACTIVE", "arn:fleet-1");
    public Queue<GameLiftFleet>? DescribeFleetSequence { get; set; }
    public string AliasIdToReturn { get; set; } = "alias-1";
    public IReadOnlyList<GameLiftEvent> EventsToReturn { get; set; } = new List<GameLiftEvent>();

    public Task<GameLiftBuild> CreateBuildAsync(string name, string version, CancellationToken ct)
    {
        CreateBuildCalls.Add((name, version));
        return Task.FromResult(BuildToReturn);
    }

    public Task UploadBuildAsync(GameLiftBuild build, string zipFilePath, CancellationToken ct)
    {
        UploadCalls.Add((build, zipFilePath));
        return Task.CompletedTask;
    }

    public Task<GameLiftBuild> DescribeBuildAsync(string buildId, CancellationToken ct)
    {
        DescribeBuildCalls.Add(buildId);
        var build = DescribeBuildSequence is { Count: > 0 } ? DescribeBuildSequence.Dequeue() : BuildToReturn;
        return Task.FromResult(build);
    }

    public Task<GameLiftFleet> CreateFleetAsync(GameLiftCreateFleetRequest request, CancellationToken ct)
    {
        CreateFleetCalls.Add(request);
        return Task.FromResult(FleetToReturn);
    }

    public Task<GameLiftFleet> DescribeFleetAttributesAsync(string fleetId, CancellationToken ct)
    {
        DescribeFleetCalls.Add(fleetId);
        var fleet = DescribeFleetSequence is { Count: > 0 } ? DescribeFleetSequence.Dequeue() : FleetToReturn;
        return Task.FromResult(fleet);
    }

    public Task UpdateFleetPortSettingsAsync(string fleetId, IReadOnlyList<GameLiftPortRange> inboundPermissions, CancellationToken ct)
    {
        UpdatePortsCalls.Add((fleetId, inboundPermissions));
        return Task.CompletedTask;
    }

    public Task<string> CreateAliasAsync(string name, string fleetId, CancellationToken ct)
    {
        CreateAliasCalls.Add((name, fleetId));
        return Task.FromResult(AliasIdToReturn);
    }

    public Task UpdateAliasAsync(string aliasId, string fleetId, CancellationToken ct)
    {
        UpdateAliasCalls.Add((aliasId, fleetId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameLiftEvent>> DescribeFleetEventsAsync(string fleetId, int limit, CancellationToken ct) =>
        Task.FromResult(EventsToReturn);

    public Task DeleteFleetAsync(string fleetId, CancellationToken ct)
    {
        DeleteFleetCalls.Add(fleetId);
        return Task.CompletedTask;
    }

    public Task DeleteAliasAsync(string aliasId, CancellationToken ct)
    {
        DeleteAliasCalls.Add(aliasId);
        return Task.CompletedTask;
    }

    public Task DeleteBuildAsync(string buildId, CancellationToken ct)
    {
        DeleteBuildCalls.Add(buildId);
        return Task.CompletedTask;
    }
}
