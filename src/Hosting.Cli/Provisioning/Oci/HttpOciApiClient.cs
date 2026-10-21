using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

/// <summary>
/// Real <see cref="IOciApiClient"/>: calls the Core Services API (<c>iaas.{region}.oraclecloud.com</c>,
/// API version <c>20160918</c>), signing every request with <see cref="OciRequestSigner"/> instead of
/// a bearer token. <see cref="GetInstanceAsync"/> makes three calls under the hood (the instance
/// itself, its VNIC attachments, then the primary VNIC) because OCI, unlike GCE/Azure, does not
/// return an instance's public IP on the instance resource itself.
/// </summary>
public sealed class HttpOciApiClient : IOciApiClient
{
    private const string ApiVersion = "20160918";

    private readonly HttpClient _http;
    private readonly string _compartmentId;
    private readonly string _tenancyOcid;
    private readonly string _userOcid;
    private readonly string _fingerprint;
    private readonly string _privateKeyPem;

    /// <param name="http">An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is <c>https://iaas.{region}.oraclecloud.com/</c>.</param>
    public HttpOciApiClient(HttpClient http, string compartmentId, string tenancyOcid, string userOcid, string fingerprint, string privateKeyPem)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _compartmentId = RequireNonBlank(compartmentId, nameof(compartmentId));
        _tenancyOcid = RequireNonBlank(tenancyOcid, nameof(tenancyOcid));
        _userOcid = RequireNonBlank(userOcid, nameof(userOcid));
        _fingerprint = RequireNonBlank(fingerprint, nameof(fingerprint));
        _privateKeyPem = RequireNonBlank(privateKeyPem, nameof(privateKeyPem));
    }

    /// <inheritdoc />
    public async Task<OciInstance> LaunchInstanceAsync(OciLaunchInstanceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new JsonObject
        {
            ["availabilityDomain"] = request.AvailabilityDomain,
            ["compartmentId"] = request.CompartmentId,
            ["shape"] = request.Shape,
            ["displayName"] = request.DisplayName,
            ["createVnicDetails"] = new JsonObject { ["subnetId"] = request.SubnetId, ["assignPublicIp"] = true },
            ["metadata"] = new JsonObject { ["ssh_authorized_keys"] = request.SshPublicKey, ["user_data"] = request.UserDataBase64 },
            ["sourceDetails"] = new JsonObject { ["sourceType"] = "image", ["imageId"] = request.ImageId },
        };
        if (request.Shape.Contains("Flex", StringComparison.OrdinalIgnoreCase))
        {
            body["shapeConfig"] = new JsonObject { ["ocpus"] = request.Ocpus, ["memoryInGBs"] = request.MemoryInGBs };
        }

        JsonElement created = await SendAsync(HttpMethod.Post, $"{ApiVersion}/instances", body.ToJsonString(), ct);
        string id = created.GetProperty("id").GetString() ?? throw new InvalidOperationException("OCI LaunchInstance response had no 'id'.");
        string state = created.TryGetProperty("lifecycleState", out var s) ? s.GetString() ?? "" : "";
        return new OciInstance(id, state, null);
    }

    /// <inheritdoc />
    public async Task<OciInstance> GetInstanceAsync(string instanceId, CancellationToken ct)
    {
        JsonElement instance = await SendAsync(HttpMethod.Get, $"{ApiVersion}/instances/{instanceId}", null, ct);
        string state = instance.TryGetProperty("lifecycleState", out var s) ? s.GetString() ?? "" : "";

        JsonElement attachments = await SendAsync(HttpMethod.Get,
            $"{ApiVersion}/vnicAttachments?compartmentId={Uri.EscapeDataString(_compartmentId)}&instanceId={Uri.EscapeDataString(instanceId)}", null, ct);
        string? publicIp = null;
        if (attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var attachment in attachments.EnumerateArray())
            {
                if (!attachment.TryGetProperty("vnicId", out var vnicIdProp) || vnicIdProp.ValueKind != JsonValueKind.String) continue;
                string? vnicId = vnicIdProp.GetString();
                if (string.IsNullOrEmpty(vnicId)) continue;
                JsonElement vnic = await SendAsync(HttpMethod.Get, $"{ApiVersion}/vnics/{vnicId}", null, ct);
                if (vnic.TryGetProperty("publicIp", out var ip) && ip.ValueKind == JsonValueKind.String)
                {
                    publicIp ??= ip.GetString();
                }

                break; // the primary VNIC is the only one mp-host attaches.
            }
        }

        return new OciInstance(instanceId, state, publicIp);
    }

    /// <inheritdoc />
    public Task TerminateInstanceAsync(string instanceId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"{ApiVersion}/instances/{instanceId}?preserveBootVolume=false", null, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OciSecurityRule>> GetIngressSecurityRulesAsync(string securityListId, CancellationToken ct)
    {
        JsonElement list = await SendAsync(HttpMethod.Get, $"{ApiVersion}/securityLists/{securityListId}", null, ct);
        var rules = new List<OciSecurityRule>();
        if (!list.TryGetProperty("ingressSecurityRules", out var ingress) || ingress.ValueKind != JsonValueKind.Array)
        {
            return rules;
        }

        foreach (var rule in ingress.EnumerateArray())
        {
            string ociProtocol = rule.TryGetProperty("protocol", out var p) ? p.GetString() ?? "" : "";
            string protocol = FromOciProtocol(ociProtocol);
            int port = 0;
            if (rule.TryGetProperty("tcpOptions", out var tcp) && tcp.TryGetProperty("destinationPortRange", out var tcpRange) && tcpRange.TryGetProperty("min", out var tcpMin))
            {
                port = tcpMin.GetInt32();
            }
            else if (rule.TryGetProperty("udpOptions", out var udp) && udp.TryGetProperty("destinationPortRange", out var udpRange) && udpRange.TryGetProperty("min", out var udpMin))
            {
                port = udpMin.GetInt32();
            }

            string description = rule.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            rules.Add(new OciSecurityRule(protocol, port, description));
        }

        return rules;
    }

    /// <inheritdoc />
    public Task SetIngressSecurityRulesAsync(string securityListId, IReadOnlyList<OciSecurityRule> rules, CancellationToken ct)
    {
        var array = new JsonArray();
        foreach (var rule in rules)
        {
            var entry = new JsonObject
            {
                ["source"] = "0.0.0.0/0",
                ["sourceType"] = "CIDR_BLOCK",
                ["protocol"] = ToOciProtocol(rule.Protocol),
                ["isStateless"] = false,
                ["description"] = rule.Description,
            };
            var portRange = new JsonObject { ["min"] = rule.Port, ["max"] = rule.Port };
            if (rule.Protocol.Equals("udp", StringComparison.OrdinalIgnoreCase))
            {
                entry["udpOptions"] = new JsonObject { ["destinationPortRange"] = portRange };
            }
            else
            {
                entry["tcpOptions"] = new JsonObject { ["destinationPortRange"] = portRange };
            }

            array.Add(entry);
        }

        var body = new JsonObject { ["ingressSecurityRules"] = array };
        return SendAsync(HttpMethod.Put, $"{ApiVersion}/securityLists/{securityListId}", body.ToJsonString(), ct);
    }

    private static string ToOciProtocol(string protocol) => protocol.Equals("udp", StringComparison.OrdinalIgnoreCase) ? "17" : "6";

    private static string FromOciProtocol(string ociProtocol) => ociProtocol == "17" ? "udp" : "tcp";

    private async Task<JsonElement> SendAsync(HttpMethod method, string relativeUrl, string? jsonBody, CancellationToken ct)
    {
        Uri baseAddress = _http.BaseAddress ?? throw new InvalidOperationException("HttpOciApiClient's HttpClient needs a BaseAddress.");
        var uri = new Uri(baseAddress, relativeUrl);
        using var request = new HttpRequestMessage(method, uri);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
        }

        OciRequestSigner.SignedHeaders signed = OciRequestSigner.Sign(
            method.Method, uri.PathAndQuery, uri.Host, jsonBody, _tenancyOcid, _userOcid, _fingerprint, _privateKeyPem, DateTime.UtcNow);
        request.Headers.TryAddWithoutValidation("Date", signed.Date);
        request.Headers.TryAddWithoutValidation("Authorization", signed.Authorization);
        if (signed.XContentSha256 is not null)
        {
            request.Headers.TryAddWithoutValidation("x-content-sha256", signed.XContentSha256);
        }

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        string json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!response.IsSuccessStatusCode)
        {
            string message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? json : json;
            throw new InvalidOperationException($"OCI API error ({method} {relativeUrl}): {message}");
        }

        return doc.RootElement.Clone();
    }

    private static string RequireNonBlank(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{paramName} is required.", paramName) : value;
}
