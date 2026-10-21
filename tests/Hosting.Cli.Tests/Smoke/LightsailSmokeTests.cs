using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: creates and then deletes a real Lightsail instance in the owner's AWS account. Never
/// runs as part of a normal <c>dotnet test</c>; run it deliberately with
/// <c>dotnet test --filter LightsailSmokeTests</c> after setting every <c>MP_HOST_LIGHTSAIL_SMOKE_*</c>
/// variable below. Costs real money for as long as the instance exists, which is why the
/// <c>finally</c> block always deletes it, even on assertion failure.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real Lightsail instance; costs money and needs MP_HOST_LIGHTSAIL_SMOKE_* and an AWS account.")]
public class LightsailSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_instance()
    {
        string? accessKeyId = Environment.GetEnvironmentVariable("MP_HOST_LIGHTSAIL_SMOKE_ACCESS_KEY_ID");
        string? secretAccessKey = Environment.GetEnvironmentVariable("MP_HOST_LIGHTSAIL_SMOKE_SECRET_ACCESS_KEY");
        string? region = Environment.GetEnvironmentVariable("MP_HOST_LIGHTSAIL_SMOKE_REGION");
        string? zone = Environment.GetEnvironmentVariable("MP_HOST_LIGHTSAIL_SMOKE_AVAILABILITY_ZONE");
        string? keyPairName = Environment.GetEnvironmentVariable("MP_HOST_LIGHTSAIL_SMOKE_KEY_PAIR_NAME");
        string? sshKeyPath = Environment.GetEnvironmentVariable("MP_HOST_LIGHTSAIL_SMOKE_SSH_KEY_PATH");
        if (new[] { accessKeyId, secretAccessKey, region, zone, keyPairName, sshKeyPath }.Any(string.IsNullOrWhiteSpace))
        {
            Assert.Ignore("Set MP_HOST_LIGHTSAIL_SMOKE_ACCESS_KEY_ID/_SECRET_ACCESS_KEY/_REGION/_AVAILABILITY_ZONE/_KEY_PAIR_NAME/_SSH_KEY_PATH to run this against a real AWS account.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri($"https://lightsail.{region}.amazonaws.com/") };
        var provisioner = new LightsailProvisioner(new HttpLightsailApiClient(http, accessKeyId!, secretAccessKey!, region!), new ProcessSshCommandRunner(new ProcessRunner()));
        var log = new RecordingDeployLog();
        var connection = new ProviderConnection("mp-host-lightsail-smoke", new Dictionary<string, string>
        {
            ["availabilityZone"] = zone!, ["keyPairName"] = keyPairName!, ["sshUser"] = "ubuntu", ["sshKeyPath"] = sshKeyPath!,
        }, new Dictionary<string, string>());
        var request = new ProvisionRequest(connection, "ghcr.io/mindattic/hello-world:latest", "ghcr.io", "", "",
            TestManifests.WithHealthPort(),
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "smoke-test", ["MINDATTIC_PUBLIC_ADDRESS"] = "0.0.0.0" },
            region, null);

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
