namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>
/// The slice of Fly.io's Machines API (<c>https://api.machines.dev/v1</c>) <see cref="FlyProvisioner"/>
/// needs. The only seam between the provisioner and the network, so NUnit mocks it directly instead
/// of faking HTTP responses.
/// </summary>
public interface IFlyApiClient
{
    /// <summary>Creates the app if it doesn't already exist (idempotent -- an existing app of the same name is left alone).</summary>
    Task EnsureAppAsync(string appName, string orgSlug, CancellationToken ct);

    /// <summary>
    /// Allocates a dedicated IPv4 for <paramref name="appName"/>. Fly's free shared (anycast) IPv4
    /// only proxies HTTP(S)/TCP; a UDP game port needs a dedicated address.
    /// </summary>
    Task<FlyIpAddress> AllocateDedicatedIpv4Async(string appName, string region, CancellationToken ct);

    /// <summary>Allocates Fly's free shared IPv4 for <paramref name="appName"/> -- sufficient for a TCP-only service such as rendezvous.</summary>
    Task<FlyIpAddress> AllocateSharedIpv4Async(string appName, CancellationToken ct);

    /// <summary>Every IP address already allocated to <paramref name="appName"/>.</summary>
    Task<IReadOnlyList<FlyIpAddress>> ListIpAddressesAsync(string appName, CancellationToken ct);

    /// <summary>Creates a new machine in <paramref name="appName"/> running <paramref name="request"/>'s image and config.</summary>
    Task<FlyMachine> CreateMachineAsync(string appName, FlyCreateMachineRequest request, CancellationToken ct);

    /// <summary>The machine's current state.</summary>
    Task<FlyMachine> GetMachineAsync(string appName, string machineId, CancellationToken ct);

    /// <summary>Every machine in <paramref name="appName"/> (both the game and rendezvous process groups, when both exist).</summary>
    Task<IReadOnlyList<FlyMachine>> ListMachinesAsync(string appName, CancellationToken ct);

    /// <summary>Replaces <paramref name="machineId"/>'s config in place (Fly stops then starts it on the new image -- a brief restart, not a swap to a new machine id).</summary>
    Task<FlyMachine> UpdateMachineAsync(string appName, string machineId, FlyCreateMachineRequest request, CancellationToken ct);

    /// <summary>Destroys one machine. Always safe to call on an already-gone machine.</summary>
    Task DestroyMachineAsync(string appName, string machineId, CancellationToken ct);

    /// <summary>Destroys the whole app (and anything still running in it). Always safe to call on an already-gone app.</summary>
    Task DestroyAppAsync(string appName, CancellationToken ct);
}
