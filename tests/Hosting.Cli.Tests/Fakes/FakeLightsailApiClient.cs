using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="LightsailProvisioner"/> makes and answers canned instances, with no real HTTP.</summary>
public sealed class FakeLightsailApiClient : ILightsailApiClient
{
    public List<LightsailCreateInstanceRequest> CreateCalls { get; } = new();
    public List<string> GetCalls { get; } = new();
    public List<(string Name, IReadOnlyList<LightsailPortRange> Ports)> OpenPortsCalls { get; } = new();
    public List<string> DeleteCalls { get; } = new();

    public LightsailInstance InstanceToReturn { get; set; } = new("srv", "running", "198.51.100.20");
    public Queue<LightsailInstance>? GetSequence { get; set; }

    public Task<LightsailInstance> CreateInstanceAsync(LightsailCreateInstanceRequest request, CancellationToken ct)
    {
        CreateCalls.Add(request);
        return Task.FromResult(InstanceToReturn);
    }

    public Task<LightsailInstance> GetInstanceAsync(string instanceName, CancellationToken ct)
    {
        GetCalls.Add(instanceName);
        var instance = GetSequence is { Count: > 0 } ? GetSequence.Dequeue() : InstanceToReturn;
        return Task.FromResult(instance);
    }

    public Task OpenPublicPortsAsync(string instanceName, IReadOnlyList<LightsailPortRange> ports, CancellationToken ct)
    {
        OpenPortsCalls.Add((instanceName, ports));
        return Task.CompletedTask;
    }

    public Task DeleteInstanceAsync(string instanceName, CancellationToken ct)
    {
        DeleteCalls.Add(instanceName);
        return Task.CompletedTask;
    }
}
