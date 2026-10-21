namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>A Fly.io Machines API "guest" (VM) size: CPU kind/count plus memory.</summary>
public sealed record FlyGuest(string CpuKind, int Cpus, int MemoryMb);

/// <summary>One port Fly should route to a machine, exactly as its Machines API <c>services</c> shape expects.</summary>
public sealed record FlyServicePort(int InternalPort, string Protocol, int ExternalPort);

/// <summary>What to ask Fly to create or replace a machine with.</summary>
public sealed record FlyCreateMachineRequest(
    string Name,
    string Region,
    string Image,
    string ProcessGroup,
    FlyGuest Guest,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<FlyServicePort> Services);

/// <summary>A Fly Machine, as reported back by the Machines API.</summary>
public sealed record FlyMachine(string Id, string Name, string? ProcessGroup, string State, string? Region);

/// <summary>An IP address allocated to a Fly app (<c>v4</c> dedicated or <c>shared_v4</c>).</summary>
public sealed record FlyIpAddress(string Id, string Address, string Type);
