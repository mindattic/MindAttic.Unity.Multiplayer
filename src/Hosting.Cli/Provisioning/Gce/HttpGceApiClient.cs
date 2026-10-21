using System.Net.Http.Json;
using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

/// <summary>
/// Real <see cref="IGceApiClient"/>: POSTs/GETs the Compute Engine v1 REST API, authenticated with
/// a Bearer access token (see <c>docs/articles/hosting/gce.md</c> for how to mint one -- mp-host
/// does not implement the service-account JWT flow itself, to avoid shipping its own RSA signing
/// code for a token an operator can refresh in one command before each deploy). Field names follow
/// Compute Engine's publicly documented v1 API as of this writing; as with the other UM-T27 HTTP
/// clients, the opt-in real-provider smoke test is what actually guards this against drift.
/// </summary>
public sealed class HttpGceApiClient : IGceApiClient
{
    private readonly HttpClient _http;
    private readonly string _projectId;

    /// <param name="http">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is
    /// <c>https://compute.googleapis.com/compute/v1/</c> and whose default headers already carry
    /// <c>Authorization: Bearer &lt;access token&gt;</c>.
    /// </param>
    public HttpGceApiClient(HttpClient http, string projectId)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _projectId = string.IsNullOrWhiteSpace(projectId) ? throw new ArgumentException("projectId is required.", nameof(projectId)) : projectId;
    }

    /// <inheritdoc />
    public async Task CreateFirewallRuleAsync(string name, string network, string targetTag, IReadOnlyList<GceFirewallAllow> allowed, CancellationToken ct)
    {
        var body = new
        {
            name,
            network,
            direction = "INGRESS",
            targetTags = new[] { targetTag },
            sourceRanges = new[] { "0.0.0.0/0" },
            allowed = allowed.Select(a => new { IPProtocol = a.IpProtocol, ports = a.Ports }).ToArray(),
        };
        await SendAsync(HttpMethod.Post, $"projects/{_projectId}/global/firewalls", body, ct);
    }

    /// <inheritdoc />
    public async Task<GceInstance> InsertInstanceAsync(GceInsertInstanceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            name = request.Name,
            machineType = $"zones/{request.Zone}/machineTypes/{request.MachineType}",
            tags = new { items = new[] { request.NetworkTag } },
            disks = new object[] { new { boot = true, autoDelete = true, initializeParams = new { sourceImage = request.SourceImage } } },
            networkInterfaces = new object[] { new { network = request.Network, accessConfigs = new object[] { new { type = "ONE_TO_ONE_NAT", name = "External NAT" } } } },
            metadata = new
            {
                items = new object[]
                {
                    new { key = "startup-script", value = request.StartupScript },
                    new { key = "ssh-keys", value = $"{request.SshUser}:{request.SshPublicKey}" },
                },
            },
        };
        await SendAsync(HttpMethod.Post, $"projects/{_projectId}/zones/{request.Zone}/instances", body, ct);
        return await GetInstanceAsync(request.Zone, request.Name, ct);
    }

    /// <inheritdoc />
    public async Task<GceInstance> GetInstanceAsync(string zone, string instanceName, CancellationToken ct)
    {
        JsonElement data = await SendAsync(HttpMethod.Get, $"projects/{_projectId}/zones/{zone}/instances/{instanceName}", null, ct);
        string status = data.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        string? natIp = null;
        if (data.TryGetProperty("networkInterfaces", out var nics) && nics.ValueKind == JsonValueKind.Array)
        {
            foreach (var nic in nics.EnumerateArray())
            {
                if (!nic.TryGetProperty("accessConfigs", out var configs) || configs.ValueKind != JsonValueKind.Array) continue;
                foreach (var cfg in configs.EnumerateArray())
                {
                    natIp ??= cfg.TryGetProperty("natIP", out var ip) ? ip.GetString() : null;
                }
            }
        }

        return new GceInstance(instanceName, status, natIp);
    }

    /// <inheritdoc />
    public Task DeleteInstanceAsync(string zone, string instanceName, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"projects/{_projectId}/zones/{zone}/instances/{instanceName}", null, ct);

    /// <inheritdoc />
    public Task DeleteFirewallRuleAsync(string name, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"projects/{_projectId}/global/firewalls/{name}", null, ct);

    private async Task<JsonElement> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        string json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!response.IsSuccessStatusCode)
        {
            string message = doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var m)
                ? m.GetString() ?? json
                : json;
            throw new InvalidOperationException($"GCE API error ({method} {relativeUrl}): {message}");
        }

        return doc.RootElement.Clone();
    }
}
