using System.Net.Http.Json;
using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

/// <summary>
/// Real <see cref="IRunPodApiClient"/>: POSTs GraphQL to RunPod's API. The exact mutation/query
/// field names here follow RunPod's publicly documented Pods GraphQL schema as of this writing;
/// RunPod has changed this schema before, so the opt-in real-provider smoke test
/// (<c>docs/articles/hosting/runpod.md</c>) is what actually guards this against drift -- verify
/// it still matches RunPod's live schema before relying on it for a real deploy.
/// </summary>
public sealed class HttpRunPodApiClient : IRunPodApiClient
{
    private readonly HttpClient _http;

    /// <param name="http">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is RunPod's GraphQL
    /// endpoint and whose default headers already carry <c>Authorization: Bearer &lt;api key&gt;</c>.
    /// Construct it from <see cref="Credentials.HostingCredentialStore.GetRunPodCreds"/>'s key; the
    /// key itself never appears in this class.
    /// </param>
    public HttpRunPodApiClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<RunPodPod> CreatePodAsync(RunPodCreatePodRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string mutation = """
            mutation podFindAndDeployOnDemand($input: PodFindAndDeployOnDemandInput!) {
              podFindAndDeployOnDemand(input: $input) {
                id
                desiredStatus
                runtime { ports { ip privatePort publicPort } }
              }
            }
            """;
        var variables = new
        {
            input = new
            {
                name = request.Name,
                imageName = request.ImageName,
                containerDiskInGb = request.ContainerDiskInGb,
                ports = string.Join(",", request.TcpPorts.Select(p => $"{p}/tcp")),
                env = request.Env.Select(kv => new { key = kv.Key, value = kv.Value }).ToArray(),
            },
        };

        JsonElement data = await PostAsync(mutation, variables, "podFindAndDeployOnDemand", ct);
        return ParsePod(data);
    }

    /// <inheritdoc />
    public async Task<RunPodPod> GetPodAsync(string podId, CancellationToken ct)
    {
        const string query = """
            query pod($podId: String!) {
              pod(input: { podId: $podId }) {
                id
                desiredStatus
                runtime { uptimeInSeconds ports { ip privatePort publicPort } }
              }
            }
            """;
        JsonElement data = await PostAsync(query, new { podId }, "pod", ct);
        return ParsePod(data);
    }

    /// <inheritdoc />
    public async Task<string> GetPodLogsAsync(string podId, int tailLines, CancellationToken ct)
    {
        const string query = """
            query podLogs($podId: String!) {
              pod(input: { podId: $podId }) { runtime { container { logs } } }
            }
            """;
        JsonElement data = await PostAsync(query, new { podId }, "pod", ct);
        string logs = data.TryGetProperty("runtime", out var runtime)
            && runtime.TryGetProperty("container", out var container)
            && container.TryGetProperty("logs", out var logsEl)
                ? logsEl.GetString() ?? ""
                : "";
        var lines = logs.Split('\n');
        return string.Join('\n', lines.Length > tailLines ? lines[^tailLines..] : lines);
    }

    /// <inheritdoc />
    public async Task StopPodAsync(string podId, CancellationToken ct)
    {
        const string mutation = "mutation podStop($podId: String!) { podStop(input: { podId: $podId }) { id } }";
        await PostAsync(mutation, new { podId }, "podStop", ct);
    }

    /// <inheritdoc />
    public async Task TerminatePodAsync(string podId, CancellationToken ct)
    {
        const string mutation = "mutation podTerminate($podId: String!) { podTerminate(input: { podId: $podId }) }";
        await PostAsync(mutation, new { podId }, "podTerminate", ct);
    }

    private async Task<JsonElement> PostAsync(string query, object variables, string dataField, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(string.Empty, new { query, variables }, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            throw new InvalidOperationException($"RunPod API error: {errors[0].GetProperty("message").GetString()}");
        }

        return root.GetProperty("data").GetProperty(dataField).Clone();
    }

    private static RunPodPod ParsePod(JsonElement data)
    {
        string id = data.GetProperty("id").GetString() ?? "";
        string desired = data.TryGetProperty("desiredStatus", out var d) ? d.GetString() ?? "" : "";
        string? runtimeStatus = null;
        string? publicIp = null;
        var mappings = new List<RunPodPortMapping>();
        if (data.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
        {
            runtimeStatus = "RUNNING";
            if (runtime.TryGetProperty("ports", out var ports) && ports.ValueKind == JsonValueKind.Array)
            {
                foreach (var port in ports.EnumerateArray())
                {
                    publicIp ??= port.TryGetProperty("ip", out var ip) ? ip.GetString() : null;
                    int priv = port.GetProperty("privatePort").GetInt32();
                    int pub = port.GetProperty("publicPort").GetInt32();
                    mappings.Add(new RunPodPortMapping(priv, pub));
                }
            }
        }

        return new RunPodPod(id, desired, runtimeStatus, publicIp, mappings);
    }
}
