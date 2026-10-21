using System.Text;
using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;

/// <summary>
/// Real <see cref="ILightsailApiClient"/>: POSTs Lightsail's AWS-JSON-1.1 RPC protocol (one
/// <c>X-Amz-Target: Lightsail_20161128.&lt;Action&gt;</c> action per call), signed with
/// <see cref="AwsSigV4Signer"/>. Field names follow Lightsail's publicly documented 2016-11-28 API
/// as of this writing; as with the other UM-T27 HTTP clients, the opt-in real-provider smoke test
/// (<c>docs/articles/hosting/aws-lightsail.md</c>) is what actually guards this against drift.
/// </summary>
public sealed class HttpLightsailApiClient : ILightsailApiClient
{
    private const string TargetPrefix = "Lightsail_20161128.";
    private const string ContentType = "application/x-amz-json-1.1";

    private readonly HttpClient _http;
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly string _region;

    /// <param name="http">An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is <c>https://lightsail.&lt;region&gt;.amazonaws.com/</c>.</param>
    public HttpLightsailApiClient(HttpClient http, string accessKeyId, string secretAccessKey, string region)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _accessKeyId = string.IsNullOrWhiteSpace(accessKeyId) ? throw new ArgumentException("accessKeyId is required.", nameof(accessKeyId)) : accessKeyId;
        _secretAccessKey = string.IsNullOrWhiteSpace(secretAccessKey) ? throw new ArgumentException("secretAccessKey is required.", nameof(secretAccessKey)) : secretAccessKey;
        _region = string.IsNullOrWhiteSpace(region) ? throw new ArgumentException("region is required.", nameof(region)) : region;
    }

    /// <inheritdoc />
    public async Task<LightsailInstance> CreateInstanceAsync(LightsailCreateInstanceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            instanceNames = new[] { request.Name },
            availabilityZone = request.AvailabilityZone,
            blueprintId = request.BlueprintId,
            bundleId = request.BundleId,
            keyPairName = request.KeyPairName,
            userData = request.UserData,
        };
        await PostAsync("CreateInstances", body, ct);
        return new LightsailInstance(request.Name, "pending", null);
    }

    /// <inheritdoc />
    public async Task<LightsailInstance> GetInstanceAsync(string instanceName, CancellationToken ct)
    {
        JsonElement data = await PostAsync("GetInstance", new { instanceName }, ct);
        JsonElement instance = data.GetProperty("instance");
        string name = instance.GetProperty("name").GetString() ?? instanceName;
        string state = instance.TryGetProperty("state", out var s) && s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        string? publicIp = instance.TryGetProperty("publicIpAddress", out var ip) ? ip.GetString() : null;
        return new LightsailInstance(name, state, string.IsNullOrEmpty(publicIp) ? null : publicIp);
    }

    /// <inheritdoc />
    public Task OpenPublicPortsAsync(string instanceName, IReadOnlyList<LightsailPortRange> ports, CancellationToken ct)
    {
        var body = new
        {
            instanceName,
            portInfos = ports.Select(p => new { fromPort = p.FromPort, toPort = p.ToPort, protocol = p.Protocol }).ToArray(),
        };
        return PostAsync("PutInstancePublicPorts", body, ct);
    }

    /// <inheritdoc />
    public Task DeleteInstanceAsync(string instanceName, CancellationToken ct) =>
        PostAsync("DeleteInstance", new { instanceName }, ct);

    private async Task<JsonElement> PostAsync(string action, object body, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(body);
        string host = _http.BaseAddress!.Host;
        var extraHeaders = new Dictionary<string, string>
        {
            ["content-type"] = ContentType,
            ["x-amz-target"] = TargetPrefix + action,
        };
        var signed = AwsSigV4Signer.Sign("POST", host, "/", "", extraHeaders, json, _accessKeyId, _secretAccessKey, _region, "lightsail", DateTime.UtcNow);

        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = new StringContent(json, Encoding.UTF8, ContentType) };
        request.Headers.Host = host;
        request.Headers.Add("X-Amz-Date", signed.AmzDate);
        request.Headers.Add("X-Amz-Target", TargetPrefix + action);
        request.Headers.TryAddWithoutValidation("Authorization", signed.Authorization);

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        string responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseJson) ? "{}" : responseJson);
        if (!response.IsSuccessStatusCode)
        {
            string message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? responseJson : responseJson;
            throw new InvalidOperationException($"Lightsail API error ({action}): {message}");
        }

        return doc.RootElement.Clone();
    }
}
