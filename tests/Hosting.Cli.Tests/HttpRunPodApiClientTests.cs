using System.Net;
using System.Text;
using Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

/// <summary>Routes every request through a canned response instead of a real socket.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string> _respond;
    private readonly Func<HttpRequestMessage, HttpStatusCode> _status;
    private readonly string _contentType;
    public List<string> RequestBodies { get; } = new();
    public List<string> RequestUris { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string> respond, HttpStatusCode statusCode = HttpStatusCode.OK, string contentType = "application/json")
        : this(respond, _ => statusCode, contentType)
    {
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string> respond, Func<HttpRequestMessage, HttpStatusCode> statusSelector, string contentType = "application/json")
    {
        _respond = respond;
        _status = statusSelector;
        _contentType = contentType;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestUris.Add(request.RequestUri!.ToString());
        if (request.Content is not null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(ct));
        }

        return new HttpResponseMessage(_status(request))
        {
            Content = new StringContent(_respond(request), Encoding.UTF8, _contentType),
        };
    }
}

[TestFixture]
public class HttpRunPodApiClientTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.runpod.io/graphql") };
    }

    [Test]
    public async Task CreatePodAsync_sends_the_image_and_ports_and_parses_the_pod_back()
    {
        const string response = """
            { "data": { "podFindAndDeployOnDemand": { "id": "pod-9", "desiredStatus": "RUNNING", "runtime": null } } }
            """;
        var http = MakeClient(_ => response, out var handler);
        var client = new HttpRunPodApiClient(http);

        var pod = await client.CreatePodAsync(new RunPodCreatePodRequest("srv", "ghcr.io/x/y:v1", new Dictionary<string, string>(), new[] { 7777 }, 5), CancellationToken.None);

        Assert.That(pod.Id, Is.EqualTo("pod-9"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("ghcr.io/x/y:v1"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("7777/tcp"));
    }

    [Test]
    public async Task GetPodAsync_parses_runtime_ports_when_present()
    {
        const string response = """
            { "data": { "pod": { "id": "pod-9", "desiredStatus": "RUNNING",
              "runtime": { "uptimeInSeconds": 30, "ports": [ { "ip": "198.51.100.1", "privatePort": 7777, "publicPort": 37777 } ] } } } }
            """;
        var http = MakeClient(_ => response, out _);
        var pod = await new HttpRunPodApiClient(http).GetPodAsync("pod-9", CancellationToken.None);

        Assert.That(pod.RuntimeStatus, Is.EqualTo("RUNNING"));
        Assert.That(pod.PublicIp, Is.EqualTo("198.51.100.1"));
        Assert.That(pod.PortMappings.Single(), Is.EqualTo(new RunPodPortMapping(7777, 37777)));
    }

    [Test]
    public void A_GraphQL_error_surfaces_as_an_exception()
    {
        const string response = """{ "errors": [ { "message": "invalid api key" } ] }""";
        var http = MakeClient(_ => response, out _);
        Assert.ThrowsAsync<InvalidOperationException>(() => new HttpRunPodApiClient(http).GetPodAsync("pod-9", CancellationToken.None));
    }

    [Test]
    public async Task GetPodLogsAsync_tails_to_the_requested_line_count()
    {
        string logs = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}"));
        string response = $$"""{ "data": { "pod": { "id": "p", "desiredStatus": "RUNNING", "runtime": { "container": { "logs": "{{logs.Replace("\n", "\\n")}}" } } } } }""";
        var http = MakeClient(_ => response, out _);
        string tailed = await new HttpRunPodApiClient(http).GetPodLogsAsync("p", 3, CancellationToken.None);
        Assert.That(tailed, Is.EqualTo("line 8\nline 9\nline 10"));
    }
}
