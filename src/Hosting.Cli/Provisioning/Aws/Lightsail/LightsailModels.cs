namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;

/// <summary>A Lightsail instance, as reported by <c>GetInstance</c>.</summary>
public sealed record LightsailInstance(string Name, string State, string? PublicIp);

/// <summary>What to ask Lightsail's <c>CreateInstances</c> to create.</summary>
public sealed record LightsailCreateInstanceRequest(
    string Name,
    string AvailabilityZone,
    string BlueprintId,
    string BundleId,
    string KeyPairName,
    string UserData);

/// <summary>One port (or range) opened by <c>PutInstancePublicPorts</c>. <paramref name="Protocol"/> is <c>tcp</c>, <c>udp</c> or <c>all</c>.</summary>
public sealed record LightsailPortRange(int FromPort, int ToPort, string Protocol);
