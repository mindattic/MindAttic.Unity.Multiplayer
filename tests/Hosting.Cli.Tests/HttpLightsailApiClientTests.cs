using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpLightsailApiClientTests
{
    private static HttpLightsailApiClient MakeSut(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://lightsail.us-east-1.amazonaws.com/") };
        return new HttpLightsailApiClient(http, "AKIAEXAMPLE", "secret", "us-east-1");
    }

    [Test]
    public async Task CreateInstanceAsync_sends_the_blueprint_bundle_and_key_pair()
    {
        var client = MakeSut(_ => "{}", out var handler);
        var instance = await client.CreateInstanceAsync(new LightsailCreateInstanceRequest("srv", "us-east-1a", "ubuntu_22_04", "nano_3_0", "my-key", "userdata"), CancellationToken.None);

        Assert.That(instance.Name, Is.EqualTo("srv"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("\"blueprintId\":\"ubuntu_22_04\""));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("\"keyPairName\":\"my-key\""));
    }

    [Test]
    public async Task GetInstanceAsync_parses_state_and_public_ip()
    {
        const string response = """
            { "instance": { "name": "srv", "state": { "name": "running" }, "publicIpAddress": "198.51.100.20" } }
            """;
        var client = MakeSut(_ => response, out _);
        var instance = await client.GetInstanceAsync("srv", CancellationToken.None);

        Assert.That(instance.State, Is.EqualTo("running"));
        Assert.That(instance.PublicIp, Is.EqualTo("198.51.100.20"));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var handler = new FakeHttpMessageHandler(_ => """{ "message": "invalid key pair" }""", System.Net.HttpStatusCode.BadRequest);
        var client = new HttpLightsailApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://lightsail.us-east-1.amazonaws.com/") }, "AKIA", "secret", "us-east-1");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInstanceAsync("srv", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("invalid key pair"));
    }
}
