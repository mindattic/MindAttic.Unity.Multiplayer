using System.Net;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Linode;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpLinodeApiClientTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.linode.com/v4/") };
    }

    [Test]
    public async Task EnsureSshKeyAsync_hands_the_key_text_straight_back_with_no_api_call()
    {
        var http = MakeClient(_ => throw new InvalidOperationException("should not call the API"), out _);
        string id = await new HttpLinodeApiClient(http).EnsureSshKeyAsync("mp-host-srv", "ssh-ed25519 AAAA...", CancellationToken.None);
        Assert.That(id, Is.EqualTo("ssh-ed25519 AAAA..."));
    }

    [Test]
    public async Task CreateServerAsync_sends_the_key_inline_as_an_authorized_key()
    {
        const string response = """{ "id": 555, "status": "provisioning", "ipv4": [] }""";
        var http = MakeClient(_ => response, out var handler);
        var client = new HttpLinodeApiClient(http);

        var server = await client.CreateServerAsync(new CloudVpsCreateRequest("srv", "us-east", "g6-nanode-1", "linode/ubuntu24.04", "ssh-ed25519 AAAA...", "#cloud-init\n"), CancellationToken.None);

        Assert.That(server.Id, Is.EqualTo("555"));
        Assert.That(server.PublicIp, Is.Null);
        Assert.That(handler.RequestBodies.Single(), Does.Contain("ssh-ed25519 AAAA...").And.Contain("linode/ubuntu24.04"));
    }

    [Test]
    public async Task GetServerAsync_reports_the_first_ipv4_address()
    {
        const string response = """{ "id": 555, "status": "running", "ipv4": [ "203.0.113.11" ] }""";
        var http = MakeClient(_ => response, out _);
        var server = await new HttpLinodeApiClient(http).GetServerAsync("555", CancellationToken.None);
        Assert.That(server.PublicIp, Is.EqualTo("203.0.113.11"));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var http = new HttpClient(new FakeStatusHttpMessageHandler(HttpStatusCode.Unauthorized, "{\"errors\":[{\"reason\":\"Invalid Token\"}]}"))
        {
            BaseAddress = new Uri("https://api.linode.com/v4/"),
        };
        Assert.ThrowsAsync<InvalidOperationException>(() => new HttpLinodeApiClient(http).GetServerAsync("555", CancellationToken.None));
    }
}
