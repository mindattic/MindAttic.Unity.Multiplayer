namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

/// <summary>A GameLift build, as reported by <c>CreateBuild</c>/<c>DescribeBuild</c>.</summary>
public sealed record GameLiftBuild(
    string BuildId,
    string Status,
    string? Bucket,
    string? Key,
    string? RoleArn,
    string? UploadAccessKeyId,
    string? UploadSecretAccessKey,
    string? UploadSessionToken);

/// <summary>What to ask GameLift's <c>CreateFleet</c> to create, from an already-READY build.</summary>
public sealed record GameLiftCreateFleetRequest(
    string Name,
    string BuildId,
    string Ec2InstanceType,
    string FleetType,
    string LaunchPath,
    string LaunchParameters,
    int ConcurrentExecutions,
    IReadOnlyList<GameLiftPortRange> InboundPermissions);

/// <summary>A GameLift fleet, as reported by <c>CreateFleet</c>/<c>DescribeFleetAttributes</c>.</summary>
public sealed record GameLiftFleet(string FleetId, string Status, string? FleetArn);

/// <summary>One inbound permission range on a fleet's <c>EC2InboundPermissions</c>.</summary>
public sealed record GameLiftPortRange(int FromPort, int ToPort, string Protocol, string IpRange = "0.0.0.0/0");

/// <summary>One entry of <c>DescribeFleetEvents</c>, mp-host's stand-in for container logs on a fleet.</summary>
public sealed record GameLiftEvent(string EventId, string EventCode, string Message, DateTimeOffset EventTime);
