namespace Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

/// <summary>One declared port, mapped by RunPod to a public port at pod creation.</summary>
public sealed record RunPodPortMapping(int ContainerPort, int PublicPort);

/// <summary>A RunPod pod, as reported by the GraphQL API.</summary>
public sealed record RunPodPod(string Id, string DesiredStatus, string? RuntimeStatus, string? PublicIp, IReadOnlyList<RunPodPortMapping> PortMappings);

/// <summary>What to ask RunPod to create.</summary>
public sealed record RunPodCreatePodRequest(
    string Name,
    string ImageName,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<int> TcpPorts,
    int ContainerDiskInGb);
