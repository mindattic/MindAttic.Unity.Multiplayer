using System.Net;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Vultr;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpVultrApiClientTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.vultr.com/v2/") };
    }

    [Test]
    public async Task CreateServerAsync_base64_encodes_the_cloud_init_user_data()
    {
        const string response = """{ "instance": { "id": "abc-123", "status": "pending", "main_ip": "0.0.0.0" } }""";
        var http = MakeClient(_ => response, out var handler);
        var client = new HttpVultrApiClient(http);

        var server = await client.CreateServerAsync(new CloudVpsCreateRequest("srv", "ewr", "vc2-1c-1gb", "1743", "key-1", "#cloud-init\nhello"), CancellationToken.None);

        Assert.That(server.Id, Is.EqualTo("abc-123"));
        Assert.That(server.PublicIp, Is.Null, "0.0.0.0 means Vultr hasn't assigned an IP yet.");
        Assert.That(handler.RequestBodies.Single(), Does.Contain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("#cloud-init\nhello"))));
    }

    [Test]
    public async Task GetServerAsync_reports_the_assigned_ip_once_present()
    {
        const string response = """{ "instance": { "id": "abc-123", "status": "active", "main_ip": "203.0.113.4" } }""";
        var http = MakeClient(_ => response, out _);
        var server = await new HttpVultrApiClient(http).GetServerAsync("abc-123", CancellationToken.None);
        Assert.That(server.PublicIp, Is.EqualTo("203.0.113.4"));
        Assert.That(server.Status, Is.EqualTo("active"));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var http = new HttpClient(new FakeStatusHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":\"invalid key\"}"))
        {
            BaseAddress = new Uri("https://api.vultr.com/v2/"),
        };
        Assert.ThrowsAsync<InvalidOperationException>(() => new HttpVultrApiClient(http).GetServerAsync("abc-123", CancellationToken.None));
    }
}
