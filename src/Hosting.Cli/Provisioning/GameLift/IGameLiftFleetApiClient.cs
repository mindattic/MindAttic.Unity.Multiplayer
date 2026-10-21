namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

/// <summary>
/// The slice of GameLift's build/fleet API <see cref="GameLiftProvisioner"/> needs. The only seam
/// between the provisioner and the network, so NUnit mocks it directly instead of faking signed
/// HTTP/JSON (and the raw S3 upload <see cref="UploadBuildAsync"/> stands in for).
/// </summary>
public interface IGameLiftFleetApiClient
{
    /// <summary>Registers a new build and gets back a storage location plus temporary upload credentials for <see cref="UploadBuildAsync"/>.</summary>
    Task<GameLiftBuild> CreateBuildAsync(string name, string version, CancellationToken ct);

    /// <summary>Uploads the zipped server package at <paramref name="zipFilePath"/> to the build's storage location (an S3 <c>PutObject</c>, signed with its temporary credentials).</summary>
    Task UploadBuildAsync(GameLiftBuild build, string zipFilePath, CancellationToken ct);

    /// <summary>The build's current status (<c>INITIALIZED</c> -&gt; <c>READY</c> once GameLift has validated the upload, or <c>FAILED</c>).</summary>
    Task<GameLiftBuild> DescribeBuildAsync(string buildId, CancellationToken ct);

    /// <summary>Creates a fleet of EC2 instances running this server-package build, opening <paramref name="request"/>'s inbound permissions from the start.</summary>
    Task<GameLiftFleet> CreateFleetAsync(GameLiftCreateFleetRequest request, CancellationToken ct);

    /// <summary>The fleet's current status (<c>ACTIVATING</c> -&gt; <c>ACTIVE</c>, or <c>ERROR</c>).</summary>
    Task<GameLiftFleet> DescribeFleetAttributesAsync(string fleetId, CancellationToken ct);

    /// <summary>Replaces a fleet's open inbound permissions with exactly <paramref name="inboundPermissions"/>.</summary>
    Task UpdateFleetPortSettingsAsync(string fleetId, IReadOnlyList<GameLiftPortRange> inboundPermissions, CancellationToken ct);

    /// <summary>Creates a simple-routing alias pointed at <paramref name="fleetId"/>, so a matchmaker (and a later <c>mp-host update</c>) can target the alias instead of a fleet id directly.</summary>
    Task<string> CreateAliasAsync(string name, string fleetId, CancellationToken ct);

    /// <summary>Repoints an existing alias at a new fleet (the blue/green swap <see cref="GameLiftProvisioner.UpdateAsync"/> does).</summary>
    Task UpdateAliasAsync(string aliasId, string fleetId, CancellationToken ct);

    /// <summary>The fleet's recent activation/capacity/error events, newest last -- mp-host's stand-in for container logs, since a fleet has no single process to tail.</summary>
    Task<IReadOnlyList<GameLiftEvent>> DescribeFleetEventsAsync(string fleetId, int limit, CancellationToken ct);

    /// <summary>Deletes a fleet. Safe to call on an already-deleted one.</summary>
    Task DeleteFleetAsync(string fleetId, CancellationToken ct);

    /// <summary>Deletes an alias. Safe to call on an already-deleted one.</summary>
    Task DeleteAliasAsync(string aliasId, CancellationToken ct);

    /// <summary>Deletes a build (and its S3 upload with it). Safe to call on an already-deleted one.</summary>
    Task DeleteBuildAsync(string buildId, CancellationToken ct);
}
