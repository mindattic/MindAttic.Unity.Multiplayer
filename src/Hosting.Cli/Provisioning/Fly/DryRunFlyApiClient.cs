using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>
/// Dry-run <see cref="IFlyApiClient"/>: prints the Machines API call it would have made and returns
/// synthesized apps/machines/IPs so the rest of the pipeline (health check, join-code wait, printed
/// summary) still has something to narrate against. No network call is made and no API token is read.
/// </summary>
public sealed class DryRunFlyApiClient : IFlyApiClient
{
    private const string SimulatedDedicatedIp = "203.0.113.30";
    private const string SimulatedSharedIp = "203.0.113.31";

    private readonly IDeployLog _log;

    public DryRunFlyApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task EnsureAppAsync(string appName, string orgSlug, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.machines.dev/v1/apps {{ app_name: \"{appName}\", org_slug: \"{orgSlug}\" }}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<FlyIpAddress> AllocateDedicatedIpv4Async(string appName, string region, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.machines.dev/v1/apps/{appName}/ips {{ type: \"v4\", region: \"{region}\" }}");
        return Task.FromResult(new FlyIpAddress("dryrun-ip-v4", SimulatedDedicatedIp, "v4"));
    }

    /// <inheritdoc />
    public Task<FlyIpAddress> AllocateSharedIpv4Async(string appName, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.machines.dev/v1/apps/{appName}/ips {{ type: \"shared_v4\" }}");
        return Task.FromResult(new FlyIpAddress("dryrun-ip-shared", SimulatedSharedIp, "shared_v4"));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FlyIpAddress>> ListIpAddressesAsync(string appName, CancellationToken ct)
    {
        _log.DryRun($"GET https://api.machines.dev/v1/apps/{appName}/ips");
        return Task.FromResult<IReadOnlyList<FlyIpAddress>>(new List<FlyIpAddress> { new("dryrun-ip-v4", SimulatedDedicatedIp, "v4") });
    }

    /// <inheritdoc />
    public Task<FlyMachine> CreateMachineAsync(string appName, FlyCreateMachineRequest request, CancellationToken ct)
    {
        var body = new { request.Name, request.Region, request.ProcessGroup, request.Image, services = request.Services };
        _log.DryRun($"POST https://api.machines.dev/v1/apps/{appName}/machines {JsonSerializer.Serialize(body)}");
        return Task.FromResult(new FlyMachine($"dryrun-machine-{request.ProcessGroup}", request.Name, request.ProcessGroup, "started", request.Region));
    }

    /// <inheritdoc />
    public Task<FlyMachine> GetMachineAsync(string appName, string machineId, CancellationToken ct)
    {
        _log.DryRun($"GET https://api.machines.dev/v1/apps/{appName}/machines/{machineId}");
        return Task.FromResult(new FlyMachine(machineId, appName, "app", "started", null));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FlyMachine>> ListMachinesAsync(string appName, CancellationToken ct)
    {
        _log.DryRun($"GET https://api.machines.dev/v1/apps/{appName}/machines");
        return Task.FromResult<IReadOnlyList<FlyMachine>>(new List<FlyMachine> { new("dryrun-machine-app", appName, "app", "started", null) });
    }

    /// <inheritdoc />
    public Task<FlyMachine> UpdateMachineAsync(string appName, string machineId, FlyCreateMachineRequest request, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.machines.dev/v1/apps/{appName}/machines/{machineId} (new image {request.Image})");
        return Task.FromResult(new FlyMachine(machineId, request.Name, request.ProcessGroup, "started", request.Region));
    }

    /// <inheritdoc />
    public Task DestroyMachineAsync(string appName, string machineId, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://api.machines.dev/v1/apps/{appName}/machines/{machineId}?force=true");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DestroyAppAsync(string appName, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://api.machines.dev/v1/apps/{appName}");
        return Task.CompletedTask;
    }
}
