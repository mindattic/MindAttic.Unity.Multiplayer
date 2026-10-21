using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Hetzner;

/// <summary>
/// Real <see cref="ICloudVpsApiClient"/> for Hetzner Cloud: POSTs/GETs JSON against
/// <c>https://api.hetzner.cloud/v1</c>. The exact field names here follow Hetzner's publicly
/// documented Cloud API as of this writing; Hetzner has changed field shapes before, so the opt-in
/// real-provider smoke test is what actually guards this against drift -- verify it still matches
/// Hetzner's live schema before relying on it for a real deploy.
/// </summary>
public sealed class HttpHetznerApiClient : ICloudVpsApiClient
{
    private readonly HttpClient _http;

    /// <param name="http">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is Hetzner's API root
    /// (<c>https://api.hetzner.cloud/v1/</c>) and whose default headers already carry
    /// <c>Authorization: Bearer &lt;token&gt;</c>. Construct it from
    /// <see cref="Credentials.HostingCredentialStore.GetHetznerCreds"/>; the token itself never
    /// appears in this class.
    /// </param>
    public HttpHetznerApiClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct)
    {
        JsonElement existing = await GetAsync($"ssh_keys?name={Uri.EscapeDataString(name)}", ct);
        if (existing.TryGetProperty("ssh_keys", out var keys) && keys.ValueKind == JsonValueKind.Array && keys.GetArrayLength() > 0)
        {
            return keys[0].GetProperty("id").GetRawText();
        }

        HttpResponseMessage response = await _http.PostAsJsonAsync("ssh_keys", new { name, public_key = publicKey }, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            JsonElement retry = await GetAsync($"ssh_keys?name={Uri.EscapeDataString(name)}", ct);
            return retry.GetProperty("ssh_keys")[0].GetProperty("id").GetRawText();
        }

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
            server_type = request.Size,
            image = request.ImageSlug,
            location = request.Region,
            ssh_keys = new object[] { ParseId(request.SshKeyId) },
            user_data = request.CloudInitUserData,
        };
        HttpResponseMessage response = await _http.PostAsJsonAsync("servers", body, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement data = await ReadAsync(response, ct);
        return ParseServer(data.GetProperty("server"));
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct)
    {
        JsonElement data = await GetAsync($"servers/{serverId}", ct);
        return ParseServer(data.GetProperty("server"));
    }

    /// <inheritdoc />
    public async Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct)
    {
        var body = new
        {
            name = $"mp-host-{serverId}",
            rules = rules.Select(r => new
            {
                direction = "in",
                protocol = r.Protocol.ToLowerInvariant(),
                port = r.Port.ToString(),
                source_ips = new[] { "0.0.0.0/0", "::/0" },
            }).ToArray(),
        };
        HttpResponseMessage response = await _http.PostAsJsonAsync("firewalls", body, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement created = await ReadAsync(response, ct);
        string firewallId = created.GetProperty("firewall").GetProperty("id").GetRawText();

        var apply = new { apply_to = new object[] { new { type = "server", server = new { id = ParseId(serverId) } } } };
        HttpResponseMessage applyResponse = await _http.PostAsJsonAsync($"firewalls/{firewallId}/actions/apply_to_resources", apply, ct);
        await EnsureSuccessAsync(applyResponse, ct);
    }

    /// <inheritdoc />
    public async Task DestroyServerAsync(string serverId, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"servers/{serverId}", ct);
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
        throw new InvalidOperationException($"Hetzner API error ({(int)response.StatusCode}): {body}");
    }

    private static CloudVpsServer ParseServer(JsonElement server)
    {
        string id = server.GetProperty("id").GetRawText();
        string status = server.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        string? ip = server.TryGetProperty("public_net", out var net)
            && net.TryGetProperty("ipv4", out var ipv4)
            && ipv4.ValueKind == JsonValueKind.Object
            && ipv4.TryGetProperty("ip", out var ipEl)
                ? ipEl.GetString()
                : null;
        return new CloudVpsServer(id, ip, status);
    }

    private static long ParseId(string raw) =>
        long.TryParse(raw, out long id) ? id : throw new FormatException($"Expected a numeric Hetzner id, got '{raw}'.");
}
