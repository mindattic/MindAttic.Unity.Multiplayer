using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.DigitalOcean;

/// <summary>
/// Real <see cref="ICloudVpsApiClient"/> for DigitalOcean: POSTs/GETs JSON against
/// <c>https://api.digitalocean.com/v2</c>. The exact field names here follow DigitalOcean's publicly
/// documented API as of this writing; DigitalOcean has changed field shapes before, so the opt-in
/// real-provider smoke test is what actually guards this against drift -- verify it still matches
/// DigitalOcean's live schema before relying on it for a real deploy.
/// </summary>
public sealed class HttpDigitalOceanApiClient : ICloudVpsApiClient
{
    private readonly HttpClient _http;

    /// <param name="http">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is DigitalOcean's API
    /// root (<c>https://api.digitalocean.com/v2/</c>) and whose default headers already carry
    /// <c>Authorization: Bearer &lt;token&gt;</c>. Construct it from
    /// <see cref="Credentials.HostingCredentialStore.GetDigitalOceanCreds"/>; the token itself never
    /// appears in this class.
    /// </param>
    public HttpDigitalOceanApiClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct)
    {
        JsonElement existing = await GetAsync("account/keys", ct);
        if (existing.TryGetProperty("ssh_keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var key in keys.EnumerateArray())
            {
                if (key.TryGetProperty("name", out var n) && n.GetString() == name)
                {
                    return key.GetProperty("id").GetRawText();
                }
            }
        }

        HttpResponseMessage response = await _http.PostAsJsonAsync("account/keys", new { name, public_key = publicKey }, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement created = await ReadAsync(response, ct);
        return created.GetProperty("ssh_key").GetProperty("id").GetRawText();
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> CreateServerAsync(CloudVpsCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            name = request.Name,
            region = request.Region,
            size = request.Size,
            image = request.ImageSlug,
            ssh_keys = new object[] { ParseId(request.SshKeyId) },
            user_data = request.CloudInitUserData,
        };
        HttpResponseMessage response = await _http.PostAsJsonAsync("droplets", body, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement data = await ReadAsync(response, ct);
        return ParseDroplet(data.GetProperty("droplet"));
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct)
    {
        JsonElement data = await GetAsync($"droplets/{serverId}", ct);
        return ParseDroplet(data.GetProperty("droplet"));
    }

    /// <inheritdoc />
    public async Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct)
    {
        var body = new
        {
            name = $"mp-host-{serverId}",
            inbound_rules = rules.Select(r => new
            {
                protocol = r.Protocol.ToLowerInvariant(),
                ports = r.Port.ToString(),
                sources = new { addresses = new[] { "0.0.0.0/0", "::/0" } },
            }).ToArray(),
            outbound_rules = Array.Empty<object>(),
            droplet_ids = new object[] { ParseId(serverId) },
        };
        HttpResponseMessage response = await _http.PostAsJsonAsync("firewalls", body, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <inheritdoc />
    public async Task DestroyServerAsync(string serverId, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"droplets/{serverId}", ct);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadAsync(response, ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"DigitalOcean API error ({(int)response.StatusCode}): {body}");
    }

    private static CloudVpsServer ParseDroplet(JsonElement droplet)
    {
        string id = droplet.GetProperty("id").GetRawText();
        string status = droplet.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        string? ip = null;
        if (droplet.TryGetProperty("networks", out var networks) && networks.TryGetProperty("v4", out var v4) && v4.ValueKind == JsonValueKind.Array)
        {
            foreach (var net in v4.EnumerateArray())
            {
                if (net.TryGetProperty("type", out var t) && t.GetString() == "public" && net.TryGetProperty("ip_address", out var ipEl))
                {
                    ip = ipEl.GetString();
                    break;
                }
            }
        }

        return new CloudVpsServer(id, ip, status);
    }

    private static long ParseId(string raw) =>
        long.TryParse(raw, out long id) ? id : throw new FormatException($"Expected a numeric DigitalOcean id, got '{raw}'.");
}
