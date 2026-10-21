using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Ovh;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

/// <summary>Captures every request sent through it (headers included) instead of opening a real socket.</summary>
internal sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string> _respond;
    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>The request body and headers snapshotted while the request is still live -- the caller disposes it once <see cref="SendAsync"/> returns, so reading <see cref="HttpRequestMessage.Content"/> afterward would throw <see cref="ObjectDisposedException"/>.</summary>
    public List<(HttpMethod Method, Uri? Uri, string Body, HttpRequestHeaders Headers)> Captured { get; } = new();

    public CapturingHttpMessageHandler(Func<HttpRequestMessage, string> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        Captured.Add((request.Method, request.RequestUri, body, request.Headers));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_respond(request), Encoding.UTF8, "application/json"),
        };
    }
}

[TestFixture]
public class HttpOvhApiClientTests
{
    [Test]
    public async Task CreateServerAsync_signs_the_request_with_the_documented_Ovh_formula()
    {
        var handler = new CapturingHttpMessageHandler(_ => """{"id":"vm-1","status":"BUILD","ipAddresses":[]}""");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://eu.api.ovh.com/1.0/") };
        var client = new HttpOvhApiClient(http, "app-key", "app-secret", "consumer-key", "project-1");

        var server = await client.CreateServerAsync(new CloudVpsCreateRequest("srv", "GRA", "d2-2", "Ubuntu 24.04", "key-1", "#cloud-init\n"), CancellationToken.None);

        Assert.That(server.Id, Is.EqualTo("vm-1"));
        var (method, uri, body, headers) = handler.Captured.Single();
        Assert.That(headers.GetValues("X-Ovh-Application").Single(), Is.EqualTo("app-key"));
        Assert.That(headers.GetValues("X-Ovh-Consumer").Single(), Is.EqualTo("consumer-key"));

        string timestamp = headers.GetValues("X-Ovh-Timestamp").Single();
        string toSign = $"app-secret+consumer-key+{method.Method}+{uri}+{body}+{timestamp}";
        string expectedSignature = "$1$" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(toSign))).ToLowerInvariant();
        Assert.That(headers.GetValues("X-Ovh-Signature").Single(), Is.EqualTo(expectedSignature));
    }

    [Test]
    public async Task GetServerAsync_picks_the_public_ip_address()
    {
        const string response = """
            { "id": "vm-1", "status": "ACTIVE", "region": "GRA",
              "ipAddresses": [ { "ip": "10.0.0.4", "type": "private" }, { "ip": "203.0.113.3", "type": "public" } ] }
            """;
        var handler = new CapturingHttpMessageHandler(_ => response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://eu.api.ovh.com/1.0/") };
        var server = await new HttpOvhApiClient(http, "k", "s", "c", "project-1").GetServerAsync("vm-1", CancellationToken.None);
        Assert.That(server.PublicIp, Is.EqualTo("203.0.113.3"));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var http = new HttpClient(new FakeStatusHttpMessageHandler(HttpStatusCode.Forbidden, "{\"message\":\"Invalid signature\"}"))
        {
            BaseAddress = new Uri("https://eu.api.ovh.com/1.0/"),
        };
        Assert.ThrowsAsync<InvalidOperationException>(() => new HttpOvhApiClient(http, "k", "s", "c", "project-1").GetServerAsync("vm-1", CancellationToken.None));
    }
}
