using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;

/// <summary>
/// Dry-run <see cref="ILightsailApiClient"/>: prints the Lightsail action it would have made and
/// returns a synthesized instance so the rest of the pipeline still has something to narrate
/// against. No network call is made and no AWS credential is read.
/// </summary>
public sealed class DryRunLightsailApiClient : ILightsailApiClient
{
    private const string SimulatedIp = "203.0.113.30";
    private readonly IDeployLog _log;

    public DryRunLightsailApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<LightsailInstance> CreateInstanceAsync(LightsailCreateInstanceRequest request, CancellationToken ct)
    {
        _log.DryRun($"POST https://lightsail.{{region}}.amazonaws.com/ Lightsail_20161128.CreateInstances name={request.Name} blueprint={request.BlueprintId} bundle={request.BundleId}");
        return Task.FromResult(new LightsailInstance(request.Name, "running", SimulatedIp));
    }

    /// <inheritdoc />
    public Task<LightsailInstance> GetInstanceAsync(string instanceName, CancellationToken ct)
    {
        _log.DryRun($"POST https://lightsail.{{region}}.amazonaws.com/ Lightsail_20161128.GetInstance instanceName={instanceName}");
        return Task.FromResult(new LightsailInstance(instanceName, "running", SimulatedIp));
    }

    /// <inheritdoc />
    public Task OpenPublicPortsAsync(string instanceName, IReadOnlyList<LightsailPortRange> ports, CancellationToken ct)
    {
        _log.DryRun($"POST https://lightsail.{{region}}.amazonaws.com/ Lightsail_20161128.PutInstancePublicPorts instanceName={instanceName} ports={JsonSerializer.Serialize(ports)}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteInstanceAsync(string instanceName, CancellationToken ct)
    {
        _log.DryRun($"POST https://lightsail.{{region}}.amazonaws.com/ Lightsail_20161128.DeleteInstance instanceName={instanceName}");
        return Task.CompletedTask;
    }
}
