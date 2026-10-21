using Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="FlyProvisioner"/> makes and answers canned apps/machines/IPs, with no real HTTP.</summary>
public sealed class FakeFlyApiClient : IFlyApiClient
{
    public List<(string AppName, string OrgSlug)> EnsureAppCalls { get; } = new();
    public List<string> AllocateDedicatedCalls { get; } = new();
    public List<string> AllocateSharedCalls { get; } = new();
    public List<(string AppName, FlyCreateMachineRequest Request)> CreateCalls { get; } = new();
    public List<(string AppName, string MachineId, FlyCreateMachineRequest Request)> UpdateCalls { get; } = new();
    public List<(string AppName, string MachineId)> DestroyMachineCalls { get; } = new();
    public List<string> DestroyAppCalls { get; } = new();

    public FlyIpAddress DedicatedIp { get; set; } = new("ip-1", "198.51.100.10", "v4");
    public FlyIpAddress SharedIp { get; set; } = new("ip-2", "198.51.100.11", "shared_v4");
    public Dictionary<string, List<FlyMachine>> MachinesByApp { get; } = new();
    public Queue<FlyMachine>? GetMachineSequence { get; set; }

    public Task EnsureAppAsync(string appName, string orgSlug, CancellationToken ct)
    {
        EnsureAppCalls.Add((appName, orgSlug));
        return Task.CompletedTask;
    }

    public Task<FlyIpAddress> AllocateDedicatedIpv4Async(string appName, string region, CancellationToken ct)
    {
        AllocateDedicatedCalls.Add(appName);
        return Task.FromResult(DedicatedIp);
    }

    public Task<FlyIpAddress> AllocateSharedIpv4Async(string appName, CancellationToken ct)
    {
        AllocateSharedCalls.Add(appName);
        return Task.FromResult(SharedIp);
    }

    public Task<IReadOnlyList<FlyIpAddress>> ListIpAddressesAsync(string appName, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FlyIpAddress>>(new List<FlyIpAddress> { DedicatedIp });

    public Task<FlyMachine> CreateMachineAsync(string appName, FlyCreateMachineRequest request, CancellationToken ct)
    {
        CreateCalls.Add((appName, request));
        var machine = new FlyMachine($"machine-{CreateCalls.Count}", request.Name, request.ProcessGroup, "started", request.Region);
        if (!MachinesByApp.TryGetValue(appName, out var list))
        {
            list = new List<FlyMachine>();
            MachinesByApp[appName] = list;
        }

        list.Add(machine);
        return Task.FromResult(machine);
    }

    public Task<FlyMachine> GetMachineAsync(string appName, string machineId, CancellationToken ct)
    {
        var machine = GetMachineSequence is { Count: > 0 }
            ? GetMachineSequence.Dequeue()
            : MachinesByApp.TryGetValue(appName, out var list) ? list.First(m => m.Id == machineId) : throw new InvalidOperationException("no machine");
        return Task.FromResult(machine);
    }

    public Task<IReadOnlyList<FlyMachine>> ListMachinesAsync(string appName, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FlyMachine>>(MachinesByApp.TryGetValue(appName, out var list) ? list : new List<FlyMachine>());

    public Task<FlyMachine> UpdateMachineAsync(string appName, string machineId, FlyCreateMachineRequest request, CancellationToken ct)
    {
        UpdateCalls.Add((appName, machineId, request));
        var machine = new FlyMachine(machineId, request.Name, request.ProcessGroup, "started", request.Region);
        if (MachinesByApp.TryGetValue(appName, out var list))
        {
            int index = list.FindIndex(m => m.Id == machineId);
            if (index >= 0) list[index] = machine;
        }

        return Task.FromResult(machine);
    }

    public Task DestroyMachineAsync(string appName, string machineId, CancellationToken ct)
    {
        DestroyMachineCalls.Add((appName, machineId));
        return Task.CompletedTask;
    }

    public Task DestroyAppAsync(string appName, CancellationToken ct)
    {
        DestroyAppCalls.Add(appName);
        return Task.CompletedTask;
    }
}
