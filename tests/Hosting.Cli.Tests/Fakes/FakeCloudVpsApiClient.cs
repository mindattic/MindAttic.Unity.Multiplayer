using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="CloudVpsProvisioner"/> makes and answers canned servers, with no real HTTP.</summary>
public sealed class FakeCloudVpsApiClient : ICloudVpsApiClient
{
    public List<(string Name, string PublicKey)> EnsureSshKeyCalls { get; } = new();
    public List<CloudVpsCreateRequest> CreateCalls { get; } = new();
    public List<string> GetCalls { get; } = new();
    public List<(string ServerId, IReadOnlyList<CloudVpsFirewallRule> Rules)> FirewallCalls { get; } = new();
    public List<string> DestroyCalls { get; } = new();

    public string SshKeyIdToReturn { get; set; } = "key-1";
    public CloudVpsServer ServerToCreate { get; set; } = new("server-1", "198.51.100.1", "running");
    public Queue<CloudVpsServer>? GetSequence { get; set; }

    public Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct)
    {
        EnsureSshKeyCalls.Add((name, publicKey));
        return Task.FromResult(SshKeyIdToReturn);
    }

    public Task<CloudVpsServer> CreateServerAsync(CloudVpsCreateRequest request, CancellationToken ct)
    {
        CreateCalls.Add(request);
        return Task.FromResult(ServerToCreate);
    }

    public Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct)
    {
        GetCalls.Add(serverId);
        var server = GetSequence is { Count: > 0 } ? GetSequence.Dequeue() : ServerToCreate;
        return Task.FromResult(server);
    }

    public Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct)
    {
        FirewallCalls.Add((serverId, rules));
        return Task.CompletedTask;
    }

    public Task DestroyServerAsync(string serverId, CancellationToken ct)
    {
        DestroyCalls.Add(serverId);
        return Task.CompletedTask;
    }
}
