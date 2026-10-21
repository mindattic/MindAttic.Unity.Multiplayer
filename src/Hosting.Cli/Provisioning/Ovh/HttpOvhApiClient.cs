using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Ovh;

/// <summary>
/// Real <see cref="ICloudVpsApiClient"/> for OVHcloud's Public Cloud instance API: POSTs/GETs JSON
/// against <c>https://&lt;endpoint&gt;.api.ovh.com/1.0/cloud/project/{serviceName}</c>. Unlike the
/// other UM-T26 providers, OVH authenticates every request with a signature
/// (<c>X-Ovh-Signature = "$1$" + sha1(applicationSecret+"+"+consumerKey+"+"+method+"+"+url+"+"+body+"+"+timestamp)</c>)
/// instead of a static bearer header, so this client builds and signs each
/// <see cref="HttpRequestMessage"/> itself rather than relying on <see cref="HttpClient"/> default
/// headers. The exact field names here follow OVH's publicly documented Public Cloud API as of this
/// writing; verify them (and the security-group flow in particular, which is the most
/// OpenStack-shaped part of OVH's API) against OVH's live docs before relying on this for a real
/// deploy -- the opt-in real-provider smoke test is what actually guards this against drift.
/// </summary>
public sealed class HttpOvhApiClient : ICloudVpsApiClient
{
    private readonly HttpClient _http;
    private readonly string _applicationKey;
    private readonly string _applicationSecret;
    private readonly string _consumerKey;
    private readonly string _serviceName;

    /// <param name="http">An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is OVH's regional API endpoint, e.g. <c>https://eu.api.ovh.com/1.0/</c>.</param>
    /// <param name="applicationKey">OVH application key.</param>
    /// <param name="applicationSecret">OVH application secret. Never logged.</param>
    /// <param name="consumerKey">OVH consumer key, validated for the Public Cloud project. Never logged.</param>
    /// <param name="serviceName">The Public Cloud project id (OVH calls it <c>serviceName</c>) every instance/network call is scoped under.</param>
    public HttpOvhApiClient(HttpClient http, string applicationKey, string applicationSecret, string consumerKey, string serviceName)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _applicationKey = applicationKey ?? throw new ArgumentNullException(nameof(applicationKey));
        _applicationSecret = applicationSecret ?? throw new ArgumentNullException(nameof(applicationSecret));
        _consumerKey = consumerKey ?? throw new ArgumentNullException(nameof(consumerKey));
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
    }

    private string ProjectPath => $"cloud/project/{_serviceName}";

    /// <inheritdoc />
    public async Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct)
    {
        JsonElement existing = await SendAsync(HttpMethod.Get, $"{ProjectPath}/sshkey", null, ct);
        foreach (var key in existing.EnumerateArray())
        {
            if (key.TryGetProperty("name", out var n) && n.GetString() == name)
            {
                return key.GetProperty("id").GetString() ?? throw new InvalidOperationException("OVH ssh key id was null.");
            }
        }

        JsonElement created = await SendAsync(HttpMethod.Post, $"{ProjectPath}/sshkey", new { name, publicKey }, ct);
        return created.GetProperty("id").GetString() ?? throw new InvalidOperationException("OVH ssh key id was null.");
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> CreateServerAsync(CloudVpsCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            name = request.Name,
            flavorId = request.Size,
            imageId = request.ImageSlug,
            region = request.Region,
            sshKeyId = request.SshKeyId,
            userData = request.CloudInitUserData,
        };
        JsonElement data = await SendAsync(HttpMethod.Post, $"{ProjectPath}/instance", body, ct);
        return ParseInstance(data);
    }

    /// <inheritdoc />
    public async Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct)
    {
        JsonElement data = await SendAsync(HttpMethod.Get, $"{ProjectPath}/instance/{serverId}", null, ct);
        return ParseInstance(data);
    }

    /// <inheritdoc />
    /// <remarks>
    /// OVH Public Cloud firewalling is OpenStack-shaped security groups, scoped to a region rather
    /// than an instance directly: look the instance's region up, create a security group with one
    /// ingress rule per <paramref name="rules"/> entry (source <c>0.0.0.0/0</c>, matching the other
    /// UM-T26 providers), then attach the group to the instance.
    /// </remarks>
    public async Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct)
    {
        JsonElement instance = await SendAsync(HttpMethod.Get, $"{ProjectPath}/instance/{serverId}", null, ct);
        string region = instance.GetProperty("region").GetString() ?? throw new InvalidOperationException("OVH instance had no region.");

        JsonElement group = await SendAsync(HttpMethod.Post, $"{ProjectPath}/region/{region}/securityGroup", new { name = $"mp-host-{serverId}" }, ct);
        string groupId = group.GetProperty("id").GetString() ?? throw new InvalidOperationException("OVH security group id was null.");

        foreach (var rule in rules)
        {
            var ruleBody = new { direction = "ingress", protocol = rule.Protocol.ToLowerInvariant(), port = rule.Port.ToString(), ipRange = "0.0.0.0/0" };
            await SendAsync(HttpMethod.Post, $"{ProjectPath}/region/{region}/securityGroup/{groupId}/rule", ruleBody, ct);
        }

        await SendAsync(HttpMethod.Post, $"{ProjectPath}/instance/{serverId}/securityGroup/{groupId}", null, ct);
    }

    /// <inheritdoc />
    public async Task DestroyServerAsync(string serverId, CancellationToken ct)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"{ProjectPath}/instance/{serverId}", null, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("(404)", StringComparison.Ordinal))
        {
            // Already gone; destroy is always safe to call twice.
        }
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        string baseUrl = (_http.BaseAddress?.ToString() ?? throw new InvalidOperationException("HttpClient.BaseAddress is required for OVH.")).TrimEnd('/');
        string url = $"{baseUrl}/{path}";
        string json = body is null ? string.Empty : JsonSerializer.Serialize(body);
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string toSign = $"{_applicationSecret}+{_consumerKey}+{method.Method}+{url}+{json}+{timestamp}";
        string signature = "$1$" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(toSign))).ToLowerInvariant();

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        request.Headers.Add("X-Ovh-Application", _applicationKey);
        request.Headers.Add("X-Ovh-Consumer", _consumerKey);
        request.Headers.Add("X-Ovh-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Ovh-Signature", signature);

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"OVH API error ({(int)response.StatusCode}): {errorBody}");
        }

        if (response.Content.Headers.ContentLength is null or 0)
        {
            return default;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    private static CloudVpsServer ParseInstance(JsonElement instance)
    {
        string id = instance.GetProperty("id").GetString() ?? throw new InvalidOperationException("OVH instance id was null.");
        string status = instance.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        string? ip = null;
        if (instance.TryGetProperty("ipAddresses", out var ips) && ips.ValueKind == JsonValueKind.Array)
        {
            foreach (var addr in ips.EnumerateArray())
            {
                if (addr.TryGetProperty("type", out var t) && t.GetString() == "public" && addr.TryGetProperty("ip", out var ipEl))
                {
                    ip = ipEl.GetString();
                    break;
                }
            }
        }

        return new CloudVpsServer(id, ip, status);
    }
}
