using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

/// <summary>
/// Dry-run <see cref="ICloudVpsApiClient"/> shared by every UM-T26 plain-VPS profile: prints the
/// REST call it would have made (named by <paramref name="providerName"/>/<paramref name="apiBaseUrl"/>
/// at construction) and returns a synthesized server so the rest of the pipeline (firewall, health
/// check, join-code wait, printed summary) still has something to narrate against. No network call
/// is made and no API key is read.
/// </summary>
public sealed class DryRunCloudVpsApiClient : ICloudVpsApiClient
{
    private const string SimulatedServerId = "dryrun-server-id";
    private const string SimulatedSshKeyId = "dryrun-ssh-key-id";
    private const string SimulatedIp = "203.0.113.20";

    private readonly IDeployLog _log;
    private readonly string _providerName;
    private readonly string _apiBaseUrl;

    public DryRunCloudVpsApiClient(IDeployLog log, string providerName, string apiBaseUrl)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _apiBaseUrl = apiBaseUrl ?? throw new ArgumentNullException(nameof(apiBaseUrl));
    }

    /// <inheritdoc />
    public Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct)
    {
        _log.DryRun($"{_providerName}: POST {_apiBaseUrl} register SSH key '{name}'.");
        return Task.FromResult(SimulatedSshKeyId);
    }

    /// <inheritdoc />
    public Task<CloudVpsServer> CreateServerAsync(CloudVpsCreateRequest request, CancellationToken ct)
    {
        var body = new { request.Name, request.Region, request.Size, request.ImageSlug, request.SshKeyId };
        _log.DryRun($"{_providerName}: POST {_apiBaseUrl} create server {JsonSerializer.Serialize(body)}");
        return Task.FromResult(new CloudVpsServer(SimulatedServerId, SimulatedIp, "running"));
    }

    /// <inheritdoc />
    public Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct)
    {
        _log.DryRun($"{_providerName}: GET {_apiBaseUrl} server '{serverId}'.");
        return Task.FromResult(new CloudVpsServer(serverId, SimulatedIp, "running"));
    }

    /// <inheritdoc />
    public Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct)
    {
        _log.DryRun($"{_providerName}: POST {_apiBaseUrl} open {string.Join(", ", rules.Select(r => $"{r.Port}/{r.Protocol}"))} for '{serverId}'.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DestroyServerAsync(string serverId, CancellationToken ct)
    {
        _log.DryRun($"{_providerName}: DELETE {_apiBaseUrl} server '{serverId}'.");
        return Task.CompletedTask;
    }
}
