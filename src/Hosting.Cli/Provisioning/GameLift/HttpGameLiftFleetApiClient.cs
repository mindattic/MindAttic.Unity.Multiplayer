using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

/// <summary>
/// Real <see cref="IGameLiftFleetApiClient"/>: POSTs GameLift's AWS-JSON-1.1 RPC protocol (one
/// <c>X-Amz-Target: GameLift.&lt;Action&gt;</c> action per call) to
/// <c>https://gamelift.&lt;region&gt;.amazonaws.com/</c>, signed with <see cref="AwsSigV4Signer"/>;
/// <see cref="UploadBuildAsync"/> is the one call that leaves GameLift's own API, PUTting the zipped
/// package straight to the S3 location <c>CreateBuild</c> handed back, signed with the build's own
/// temporary upload credentials (never the long-term account key). Field names follow GameLift's
/// publicly documented API as of this writing; as with mp-host's other UM-T27 HTTP clients, the
/// opt-in real-provider smoke test (<c>docs/articles/hosting/gamelift.md</c>) is what actually
/// guards this against drift.
/// </summary>
public sealed class HttpGameLiftFleetApiClient : IGameLiftFleetApiClient
{
    private const string TargetPrefix = "GameLift.";
    private const string ContentType = "application/x-amz-json-1.1";

    private readonly HttpClient _http;
    private readonly HttpClient _s3;
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly string _region;

