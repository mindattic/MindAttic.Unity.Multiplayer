using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: launches and then terminates a real OCI Always Free instance in the owner's Oracle Cloud
/// tenancy. Never runs as part of a normal <c>dotnet test</c>; run it deliberately with
/// <c>dotnet test --filter OciSmokeTests</c> after setting every <c>MP_HOST_OCI_SMOKE_*</c> variable
/// below. Free under Oracle's Always Free tier for the AMD micro shape used here, but the
/// <c>finally</c> block always terminates the instance anyway, even on assertion failure.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real OCI instance; needs MP_HOST_OCI_SMOKE_* and an Oracle Cloud tenancy.")]
public class OciSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_instance()
    {
        string? tenancyOcid = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_TENANCY_OCID");
        string? userOcid = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_USER_OCID");
        string? fingerprint = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_FINGERPRINT");
        string? privateKeyPath = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_PRIVATE_KEY_PATH");
        string? region = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_REGION");
        string? compartmentOcid = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_COMPARTMENT_OCID");
        string? availabilityDomain = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_AD");
        string? subnetId = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_SUBNET_OCID");
        string? securityListId = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_SECURITY_LIST_OCID");
        string? imageId = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_IMAGE_OCID");
        string? sshKeyPath = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_SSH_KEY_PATH");
        string? sshPublicKeyPath = Environment.GetEnvironmentVariable("MP_HOST_OCI_SMOKE_SSH_PUBLIC_KEY_PATH");
        var required = new[]
        {
            tenancyOcid, userOcid, fingerprint, privateKeyPath, region, compartmentOcid,
            availabilityDomain, subnetId, securityListId, imageId, sshKeyPath, sshPublicKeyPath,
        };
        if (required.Any(string.IsNullOrWhiteSpace))
        {
            Assert.Ignore("Set every MP_HOST_OCI_SMOKE_* variable (see this test's source) to run this against a real OCI tenancy.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri($"https://iaas.{region}.oraclecloud.com/") };
        string privateKeyPem = File.ReadAllText(privateKeyPath!);
        var provisioner = new OciProvisioner(
            new HttpOciApiClient(http, compartmentOcid!, tenancyOcid!, userOcid!, fingerprint!, privateKeyPem),
            new ProcessSshCommandRunner(new ProcessRunner()));
        var log = new RecordingDeployLog();
        var connection = new ProviderConnection("mp-host-oci-smoke", new Dictionary<string, string>
        {
            ["compartmentId"] = compartmentOcid!,
            ["availabilityDomain"] = availabilityDomain!,
            ["subnetId"] = subnetId!,
            ["securityListId"] = securityListId!,
            ["imageId"] = imageId!,
            ["sshUser"] = "ubuntu",
            ["sshKeyPath"] = sshKeyPath!,
            ["sshPublicKeyPath"] = sshPublicKeyPath!,
        }, new Dictionary<string, string>());
        var request = new ProvisionRequest(connection, "ghcr.io/mindattic/hello-world:latest", "ghcr.io", "", "",
            TestManifests.WithHealthPort(),
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "smoke-test", ["MINDATTIC_PUBLIC_ADDRESS"] = "0.0.0.0" },
            null, null);

        ProvisionResult? result = null;
        try
        {
            result = await provisioner.ProvisionAsync(request, log, CancellationToken.None);
            await provisioner.OpenFirewallAsync(result.ServerId, request, log, CancellationToken.None);
            var status = await provisioner.GetStatusAsync(result.ServerId, connection, log, CancellationToken.None);
            Assert.That(status.State, Is.Not.EqualTo(ServerState.Gone));
        }
        finally
        {
            if (result is not null)
            {
                await provisioner.DestroyAsync(result.ServerId, connection, log, CancellationToken.None);
            }
        }
    }
}
