using System.Text;
using System.Xml.Linq;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;

/// <summary>
/// Real <see cref="IEc2ApiClient"/>: POSTs AWS's Query API (form-encoded <c>Action=...</c>, XML
/// response), signed with <see cref="AwsSigV4Signer"/>. Field names follow EC2's publicly
/// documented 2016-11-15 API version as of this writing; as with
/// <c>Provisioning.RunPod.HttpRunPodApiClient</c>, the opt-in real-provider smoke test
/// (<c>docs/articles/hosting/aws-ec2.md</c>) is what actually guards this against drift.
/// </summary>
public sealed class HttpEc2ApiClient : IEc2ApiClient
{
    private const string ApiVersion = "2016-11-15";
    private const string ContentType = "application/x-www-form-urlencoded; charset=utf-8";

    private readonly HttpClient _http;
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly string _region;

    /// <param name="http">An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is <c>https://ec2.&lt;region&gt;.amazonaws.com/</c>.</param>
    public HttpEc2ApiClient(HttpClient http, string accessKeyId, string secretAccessKey, string region)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _accessKeyId = string.IsNullOrWhiteSpace(accessKeyId) ? throw new ArgumentException("accessKeyId is required.", nameof(accessKeyId)) : accessKeyId;
        _secretAccessKey = string.IsNullOrWhiteSpace(secretAccessKey) ? throw new ArgumentException("secretAccessKey is required.", nameof(secretAccessKey)) : secretAccessKey;
        _region = string.IsNullOrWhiteSpace(region) ? throw new ArgumentException("region is required.", nameof(region)) : region;
    }

    /// <inheritdoc />
    public async Task<string> CreateSecurityGroupAsync(string name, string? vpcId, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["Action"] = "CreateSecurityGroup",
            ["Version"] = ApiVersion,
            ["GroupName"] = name,
            ["GroupDescription"] = $"mp-host game server: {name}",
        };
        if (!string.IsNullOrEmpty(vpcId)) form["VpcId"] = vpcId;
        XElement root = await PostAsync(form, ct);
        return RequireValue(root, "groupId");
    }

    /// <inheritdoc />
    public async Task AuthorizeIngressAsync(string groupId, IReadOnlyList<Ec2IngressRule> rules, CancellationToken ct)
    {
        var form = new Dictionary<string, string> { ["Action"] = "AuthorizeSecurityGroupIngress", ["Version"] = ApiVersion, ["GroupId"] = groupId };
        for (int i = 0; i < rules.Count; i++)
        {
            string n = (i + 1).ToString();
            form[$"IpPermissions.{n}.IpProtocol"] = rules[i].Protocol;
            form[$"IpPermissions.{n}.FromPort"] = rules[i].Port.ToString();
            form[$"IpPermissions.{n}.ToPort"] = rules[i].Port.ToString();
            form[$"IpPermissions.{n}.IpRanges.1.CidrIp"] = "0.0.0.0/0";
        }

        await PostAsync(form, ct);
    }

    /// <inheritdoc />
    public async Task<Ec2Instance> RunInstanceAsync(Ec2RunInstanceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var form = new Dictionary<string, string>
        {
            ["Action"] = "RunInstances",
            ["Version"] = ApiVersion,
            ["ImageId"] = request.ImageId,
            ["InstanceType"] = request.InstanceType,
            ["MinCount"] = "1",
            ["MaxCount"] = "1",
            ["KeyName"] = request.KeyName,
            ["SecurityGroupId.1"] = request.SecurityGroupId,
            ["UserData"] = request.UserDataBase64,
            ["TagSpecification.1.ResourceType"] = "instance",
            ["TagSpecification.1.Tag.1.Key"] = "Name",
            ["TagSpecification.1.Tag.1.Value"] = request.Name,
        };
        if (!string.IsNullOrEmpty(request.SubnetId)) form["SubnetId"] = request.SubnetId;

        XElement root = await PostAsync(form, ct);
        return ParseInstance(SingleInstanceItem(root));
    }

    /// <inheritdoc />
    public async Task<Ec2Instance> DescribeInstanceAsync(string instanceId, CancellationToken ct)
    {
        var form = new Dictionary<string, string> { ["Action"] = "DescribeInstances", ["Version"] = ApiVersion, ["InstanceId.1"] = instanceId };
        XElement root = await PostAsync(form, ct);
        return ParseInstance(SingleInstanceItem(root));
    }

    /// <inheritdoc />
    public Task TerminateInstanceAsync(string instanceId, CancellationToken ct) =>
        PostAsync(new Dictionary<string, string> { ["Action"] = "TerminateInstances", ["Version"] = ApiVersion, ["InstanceId.1"] = instanceId }, ct);

    /// <inheritdoc />
    public Task DeleteSecurityGroupAsync(string groupId, CancellationToken ct) =>
        PostAsync(new Dictionary<string, string> { ["Action"] = "DeleteSecurityGroup", ["Version"] = ApiVersion, ["GroupId"] = groupId }, ct);

    private static XElement SingleInstanceItem(XElement root)
    {
        XNamespace ns = root.Name.Namespace;
        return root.Descendants(ns + "instancesSet").Elements(ns + "item").First();
    }

    private static Ec2Instance ParseInstance(XElement item)
    {
        XNamespace ns = item.Name.Namespace;
        string instanceId = item.Element(ns + "instanceId")?.Value ?? "";
        string state = item.Element(ns + "instanceState")?.Element(ns + "name")?.Value ?? "";
        string? publicIp = item.Element(ns + "ipAddress")?.Value;
        return new Ec2Instance(instanceId, state, string.IsNullOrEmpty(publicIp) ? null : publicIp);
    }

    private static string RequireValue(XElement root, string elementName)
    {
        XNamespace ns = root.Name.Namespace;
        return root.Element(ns + elementName)?.Value
            ?? throw new InvalidOperationException($"EC2 response had no <{elementName}>: {root}");
    }

    private async Task<XElement> PostAsync(IReadOnlyDictionary<string, string> form, CancellationToken ct)
    {
        string body = string.Join('&', form.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        string host = _http.BaseAddress!.Host;
        var signed = AwsSigV4Signer.Sign(
            "POST", host, "/", "", new Dictionary<string, string> { ["content-type"] = ContentType },
            body, _accessKeyId, _secretAccessKey, _region, "ec2", DateTime.UtcNow);

        using var request = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Host = host;
        request.Headers.Add("X-Amz-Date", signed.AmzDate);
        request.Headers.TryAddWithoutValidation("Authorization", signed.Authorization);

        HttpResponseMessage response = await _http.SendAsync(request, ct);
        string xml = await response.Content.ReadAsStringAsync(ct);
        XDocument doc = XDocument.Parse(xml);
        if (!response.IsSuccessStatusCode)
        {
            string message = doc.Root?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value ?? xml;
            throw new InvalidOperationException($"EC2 API error: {message}");
        }

        return doc.Root ?? throw new InvalidOperationException("EC2 response had no root element.");
    }
}
