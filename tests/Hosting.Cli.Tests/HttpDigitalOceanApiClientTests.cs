using System.Net;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.DigitalOcean;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpDigitalOceanApiClientTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.digitalocean.com/v2/") };
    }

    [Test]
    public async Task CreateServerAsync_sends_the_image_region_and_key_then_parses_the_droplet_back()
    {
        const string response = """
            { "droplet": { "id": 99, "status": "new", "networks": { "v4": [ { "ip_address": "203.0.113.7", "type": "public" } ] } } }
            """;
        var http = MakeClient(_ => response, out var handler);
        var client = new HttpDigitalOceanApiClient(http);

        var server = await client.CreateServerAsync(new CloudVpsCreateRequest("srv", "nyc1", "s-1vcpu-1gb", "ubuntu-24-04-x64", "17", "#cloud-init\n"), CancellationToken.None);

        Assert.That(server.Id, Is.EqualTo("99"));
        Assert.That(server.PublicIp, Is.EqualTo("203.0.113.7"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("ubuntu-24-04-x64").And.Contain("nyc1"));
    }

    [Test]
    public async Task GetServerAsync_ignores_private_networks_when_picking_the_public_ip()
    {
        const string response = """
            { "droplet": { "id": 99, "status": "active",
              "networks": { "v4": [ { "ip_address": "10.0.0.5", "type": "private" }, { "ip_address": "203.0.113.7", "type": "public" } ] } } }
            """;
        var http = MakeClient(_ => response, out _);
        var server = await new HttpDigitalOceanApiClient(http).GetServerAsync("99", CancellationToken.None);
        Assert.That(server.PublicIp, Is.EqualTo("203.0.113.7"));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var http = new HttpClient(new FakeStatusHttpMessageHandler(HttpStatusCode.Unauthorized, "{\"message\":\"Unable to authenticate you\"}"))
        {
            BaseAddress = new Uri("https://api.digitalocean.com/v2/"),
        };
        Assert.ThrowsAsync<InvalidOperationException>(() => new HttpDigitalOceanApiClient(http).GetServerAsync("99", CancellationToken.None));
    }
}
