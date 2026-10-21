using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;

/// <summary>
/// Real <see cref="IAzureVmApiClient"/>: fetches an OAuth2 client-credentials token from Microsoft
/// Entra ID (cached until it's within a minute of expiring) and PUTs/GETs/DELETEs Azure Resource
/// Manager's REST API for the four resources a VM needs (NSG, public IP, NIC, VM). Field names and
/// api-versions follow ARM's publicly documented Network/Compute providers as of this writing; as
/// with the other UM-T27 HTTP clients, the opt-in real-provider smoke test is what actually guards
/// this against drift.
/// </summary>
public sealed class HttpAzureVmApiClient : IAzureVmApiClient
{
    private const string ManagementBaseUrl = "https://management.azure.com";
    private const string NetworkApiVersion = "2023-09-01";
    private const string ComputeApiVersion = "2023-09-01";

    private readonly HttpClient _http;
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _subscriptionId;
    private string? _cachedToken;
    private DateTime _tokenExpiresUtc = DateTime.MinValue;

    /// <param name="http">A bare <see cref="HttpClient"/> (no preset auth header -- this class fetches and attaches its own token per call).</param>
    public HttpAzureVmApiClient(HttpClient http, string tenantId, string clientId, string clientSecret, string subscriptionId)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tenantId = Require(tenantId, nameof(tenantId));
        _clientId = Require(clientId, nameof(clientId));
        _clientSecret = Require(clientSecret, nameof(clientSecret));
        _subscriptionId = Require(subscriptionId, nameof(subscriptionId));
    }

    /// <inheritdoc />
    public async Task<string> CreateOrUpdateNetworkSecurityGroupAsync(string resourceGroup, string nsgName, string location, IReadOnlyList<AzureSecurityRule> rules, CancellationToken ct)
    {
        var body = new
        {
            location,
            properties = new
            {
                securityRules = rules.Select(r => new
                {
                    name = r.Name,
                    properties = new
                    {
                        priority = r.Priority,
                        direction = "Inbound",
                        access = "Allow",
                        protocol = r.Protocol,
                        sourcePortRange = "*",
                        destinationPortRange = r.Port.ToString(),
                        sourceAddressPrefix = "*",
                        destinationAddressPrefix = "*",
                    },
                }).ToArray(),
            },
        };
        JsonElement data = await SendAsync(HttpMethod.Put, NetworkPath(resourceGroup, "networkSecurityGroups", nsgName), NetworkApiVersion, body, ct);
        return data.GetProperty("id").GetString() ?? "";
    }

    /// <inheritdoc />
    public async Task<string> CreateOrUpdatePublicIpAsync(string resourceGroup, string ipName, string location, CancellationToken ct)
    {
        var body = new { location, sku = new { name = "Standard" }, properties = new { publicIPAllocationMethod = "Static" } };
        JsonElement data = await SendAsync(HttpMethod.Put, NetworkPath(resourceGroup, "publicIPAddresses", ipName), NetworkApiVersion, body, ct);
        return data.GetProperty("id").GetString() ?? "";
    }

    /// <inheritdoc />
    public async Task<string?> GetPublicIpAddressAsync(string resourceGroup, string ipName, CancellationToken ct)
    {
        JsonElement data = await SendAsync(HttpMethod.Get, NetworkPath(resourceGroup, "publicIPAddresses", ipName), NetworkApiVersion, null, ct);
        return data.TryGetProperty("properties", out var props) && props.TryGetProperty("ipAddress", out var ip) ? ip.GetString() : null;
    }

    /// <inheritdoc />
    public async Task<string> CreateOrUpdateNetworkInterfaceAsync(string resourceGroup, string nicName, string location, string subnetId, string publicIpId, string nsgId, CancellationToken ct)
    {
        var body = new
        {
            location,
            properties = new
            {
                ipConfigurations = new object[]
                {
                    new
                    {
                        name = "ipconfig1",
                        properties = new
                        {
                            subnet = new { id = subnetId },
                            publicIPAddress = new { id = publicIpId },
                            privateIPAllocationMethod = "Dynamic",
                        },
                    },
                },
                networkSecurityGroup = new { id = nsgId },
            },
        };
        JsonElement data = await SendAsync(HttpMethod.Put, NetworkPath(resourceGroup, "networkInterfaces", nicName), NetworkApiVersion, body, ct);
        return data.GetProperty("id").GetString() ?? "";
    }

    /// <inheritdoc />
    public async Task<AzureVmInfo> CreateOrUpdateVirtualMachineAsync(AzureCreateVmRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new
        {
            location = request.Location,
            properties = new
            {
                hardwareProfile = new { vmSize = request.VmSize },
                storageProfile = new
                {
                    imageReference = new
                    {
                        publisher = request.Image.Publisher,
                        offer = request.Image.Offer,
                        sku = request.Image.Sku,
                        version = request.Image.Version,
                    },
                },
                osProfile = new
                {
                    computerName = request.Name,
                    adminUsername = request.AdminUsername,
                    customData = request.CustomDataBase64,
                    linuxConfiguration = new
                    {
                        disablePasswordAuthentication = true,
                        ssh = new
                        {
                            publicKeys = new object[]
                            {
                                new { path = $"/home/{request.AdminUsername}/.ssh/authorized_keys", keyData = request.SshPublicKey },
                            },
                        },
                    },
                },
                networkProfile = new { networkInterfaces = new object[] { new { id = request.NetworkInterfaceId } } },
            },
        };
        JsonElement data = await SendAsync(HttpMethod.Put, ComputePath(request.ResourceGroup, request.Name), ComputeApiVersion, body, ct);
        return ParseVm(request.Name, data);
    }

    /// <inheritdoc />
    public async Task<AzureVmInfo> GetVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct)
    {
        JsonElement data = await SendAsync(HttpMethod.Get, ComputePath(resourceGroup, vmName), ComputeApiVersion, null, ct, "$expand=instanceView");
        return ParseVm(vmName, data);
    }

    /// <inheritdoc />
    public Task DeleteVirtualMachineAsync(string resourceGroup, string vmName, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, ComputePath(resourceGroup, vmName), ComputeApiVersion, null, ct);

    /// <inheritdoc />
    public Task DeleteNetworkInterfaceAsync(string resourceGroup, string nicName, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, NetworkPath(resourceGroup, "networkInterfaces", nicName), NetworkApiVersion, null, ct);

    /// <inheritdoc />
    public Task DeletePublicIpAsync(string resourceGroup, string ipName, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, NetworkPath(resourceGroup, "publicIPAddresses", ipName), NetworkApiVersion, null, ct);

    /// <inheritdoc />
    public Task DeleteNetworkSecurityGroupAsync(string resourceGroup, string nsgName, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, NetworkPath(resourceGroup, "networkSecurityGroups", nsgName), NetworkApiVersion, null, ct);

    private string NetworkPath(string resourceGroup, string resourceType, string name) =>
        $"/subscriptions/{_subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Network/{resourceType}/{name}";

    private string ComputePath(string resourceGroup, string vmName) =>
        $"/subscriptions/{_subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/virtualMachines/{vmName}";

    private static AzureVmInfo ParseVm(string name, JsonElement data)
    {
        string provisioningState = "";
        string? powerState = null;
        if (data.TryGetProperty("properties", out var props))
        {
            provisioningState = props.TryGetProperty("provisioningState", out var ps) ? ps.GetString() ?? "" : "";
            if (props.TryGetProperty("instanceView", out var view) && view.TryGetProperty("statuses", out var statuses))
            {
                foreach (var status in statuses.EnumerateArray())
                {
                    string code = status.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                    if (code.StartsWith("PowerState/", StringComparison.Ordinal))
                    {
                        powerState = code["PowerState/".Length..];
                    }
                }
            }
        }

        return new AzureVmInfo(name, provisioningState, powerState);
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, string? apiVersion, object? body, CancellationToken ct, string? extraQuery = null)
    {
        string separator = path.Contains('?') ? "&" : "?";
        string url = apiVersion is null ? $"{ManagementBaseUrl}{path}" : $"{ManagementBaseUrl}{path}{separator}api-version={apiVersion}";
        if (extraQuery is not null)
        {
            url += (url.Contains('?') ? "&" : "?") + extraQuery;
        }

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(ct));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        string json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!response.IsSuccessStatusCode)
        {
            string message = doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var m)
                ? m.GetString() ?? json
                : json;
            throw new InvalidOperationException($"Azure API error ({method} {path}): {message}");
        }

        return doc.RootElement.Clone();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresUtc)
        {
            return _cachedToken;
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["scope"] = "https://management.azure.com/.default",
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(form),
        };
        HttpResponseMessage response = await _http.SendAsync(request, ct);
        string json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!response.IsSuccessStatusCode)
        {
            string message = doc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() ?? json : json;
            throw new InvalidOperationException($"Azure AD token request failed: {message}");
        }

        _cachedToken = doc.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Azure AD token response had no access_token.");
        int expiresInSeconds = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        _tokenExpiresUtc = DateTime.UtcNow + TimeSpan.FromSeconds(expiresInSeconds) - TimeSpan.FromMinutes(1);
        return _cachedToken;
    }

    private static string Require(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{paramName} is required.", paramName) : value;
}