    /// <param name="http">An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is <c>https://gamelift.&lt;region&gt;.amazonaws.com/</c>.</param>
    /// <param name="s3">A plain <see cref="HttpClient"/> (no base address needed) used only for <see cref="UploadBuildAsync"/>'s S3 PUT.</param>
    public HttpGameLiftFleetApiClient(HttpClient http, HttpClient s3, string accessKeyId, string secretAccessKey, string region)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _s3 = s3 ?? throw new ArgumentNullException(nameof(s3));
        _accessKeyId = string.IsNullOrWhiteSpace(accessKeyId) ? throw new ArgumentException("accessKeyId is required.", nameof(accessKeyId)) : accessKeyId;
        _secretAccessKey = string.IsNullOrWhiteSpace(secretAccessKey) ? throw new ArgumentException("secretAccessKey is required.", nameof(secretAccessKey)) : secretAccessKey;
        _region = string.IsNullOrWhiteSpace(region) ? throw new ArgumentException("region is required.", nameof(region)) : region;
    }

    /// <inheritdoc />
    public async Task<GameLiftBuild> CreateBuildAsync(string name, string version, CancellationToken ct)
    {
        JsonElement data = await PostAsync("CreateBuild", new { Name = name, Version = version, OperatingSystem = "AMAZON_LINUX_2023" }, ct);
        JsonElement build = data.GetProperty("Build");
        JsonElement storage = data.GetProperty("StorageLocation");
        JsonElement creds = data.GetProperty("UploadCredentials");
        return new GameLiftBuild(
            build.GetProperty("BuildId").GetString() ?? "",
            build.TryGetProperty("Status", out var s) ? s.GetString() ?? "INITIALIZED" : "INITIALIZED",
            storage.TryGetProperty("Bucket", out var b) ? b.GetString() : null,
            storage.TryGetProperty("Key", out var k) ? k.GetString() : null,
            storage.TryGetProperty("RoleArn", out var r) ? r.GetString() : null,
            creds.TryGetProperty("AccessKeyId", out var ak) ? ak.GetString() : null,
            creds.TryGetProperty("SecretAccessKey", out var sk) ? sk.GetString() : null,
            creds.TryGetProperty("SessionToken", out var st) ? st.GetString() : null);
    }

    /// <inheritdoc />
    public async Task UploadBuildAsync(GameLiftBuild build, string zipFilePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (build.Bucket is null || build.Key is null || build.UploadAccessKeyId is null || build.UploadSecretAccessKey is null)
        {
            throw new InvalidOperationException($"Build {build.BuildId} has no S3 storage location/upload credentials to upload to.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(zipFilePath, ct);
        string host = $"{build.Bucket}.s3.{_region}.amazonaws.com";
        string canonicalUri = "/" + Uri.EscapeDataString(build.Key).Replace("%2F", "/");
        string payloadHash = AwsSigV4Signer.Sha256Hex(bytes);
        var extraHeaders = new Dictionary<string, string> { ["x-amz-content-sha256"] = payloadHash };
        if (build.UploadSessionToken is not null)
        {
            extraHeaders["x-amz-security-token"] = build.UploadSessionToken;
        }

        var signed = AwsSigV4Signer.SignWithPayloadHash(
            "PUT", host, canonicalUri, "", extraHeaders, payloadHash,
            build.UploadAccessKeyId, build.UploadSecretAccessKey, _region, "s3", DateTime.UtcNow);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"https://{host}{canonicalUri}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Headers.Host = host;
        request.Headers.Add("X-Amz-Date", signed.AmzDate);
        request.Headers.Add("X-Amz-Content-Sha256", payloadHash);
        if (build.UploadSessionToken is not null)
        {
            request.Headers.Add("X-Amz-Security-Token", build.UploadSessionToken);
        }

        request.Headers.TryAddWithoutValidation("Authorization", signed.Authorization);

        HttpResponseMessage response = await _s3.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            string text = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"S3 upload of build {build.BuildId} failed (HTTP {(int)response.StatusCode}): {text}");
        }
    }

    /// <inheritdoc />
    public async Task<GameLiftBuild> DescribeBuildAsync(string buildId, CancellationToken ct)
    {
        JsonElement data = await PostAsync("DescribeBuild", new { BuildId = buildId }, ct);
        JsonElement build = data.GetProperty("Build");
        return new GameLiftBuild(buildId, build.TryGetProperty("Status", out var s) ? s.GetString() ?? "" : "", null, null, null, null, null, null);
    }

    /// <inheritdoc />
    public async Task<GameLiftFleet> CreateFleetAsync(GameLiftCreateFleetRequest request, CancellationToken ct)
    {
        var body = new
        {
            Name = request.Name,
            BuildId = request.BuildId,
            EC2InstanceType = request.Ec2InstanceType,
            FleetType = request.FleetType,
            RuntimeConfiguration = new
            {
                ServerProcesses = new[]
                {
                    new
                    {
                        LaunchPath = request.LaunchPath,
                        Parameters = request.LaunchParameters,
                        ConcurrentExecutions = request.ConcurrentExecutions,
                    },
                },
            },
            EC2InboundPermissions = request.InboundPermissions.Select(p => new
            {
                FromPort = p.FromPort,
                ToPort = p.ToPort,
                IpRange = p.IpRange,
                Protocol = p.Protocol.ToUpperInvariant(),
            }).ToArray(),
        };
        JsonElement data = await PostAsync("CreateFleet", body, ct);
        JsonElement attributes = data.GetProperty("FleetAttributes");
        return new GameLiftFleet(
            attributes.GetProperty("FleetId").GetString() ?? "",
            attributes.TryGetProperty("Status", out var s) ? s.GetString() ?? "NEW" : "NEW",
            attributes.TryGetProperty("FleetArn", out var arn) ? arn.GetString() : null);
    }

    /// <inheritdoc />
    public async Task<GameLiftFleet> DescribeFleetAttributesAsync(string fleetId, CancellationToken ct)
    {
        JsonElement data = await PostAsync("DescribeFleetAttributes", new { FleetIds = new[] { fleetId } }, ct);
        JsonElement list = data.GetProperty("FleetAttributes");
        if (list.GetArrayLength() == 0)
        {
            return new GameLiftFleet(fleetId, "TERMINATED", null);
        }

        JsonElement attributes = list[0];
        return new GameLiftFleet(
            fleetId,
            attributes.TryGetProperty("Status", out var s) ? s.GetString() ?? "" : "",
            attributes.TryGetProperty("FleetArn", out var arn) ? arn.GetString() : null);
    }

    /// <inheritdoc />
    public Task UpdateFleetPortSettingsAsync(string fleetId, IReadOnlyList<GameLiftPortRange> inboundPermissions, CancellationToken ct)
    {
        var body = new
        {
            FleetId = fleetId,
            InboundPermissionAuthorizations = inboundPermissions.Select(p => new
            {
                FromPort = p.FromPort,
                ToPort = p.ToPort,
                IpRange = p.IpRange,
                Protocol = p.Protocol.ToUpperInvariant(),
            }).ToArray(),
        };
        return PostAsync("UpdateFleetPortSettings", body, ct);
    }

    /// <inheritdoc />
    public async Task<string> CreateAliasAsync(string name, string fleetId, CancellationToken ct)
    {
        var body = new { Name = name, RoutingStrategy = new { Type = "SIMPLE", FleetId = fleetId } };
        JsonElement data = await PostAsync("CreateAlias", body, ct);
        return data.GetProperty("Alias").GetProperty("AliasId").GetString() ?? "";
    }

    /// <inheritdoc />
    public Task UpdateAliasAsync(string aliasId, string fleetId, CancellationToken ct)
    {
        var body = new { AliasId = aliasId, RoutingStrategy = new { Type = "SIMPLE", FleetId = fleetId } };
        return PostAsync("UpdateAlias", body, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameLiftEvent>> DescribeFleetEventsAsync(string fleetId, int limit, CancellationToken ct)
    {
        JsonElement data = await PostAsync("DescribeFleetEvents", new { FleetId = fleetId, Limit = limit }, ct);
        var events = new List<GameLiftEvent>();
        if (data.TryGetProperty("Events", out var list))
        {
            foreach (JsonElement e in list.EnumerateArray())
            {
                string id = e.TryGetProperty("EventId", out var eid) ? eid.GetString() ?? "" : "";
                string code = e.TryGetProperty("EventCode", out var ec) ? ec.GetString() ?? "" : "";
                string message = e.TryGetProperty("Message", out var m) ? m.GetString() ?? "" : "";
                DateTimeOffset time = e.TryGetProperty("EventTime", out var t) && t.TryGetDouble(out var epoch)
                    ? DateTimeOffset.FromUnixTimeSeconds((long)epoch)
                    : DateTimeOffset.UtcNow;
                events.Add(new GameLiftEvent(id, code, message, time));
            }
        }

        return events;
    }

    /// <inheritdoc />
    public Task DeleteFleetAsync(string fleetId, CancellationToken ct) => PostAllowNotFoundAsync("DeleteFleet", new { FleetId = fleetId }, ct);

    /// <inheritdoc />
    public Task DeleteAliasAsync(string aliasId, CancellationToken ct) => PostAllowNotFoundAsync("DeleteAlias", new { AliasId = aliasId }, ct);

    /// <inheritdoc />
    public Task DeleteBuildAsync(string buildId, CancellationToken ct) => PostAllowNotFoundAsync("DeleteBuild", new { BuildId = buildId }, ct);

    private async Task PostAllowNotFoundAsync(string action, object body, CancellationToken ct)
    {
        try
        {
            await PostAsync(action, body, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
        {
            // Already gone; every delete here is safe to call twice.
        }
    }

    private async Task<JsonElement> PostAsync(string action, object body, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(body);
        string host = _http.BaseAddress!.Host;
        var extraHeaders = new Dictionary<string, string>
        {
            ["content-type"] = ContentType,
            ["x-amz-target"] = TargetPrefix + action,
        };
        var signed = AwsSigV4Signer.Sign("POST", host, "/", "", extraHeaders, json, _accessKeyId, _secretAccessKey, _region, "gamelift", DateTime.UtcNow);

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
            throw new InvalidOperationException($"GameLift API error ({action}): {message}");
        }

        return doc.RootElement.Clone();
    }
}
