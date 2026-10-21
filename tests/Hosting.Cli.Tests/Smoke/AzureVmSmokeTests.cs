using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Azure;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: creates and then deletes a real Azure VM (plus its NSG, public IP and NIC) in the
/// owner's subscription. Never runs as part of a normal <c>dotnet test</c>; run it deliberately
/// with <c>dotnet test --filter AzureVmSmokeTests</c> after setting every
/// <c>MP_HOST_AZUREVM_SMOKE_*</c> variable below, including a pre-existing resource group and
/// VNet subnet (mp-host does not create a VNet). Costs real money for as long as the VM exists,
/// which is why the <c>finally</c> block always deletes it, even on assertion failure.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real Azure VM; costs money and needs MP_HOST_AZUREVM_SMOKE_* and an Azure subscription.")]
public class AzureVmSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_vm()
    {
        string? tenantId = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_TENANT_ID");
        string? clientId = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_CLIENT_ID");
        string? clientSecret = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_CLIENT_SECRET");
        string? subscriptionId = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_SUBSCRIPTION_ID");
        string? resourceGroup = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_RESOURCE_GROUP");
        string? subnetId = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_SUBNET_ID");
        string? sshKeyPath = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_SSH_KEY_PATH");
        string? sshPublicKeyPath = Environment.GetEnvironmentVariable("MP_HOST_AZUREVM_SMOKE_SSH_PUBLIC_KEY_PATH");
        if (new[] { tenantId, clientId, clientSecret, subscriptionId, resourceGroup, subnetId, sshKeyPath, sshPublicKeyPath }.Any(string.IsNullOrWhiteSpace))
        {
            Assert.Ignore("Set MP_HOST_AZUREVM_SMOKE_TENANT_ID/_CLIENT_ID/_CLIENT_SECRET/_SUBSCRIPTION_ID/_RESOURCE_GROUP/_SUBNET_ID/_SSH_KEY_PATH/_SSH_PUBLIC_KEY_PATH to run this against a real Azure subscription.");
            return;
        }

        var provisioner = new AzureVmProvisioner(
            new HttpAzureVmApiClient(new HttpClient(), tenantId!, clientId!, clientSecret!, subscriptionId!),
            new ProcessSshCommandRunner(new ProcessRunner()));
        var log = new RecordingDeployLog();
        var connection = new ProviderConnection("mp-host-azurevm-smoke", new Dictionary<string, string>
        {
            ["resourceGroup"] = resourceGroup!, ["subnetId"] = subnetId!, ["sshKeyPath"] = sshKeyPath!, ["sshPublicKeyPath"] = sshPublicKeyPath!,
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
