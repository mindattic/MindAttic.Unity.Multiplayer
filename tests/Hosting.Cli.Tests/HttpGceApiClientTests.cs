using Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpGceApiClientTests
{
    private static HttpGceApiClient MakeSut(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://compute.googleapis.com/compute/v1/") };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");
        return new HttpGceApiClient(http, "my-project");
    }

    [Test]
    public async Task GetInstanceAsync_parses_status_and_nat_ip()
    {
        const string response = """
            { "status": "RUNNING", "networkInterfaces": [ { "accessConfigs": [ { "natIP": "198.51.100.30" } ] } ] }
            """;
        var client = MakeSut(_ => response, out _);
        var instance = await client.GetInstanceAsync("us-central1-a", "srv", CancellationToken.None);

        Assert.That(instance.Status, Is.EqualTo("RUNNING"));
        Assert.That(instance.PublicIp, Is.EqualTo("198.51.100.30"));
    }

    [Test]
    public async Task GetInstanceAsync_returns_null_ip_before_one_is_assigned()
    {
        const string response = """{ "status": "PROVISIONING" }""";
        var client = MakeSut(_ => response, out _);
        var instance = await client.GetInstanceAsync("us-central1-a", "srv", CancellationToken.None);

        Assert.That(instance.PublicIp, Is.Null);
    }

    [Test]
    public async Task InsertInstanceAsync_sends_the_machine_type_and_startup_script_then_fetches_the_instance()
    {
        const string response = """{ "status": "PROVISIONING" }""";
        var client = MakeSut(_ => response, out var handler);
        await client.InsertInstanceAsync(new GceInsertInstanceRequest("srv", "us-central1-a", "e2-small", "img", "net", "tag", "ubuntu", "key", "#!/bin/sh\n"), CancellationToken.None);

        Assert.That(handler.RequestBodies.Single(), Does.Contain("zones/us-central1-a/machineTypes/e2-small"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("startup-script"));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var handler = new FakeHttpMessageHandler(_ => """{ "error": { "message": "quota exceeded" } }""", System.Net.HttpStatusCode.Forbidden);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://compute.googleapis.com/compute/v1/") };
        var client = new HttpGceApiClient(http, "my-project");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInstanceAsync("us-central1-a", "srv", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("quota exceeded"));
    }
}
