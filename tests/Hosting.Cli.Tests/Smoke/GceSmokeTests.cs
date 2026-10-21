using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: creates and then deletes a real GCE instance (plus its firewall rule) in the owner's
/// Google Cloud project. Never runs as part of a normal <c>dotnet test</c>; run it deliberately
/// with <c>dotnet test --filter GceSmokeTests</c> after setting every <c>MP_HOST_GCE_SMOKE_*</c>
/// variable below, including a fresh access token (<c>gcloud auth print-access-token</c>). Costs
/// real money for as long as the instance exists, which is why the <c>finally</c> block always
/// deletes it, even on assertion failure.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real GCE instance; costs money and needs MP_HOST_GCE_SMOKE_* and a GCP project.")]
public class GceSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_instance()
    {
        string? projectId = Environment.GetEnvironmentVariable("MP_HOST_GCE_SMOKE_PROJECT_ID");
        string? accessToken = Environment.GetEnvironmentVariable("MP_HOST_GCE_SMOKE_ACCESS_TOKEN");
        string? zone = Environment.GetEnvironmentVariable("MP_HOST_GCE_SMOKE_ZONE");
        string? sshKeyPath = Environment.GetEnvironmentVariable("MP_HOST_GCE_SMOKE_SSH_KEY_PATH");
        string? sshPublicKeyPath = Environment.GetEnvironmentVariable("MP_HOST_GCE_SMOKE_SSH_PUBLIC_KEY_PATH");
        if (new[] { projectId, accessToken, zone, sshKeyPath, sshPublicKeyPath }.Any(string.IsNullOrWhiteSpace))
        {
            Assert.Ignore("Set MP_HOST_GCE_SMOKE_PROJECT_ID/_ACCESS_TOKEN/_ZONE/_SSH_KEY_PATH/_SSH_PUBLIC_KEY_PATH to run this against a real GCP project.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri("https://compute.googleapis.com/compute/v1/") };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var provisioner = new GceProvisioner(new HttpGceApiClient(http, projectId!), new ProcessSshCommandRunner(new ProcessRunner()));
        var log = new RecordingDeployLog();
        var connection = new ProviderConnection("mp-host-gce-smoke", new Dictionary<string, string>
        {
            ["sshUser"] = "ubuntu", ["sshKeyPath"] = sshKeyPath!, ["sshPublicKeyPath"] = sshPublicKeyPath!,
        }, new Dictionary<string, string>());
        var request = new ProvisionRequest(connection, "ghcr.io/mindattic/hello-world:latest", "ghcr.io", "", "",
            TestManifests.WithHealthPort(),
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "smoke-test", ["MINDATTIC_PUBLIC_ADDRESS"] = "0.0.0.0" },
            zone, null);

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
