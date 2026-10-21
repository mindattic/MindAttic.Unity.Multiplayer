using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>Dry-run <see cref="IUpnpClient"/>: prints the discovery/SOAP calls it would have made. No socket or HTTP call is made.</summary>
public sealed class DryRunUpnpClient : IUpnpClient
{
    private readonly IDeployLog _log;

    public DryRunUpnpClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<UpnpMappingResult> TryAddPortMappingAsync(int externalPort, int internalPort, string protocol, string description, CancellationToken ct)
    {
        _log.DryRun($"SSDP M-SEARCH 239.255.255.250:1900 then AddPortMapping {externalPort}->{internalPort}/{protocol} ({description})");
        return Task.FromResult(new UpnpMappingResult(true, "203.0.113.60", null));
    }

    /// <inheritdoc />
    public Task TryRemovePortMappingAsync(int externalPort, string protocol, CancellationToken ct)
    {
        _log.DryRun($"DeletePortMapping {externalPort}/{protocol}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// No-op <see cref="IUpnpClient"/> for <c>mp-host local up --no-upnp</c>: the operator already knows
/// their network has no port forwarding (RFC 0001's HOA case) and does not want mp-host to spend
/// three seconds discovering that again on every run.
/// </summary>
public sealed class NullUpnpClient : IUpnpClient
{
    /// <inheritdoc />
    public Task<UpnpMappingResult> TryAddPortMappingAsync(int externalPort, int internalPort, string protocol, string description, CancellationToken ct) =>
        Task.FromResult(new UpnpMappingResult(false, null, "UPnP was skipped (--no-upnp)."));

    /// <inheritdoc />
    public Task TryRemovePortMappingAsync(int externalPort, string protocol, CancellationToken ct) => Task.CompletedTask;
}
