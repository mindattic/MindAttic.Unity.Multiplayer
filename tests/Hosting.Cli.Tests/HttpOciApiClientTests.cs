using System.Security.Cryptography;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpOciApiClientTests
{
    private static string GeneratePem()
    {
        using RSA rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static HttpOciApiClient MakeSut(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        handler = new FakeHttpMessageHandler(respond, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://iaas.us-ashburn-1.oraclecloud.com/") };
        return new HttpOciApiClient(http, "ocid1.compartment.oc1..c", "ocid1.tenancy.oc1..t", "ocid1.user.oc1..u", "aa:bb", GeneratePem());
    }

    [Test]
    public async Task LaunchInstanceAsync_posts_the_shape_and_returns_the_new_id()
    {
        const string response = """{ "id": "ocid1.instance.oc1..new", "lifecycleState": "PROVISIONING" }""";
        var client = MakeSut(_ => response, out var handler);
        var request = new OciLaunchInstanceRequest("ocid1.compartment.oc1..c", "AD-1", "ocid1.subnet.oc1..s", "VM.Standard.E2.1.Micro", 1, 6, "ocid1.image.oc1..i", "srv", "ssh-ed25519 AAAA", "dXNlcl9kYXRh");
        var instance = await client.LaunchInstanceAsync(request, CancellationToken.None);

        Assert.That(instance.Id, Is.EqualTo("ocid1.instance.oc1..new"));
        Assert.That(instance.LifecycleState, Is.EqualTo("PROVISIONING"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("VM.Standard.E2.1.Micro"));
        Assert.That(handler.RequestBodies.Single(), Does.Not.Contain("shapeConfig"));
    }

    [Test]
    public async Task LaunchInstanceAsync_includes_shapeConfig_for_a_flex_shape()
    {
        const string response = """{ "id": "ocid1.instance.oc1..new", "lifecycleState": "PROVISIONING" }""";
        var client = MakeSut(_ => response, out var handler);
        var request = new OciLaunchInstanceRequest("c", "AD-1", "s", "VM.Standard.A1.Flex", 4, 24, "i", "srv", "key", "data");
        await client.LaunchInstanceAsync(request, CancellationToken.None);

        Assert.That(handler.RequestBodies.Single(), Does.Contain("\"ocpus\":4"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("\"memoryInGBs\":24"));
    }

    [Test]
    public async Task GetInstanceAsync_resolves_the_public_ip_through_the_vnic_attachment()
    {
        var client = MakeSut(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/instances/")) return """{ "lifecycleState": "RUNNING" }""";
            if (path.Contains("/vnicAttachments")) return """[ { "vnicId": "ocid1.vnic.oc1..v" } ]""";
            if (path.Contains("/vnics/")) return """{ "publicIp": "198.51.100.40" }""";
            return "{}";
        }, out _);

        var instance = await client.GetInstanceAsync("ocid1.instance.oc1..x", CancellationToken.None);
        Assert.That(instance.LifecycleState, Is.EqualTo("RUNNING"));
        Assert.That(instance.PublicIp, Is.EqualTo("198.51.100.40"));
    }

    [Test]
    public async Task GetInstanceAsync_returns_null_ip_with_no_vnic_attachments()
    {
        var client = MakeSut(request =>
            request.RequestUri!.AbsolutePath.Contains("/vnicAttachments") ? "[]" : """{ "lifecycleState": "PROVISIONING" }""", out _);

        var instance = await client.GetInstanceAsync("ocid1.instance.oc1..x", CancellationToken.None);
        Assert.That(instance.PublicIp, Is.Null);
    }

    [Test]
    public async Task GetIngressSecurityRulesAsync_parses_tcp_and_udp_rules()
    {
        const string response = """
            {
              "ingressSecurityRules": [
                { "protocol": "6", "tcpOptions": { "destinationPortRange": { "min": 22, "max": 22 } }, "description": "ssh" },
                { "protocol": "17", "udpOptions": { "destinationPortRange": { "min": 7777, "max": 7777 } }, "description": "game" }
              ]
            }
            """;
        var client = MakeSut(_ => response, out _);
        var rules = await client.GetIngressSecurityRulesAsync("ocid1.securitylist.oc1..sl", CancellationToken.None);

        Assert.That(rules, Has.Count.EqualTo(2));
        Assert.That(rules.Any(r => r.Protocol == "tcp" && r.Port == 22), Is.True);
        Assert.That(rules.Any(r => r.Protocol == "udp" && r.Port == 7777), Is.True);
    }

    [Test]
    public async Task SetIngressSecurityRulesAsync_sends_the_protocol_numbers()
    {
        var client = MakeSut(_ => "{}", out var handler);
        await client.SetIngressSecurityRulesAsync("sl", new[] { new OciSecurityRule("tcp", 22, "ssh"), new OciSecurityRule("udp", 7777, "game") }, CancellationToken.None);

        string body = handler.RequestBodies.Single();
        Assert.That(body, Does.Contain("\"protocol\":\"6\""));
        Assert.That(body, Does.Contain("\"protocol\":\"17\""));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception()
    {
        var handler = new FakeHttpMessageHandler(_ => """{ "message": "NotAuthorizedOrNotFound" }""", System.Net.HttpStatusCode.NotFound);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://iaas.us-ashburn-1.oraclecloud.com/") };
        var client = new HttpOciApiClient(http, "c", "t", "u", "fp", GeneratePem());

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.TerminateInstanceAsync("x", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("NotAuthorizedOrNotFound"));
    }
}
