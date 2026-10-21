using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Vultr;

/// <summary>
/// Real <see cref="ICloudVpsApiClient"/> for Vultr: POSTs/GETs JSON against
/// <c>https://api.vultr.com/v2</c>. The exact field names here follow Vultr's publicly documented
/// API as of this writing; Vultr has changed field shapes before, so the opt-in real-provider smoke
/// test is what actually guards this against drift -- verify it still matches Vultr's live schema
/// before relying on it for a real deploy. Vultr attaches a firewall group to an instance by id
/// rather than at a separate "apply" step, so <see cref="EnsureFirewallAsync"/> creates the group,
/// fills in its rules, then <c>PATCH</c>es the instance onto it.
/// </summary>
public sealed class HttpVultrApiClient : ICloudVpsApiClient
{
    private readonly HttpClient _http;

    /// <param name="http">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is Vultr's API root
    /// (<c>https://api.vultr.com/v2/</c>) and whose default headers already carry
    /// <c>Authorization: Bearer &lt;api key&gt;</c>. Construct it from
    /// <see cref="Credentials.HostingCredentialStore.GetVultrCreds"/>; the key itself never appears
    /// in this class.
    /// </param>
    public HttpVultrApiClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct)
    {
        JsonElement existing = await GetAsync("ssh-keys", ct);
        if (existing.TryGetProperty("ssh_keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var key in keys.EnumerateArray())
            {
                if (key.TryGetProperty("name", out var n) && n.GetString() == name)
                {
                    return key.GetProperty("id").GetString() ?? throw new InvalidOperationException("Vultr ssh key id was null.");
                }
            }
        }

        HttpResponseMessage response = await _http.PostAsJsonAsync("ssh-keys", new { name, ssh_key = publicKey }, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement created = await ReadAsync(response, ct);
        return created.GetProperty("ssh_key").GetProperty("id").GetString() ?? throw new InvalidOperationException("Vultr ssh key id was null.");
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> CreateServerAsync(CloudVpsCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            region = request.Region,
            plan = request.Size,
            os_id = int.TryParse(request.ImageSlug, out int osId) ? osId : throw new FormatException($"Expected a numeric Vultr os_id, got '{request.ImageSlug}'."),
            label = request.Name,
            hostname = request.Name,
            sshkey_id = new[] { request.SshKeyId },
            user_data = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.CloudInitUserData)),
            backups = "disabled",
        };
        HttpResponseMessage response = await _http.PostAsJsonAsync("instances", body, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement data = await ReadAsync(response, ct);
        return ParseInstance(data.GetProperty("instance"));
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct)
    {
        JsonElement data = await GetAsync($"instances/{serverId}", ct);
        return ParseInstance(data.GetProperty("instance"));
    }

    /// <inheritdoc />
    public async Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct)
    {
        HttpResponseMessage groupResponse = await _http.PostAsJsonAsync("firewalls", new { description = $"mp-host-{serverId}" }, ct);
        await EnsureSuccessAsync(groupResponse, ct);
        JsonElement group = await ReadAsync(groupResponse, ct);
        string groupId = group.GetProperty("firewall_group").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Vultr firewall group id was null.");

        foreach (var rule in rules)
        {
            var ruleBody = new { ip_type = "v4", protocol = rule.Protocol.ToLowerInvariant(), subnet = "0.0.0.0", subnet_size = 0, port = rule.Port.ToString() };
            HttpResponseMessage ruleResponse = await _http.PostAsJsonAsync($"firewalls/{groupId}/rules", ruleBody, ct);
            await EnsureSuccessAsync(ruleResponse, ct);
        }

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"instances/{serverId}")
        {
            Content = JsonContent.Create(new { firewall_group_id = groupId }),
        };
        HttpResponseMessage attachResponse = await _http.SendAsync(patch, ct);
        await EnsureSuccessAsync(attachResponse, ct);
    }

    /// <inheritdoc />
    public async Task DestroyServerAsync(string serverId, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"instances/{serverId}", ct);
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
        throw new InvalidOperationException($"Vultr API error ({(int)response.StatusCode}): {body}");
    }

    private static CloudVpsServer ParseInstance(JsonElement instance)
    {
        string id = instance.GetProperty("id").GetString() ?? throw new InvalidOperationException("Vultr instance id was null.");
        string status = instance.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        string? ip = instance.TryGetProperty("main_ip", out var ipEl) ? ipEl.GetString() : null;
        return new CloudVpsServer(id, string.IsNullOrEmpty(ip) || ip == "0.0.0.0" ? null : ip, status);
    }
}
