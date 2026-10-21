using System.Net;
using System.Text;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Hetzner;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

/// <summary>Routes every request through a canned status code + body instead of a real socket.</summary>
internal sealed class FakeStatusHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public FakeStatusHttpMessageHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
}

[TestFixture]
public class HttpHetznerApiClientTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.hetzner.cloud/v1/") };
    }

    [Test]
    public async Task CreateServerAsync_sends_the_image_region_and_key_then_parses_the_server_back()
    {
        const string response = """
            { "server": { "id": 42, "status": "initializing", "public_net": { "ipv4": { "ip": "203.0.113.9" } } } }
            """;
        var http = MakeClient(_ => response, out var handler);
        var client = new HttpHetznerApiClient(http);

        var server = await client.CreateServerAsync(new CloudVpsCreateRequest("srv", "nbg1", "cpx11", "ubuntu-24.04", "7", "#cloud-init\n"), CancellationToken.None);

        Assert.That(server.Id, Is.EqualTo("42"));
        Assert.That(server.PublicIp, Is.EqualTo("203.0.113.9"));
        Assert.That(server.Status, Is.EqualTo("initializing"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("ubuntu-24.04").And.Contain("nbg1").And.Contain("cpx11"));
    }

    [Test]
    public async Task GetServerAsync_reports_no_ip_until_hetzner_assigns_one()
    {
        const string response = """{ "server": { "id": 42, "status": "initializing", "public_net": { "ipv4": null } } }""";
        var http = MakeClient(_ => response, out _);
        var server = await new HttpHetznerApiClient(http).GetServerAsync("42", CancellationToken.None);
        Assert.That(server.PublicIp, Is.Null);
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var http = new HttpClient(new FakeStatusHttpMessageHandler(HttpStatusCode.Unauthorized, "{\"error\": {\"message\": \"unauthorized\"}}"))
        {
            BaseAddress = new Uri("https://api.hetzner.cloud/v1/"),
        };
        Assert.ThrowsAsync<InvalidOperationException>(() => new HttpHetznerApiClient(http).GetServerAsync("42", CancellationToken.None));
    }
}
