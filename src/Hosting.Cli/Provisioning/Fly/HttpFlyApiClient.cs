using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>
/// Real <see cref="IFlyApiClient"/>: POSTs/GETs JSON against Fly.io's Machines API
/// (<c>https://api.machines.dev/v1</c>). The exact field names here follow Fly's publicly documented
/// Machines API as of this writing; Fly has changed field shapes before, so the opt-in real-provider
/// smoke test (<c>tests/Hosting.Cli.Tests/Smoke/FlySmokeTests.cs</c>) is what actually guards this
/// against drift -- verify it still matches Fly's live schema before relying on it for a real deploy.
/// </summary>
public sealed class HttpFlyApiClient : IFlyApiClient
{
    private readonly HttpClient _http;

    /// <param name="http">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is Fly's Machines API
    /// root (<c>https://api.machines.dev/v1/</c>) and whose default headers already carry
    /// <c>Authorization: Bearer &lt;Fly API token&gt;</c>. Construct it from
    /// <see cref="Credentials.HostingCredentialStore.GetFlyCreds"/>; the token itself never appears
    /// in this class.
    /// </param>
    public HttpFlyApiClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task EnsureAppAsync(string appName, string orgSlug, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.PostAsJsonAsync("apps", new { app_name = appName, org_slug = orgSlug }, ct);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            // Fly reports the app name as already taken; treat that as "already exists" the same
            // way the plain-VPS providers treat a duplicate SSH key name -- idempotent, not a failure.
            return;
        }

        await EnsureSuccessAsync(response, ct);
    }

    /// <inheritdoc />
    public async Task<FlyIpAddress> AllocateDedicatedIpv4Async(string appName, string region, CancellationToken ct) =>
        await AllocateIpAsync(appName, new { type = "v4", region }, ct);

    /// <inheritdoc />
    public async Task<FlyIpAddress> AllocateSharedIpv4Async(string appName, CancellationToken ct) =>
        await AllocateIpAsync(appName, new { type = "shared_v4" }, ct);

    private async Task<FlyIpAddress> AllocateIpAsync(string appName, object body, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.PostAsJsonAsync($"apps/{appName}/ips", body, ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement data = await ReadAsync(response, ct);
        return ParseIp(data);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlyIpAddress>> ListIpAddressesAsync(string appName, CancellationToken ct)
    {
        JsonElement data = await GetAsync($"apps/{appName}/ips", ct);
        var ips = new List<FlyIpAddress>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var ip in data.EnumerateArray())
            {
                ips.Add(ParseIp(ip));
            }
        }

        return ips;
    }

    /// <inheritdoc />
    public async Task<FlyMachine> CreateMachineAsync(string appName, FlyCreateMachineRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        HttpResponseMessage response = await _http.PostAsJsonAsync($"apps/{appName}/machines", BuildMachineBody(request), ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement data = await ReadAsync(response, ct);
        return ParseMachine(data);
    }

    /// <inheritdoc />
    public async Task<FlyMachine> GetMachineAsync(string appName, string machineId, CancellationToken ct)
    {
        JsonElement data = await GetAsync($"apps/{appName}/machines/{machineId}", ct);
        return ParseMachine(data);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlyMachine>> ListMachinesAsync(string appName, CancellationToken ct)
    {
        JsonElement data = await GetAsync($"apps/{appName}/machines", ct);
        var machines = new List<FlyMachine>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var machine in data.EnumerateArray())
            {
                machines.Add(ParseMachine(machine));
            }
        }

        return machines;
    }

    /// <inheritdoc />
    public async Task<FlyMachine> UpdateMachineAsync(string appName, string machineId, FlyCreateMachineRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        HttpResponseMessage response = await _http.PostAsJsonAsync($"apps/{appName}/machines/{machineId}", BuildMachineBody(request), ct);
        await EnsureSuccessAsync(response, ct);
        JsonElement data = await ReadAsync(response, ct);
        return ParseMachine(data);
    }

    /// <inheritdoc />
    public async Task DestroyMachineAsync(string appName, string machineId, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"apps/{appName}/machines/{machineId}?force=true", ct);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    /// <inheritdoc />
    public async Task DestroyAppAsync(string appName, CancellationToken ct)
    {
        HttpResponseMessage response = await _http.DeleteAsync($"apps/{appName}", ct);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    private static object BuildMachineBody(FlyCreateMachineRequest request) => new
    {
        name = request.Name,
        region = request.Region,
        config = new
        {
            image = request.Image,
            guest = new { cpu_kind = request.Guest.CpuKind, cpus = request.Guest.Cpus, memory_mb = request.Guest.MemoryMb },
            env = request.Env,
            metadata = new { fly_process_group = request.ProcessGroup },
            services = request.Services.Select(s => new
            {
                internal_port = s.InternalPort,
                protocol = s.Protocol,
                ports = new[] { new { port = s.ExternalPort } },
            }).ToArray(),
        },
    };

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
        throw new InvalidOperationException($"Fly Machines API error ({(int)response.StatusCode}): {body}");
    }

    private static FlyIpAddress ParseIp(JsonElement data)
    {
        string id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        string address = data.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? "" : "";
        string type = data.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
        return new FlyIpAddress(id, address, type);
    }

    private static FlyMachine ParseMachine(JsonElement data)
    {
        string id = data.GetProperty("id").GetString() ?? "";
        string name = data.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        string state = data.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
        string? region = data.TryGetProperty("region", out var r) ? r.GetString() : null;
        string? processGroup = data.TryGetProperty("config", out var config)
            && config.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("fly_process_group", out var pg)
                ? pg.GetString()
                : null;
        return new FlyMachine(id, name, processGroup, state, region);
    }
}
