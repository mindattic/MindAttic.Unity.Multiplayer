using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

/// <summary>
/// Dry-run <see cref="IGceApiClient"/>: prints the Compute Engine call it would have made and
/// returns a synthesized instance so the rest of the pipeline still has something to narrate
/// against. No network call is made and no access token is read.
/// </summary>
public sealed class DryRunGceApiClient : IGceApiClient
{
    private const string SimulatedIp = "203.0.113.40";
    private readonly IDeployLog _log;

    public DryRunGceApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task CreateFirewallRuleAsync(string name, string network, string targetTag, IReadOnlyList<GceFirewallAllow> allowed, CancellationToken ct)
    {
        _log.DryRun($"POST https://compute.googleapis.com/compute/v1/projects/{{project}}/global/firewalls name={name} targetTag={targetTag} allowed={JsonSerializer.Serialize(allowed)}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<GceInstance> InsertInstanceAsync(GceInsertInstanceRequest request, CancellationToken ct)
    {
        _log.DryRun($"POST https://compute.googleapis.com/compute/v1/projects/{{project}}/zones/{request.Zone}/instances name={request.Name} machineType={request.MachineType} sourceImage={request.SourceImage}");
        return Task.FromResult(new GceInstance(request.Name, "RUNNING", SimulatedIp));
    }

    /// <inheritdoc />
    public Task<GceInstance> GetInstanceAsync(string zone, string instanceName, CancellationToken ct)
    {
        _log.DryRun($"GET https://compute.googleapis.com/compute/v1/projects/{{project}}/zones/{zone}/instances/{instanceName}");
        return Task.FromResult(new GceInstance(instanceName, "RUNNING", SimulatedIp));
    }

    /// <inheritdoc />
    public Task DeleteInstanceAsync(string zone, string instanceName, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://compute.googleapis.com/compute/v1/projects/{{project}}/zones/{zone}/instances/{instanceName}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteFirewallRuleAsync(string name, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://compute.googleapis.com/compute/v1/projects/{{project}}/global/firewalls/{name}");
        return Task.CompletedTask;
    }
}
