using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Linode;

/// <summary>
/// Real <see cref="ICloudVpsApiClient"/> for Linode (Akamai Cloud Computing): POSTs/GETs JSON
/// against <c>https://api.linode.com/v4</c>. The exact field names here follow Linode's publicly
/// documented API as of this writing; Linode has changed field shapes before, so the opt-in
/// real-provider smoke test is what actually guards this against drift -- verify it still matches
/// Linode's live schema before relying on it for a real deploy.
///
/// <para>Unlike the other UM-T26 providers, Linode has no separate "register an SSH key" resource
/// for instance creation -- the public key text itself goes straight into the instance's
/// <c>authorized_keys</c> at create time. <see cref="EnsureSshKeyAsync"/> is therefore a no-op that
/// hands the key text straight back as the opaque id <see cref="CreateServerAsync"/> expects.
/// User-data (cloud-init) requires Linode's Metadata service, supported on current Ubuntu images.</para>
/// </summary>
public sealed class HttpLinodeApiClient : ICloudVpsApiClient
{
    private readonly HttpClient _http;

    /// <param name="http">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is Linode's API root
    /// (<c>https://api.linode.com/v4/</c>) and whose default headers already carry
    /// <c>Authorization: Bearer &lt;token&gt;</c>. Construct it from
    /// <see cref="Credentials.HostingCredentialStore.GetLinodeCreds"/>; the token itself never
    /// appears in this class.
    /// </param>
    public HttpLinodeApiClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct) => Task.FromResult(publicKey);

    /// <inheritdoc />
    public async Task<CloudVpsServer> CreateServerAsync(CloudVpsCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            region = request.Region,
            type = request.Size,
            image = request.ImageSlug,
            label = request.Name,
            authorized_keys = new[] { request.SshKeyId },
            booted = true,
            metadata = new { user_data = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.CloudInitUserData)) },
        };
        HttpResponseMessage response = await _http.PostAsJsonAsync("linode/instances", body, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement data = await ReadAsync(response, ct);
        return ParseInstance(data);
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct)
    {
        JsonElement data = await GetAsync($"linode/instances/{serverId}", ct);
        return ParseInstance(data);
    }

    /// <inheritdoc />
    public async Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct)
    {
        var body = new
        {
            label = $"mp-host-{serverId}",
            rules = new
            {
                inbound = rules.Select(r => new
                {
                    protocol = r.Protocol.ToUpperInvariant(),
                    ports = r.Port.ToString(),
                    addresses = new { ipv4 = new[] { "0.0.0.0/0" }, ipv6 = new[] { "::/0" } },
                    action = "ACCEPT",
                }).ToArray(),
                inbound_policy = "DROP",
                outbound_policy = "ACCEPT",
            },
            devices = new { linodes = new object[] { ParseId(serverId) } },
        };
        HttpResponseMessage response = await _http.PostAsJsonAsync("networking/firewalls", body, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <inheritdoc />
    public async Task DestroyServerAsync(string serverId, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"linode/instances/{serverId}", ct);
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
        throw new InvalidOperationException($"Linode API error ({(int)response.StatusCode}): {body}");
    }

    private static CloudVpsServer ParseInstance(JsonElement instance)
    {
        string id = instance.GetProperty("id").GetRawText();
        string status = instance.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        string? ip = instance.TryGetProperty("ipv4", out var ipv4) && ipv4.ValueKind == JsonValueKind.Array && ipv4.GetArrayLength() > 0
            ? ipv4[0].GetString()
            : null;
        return new CloudVpsServer(id, ip, status);
    }

    private static long ParseId(string raw) =>
        long.TryParse(raw, out long id) ? id : throw new FormatException($"Expected a numeric Linode id, got '{raw}'.");
}
