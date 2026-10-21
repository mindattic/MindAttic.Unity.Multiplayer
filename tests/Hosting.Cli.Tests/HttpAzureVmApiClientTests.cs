using Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpAzureVmApiClientTests
{
    private const string TokenResponse = """{ "access_token": "token-123", "expires_in": 3600 }""";

    private static HttpAzureVmApiClient MakeSut(Func<HttpRequestMessage, string> respondToArm, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(request =>
            request.RequestUri!.Host.Contains("login.microsoftonline.com") ? TokenResponse : respondToArm(request));
        return new HttpAzureVmApiClient(new HttpClient(handler), "tenant", "client", "secret", "sub-1");
    }

    [Test]
    public async Task GetVirtualMachineAsync_fetches_a_token_first_then_parses_provisioning_and_power_state()
    {
        const string vm = """
            { "properties": { "provisioningState": "Succeeded",
              "instanceView": { "statuses": [ { "code": "ProvisioningState/succeeded" }, { "code": "PowerState/running" } ] } } }
            """;
        var client = MakeSut(_ => vm, out var handler);
        var info = await client.GetVirtualMachineAsync("rg", "vm1", CancellationToken.None);

        Assert.That(info.ProvisioningState, Is.EqualTo("Succeeded"));
        Assert.That(info.PowerState, Is.EqualTo("running"));
        Assert.That(handler.RequestBodies, Has.Some.Matches<string>(b => b.Contains("grant_type=client_credentials")));
    }

    [Test]
    public async Task GetPublicIpAddressAsync_reads_the_assigned_address()
    {
        const string ip = """{ "properties": { "ipAddress": "198.51.100.40" } }""";
        var client = MakeSut(_ => ip, out _);
        string? address = await client.GetPublicIpAddressAsync("rg", "ip1", CancellationToken.None);

        Assert.That(address, Is.EqualTo("198.51.100.40"));
    }

    [Test]
    public async Task GetPublicIpAddressAsync_returns_null_before_one_is_assigned()
    {
        const string ip = """{ "properties": { } }""";
        var client = MakeSut(_ => ip, out _);
        string? address = await client.GetPublicIpAddressAsync("rg", "ip1", CancellationToken.None);

        Assert.That(address, Is.Null);
    }

    [Test]
    public void An_ARM_error_response_surfaces_as_an_exception()
    {
        var handler = new FakeHttpMessageHandler(
            request => request.RequestUri!.Host.Contains("login.microsoftonline.com") ? TokenResponse : """{ "error": { "message": "The subnet was not found." } }""",
            request => request.RequestUri!.Host.Contains("login.microsoftonline.com") ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.NotFound);
        var client = new HttpAzureVmApiClient(new HttpClient(handler), "tenant", "client", "secret", "sub-1");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVirtualMachineAsync("rg", "vm1", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("subnet was not found"));
    }
}
