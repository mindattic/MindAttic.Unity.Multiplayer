using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Ovh;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: creates a real OVHcloud Public Cloud instance against the owner's account and always
/// tears it down, even on failure. Never runs as part of a normal `dotnet test` (NUnit's [Explicit]
/// excludes it); run it deliberately with <c>dotnet test --filter OvhSmokeTests</c> after setting
/// <c>MP_HOST_OVH_SMOKE_ENDPOINT</c> (e.g. <c>ovh-eu</c>), <c>_SERVICE_NAME</c>, <c>_APP_KEY</c>,
/// <c>_APP_SECRET</c>, <c>_CONSUMER_KEY</c>, <c>_SSH_USER</c>, <c>_SSH_KEY_PATH</c> and
/// <c>_SSH_PUBLIC_KEY_PATH</c> to a disposable key you're fine registering with OVH.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real OVHcloud instance; costs money and needs MP_HOST_OVH_SMOKE_ENDPOINT/_SERVICE_NAME/_APP_KEY/_APP_SECRET/_CONSUMER_KEY/_SSH_USER/_SSH_KEY_PATH/_SSH_PUBLIC_KEY_PATH.")]
public class OvhSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_instance()
    {
        string? endpoint = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_ENDPOINT");
        string? serviceName = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_SERVICE_NAME");
        string? appKey = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_APP_KEY");
        string? appSecret = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_APP_SECRET");
        string? consumerKey = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_CONSUMER_KEY");
        string? sshUser = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_SSH_USER");
        string? sshKeyPath = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_SSH_KEY_PATH");
        string? sshPublicKeyPath = Environment.GetEnvironmentVariable("MP_HOST_OVH_SMOKE_SSH_PUBLIC_KEY_PATH");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(appKey)
            || string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(consumerKey)
            || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(sshKeyPath) || string.IsNullOrWhiteSpace(sshPublicKeyPath))
        {
            Assert.Ignore("Set MP_HOST_OVH_SMOKE_ENDPOINT/_SERVICE_NAME/_APP_KEY/_APP_SECRET/_CONSUMER_KEY/_SSH_USER/_SSH_KEY_PATH/_SSH_PUBLIC_KEY_PATH to run this against a real OVH account.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri($"https://{endpoint}.api.ovh.com/1.0/") };
        var provisioner = new OvhProvisioner(
            new HttpOvhApiClient(http, appKey, appSecret, consumerKey, serviceName),
            new ProcessSshCommandRunner(new Processes.ProcessRunner()));
        var log = new RecordingDeployLog();
        var connection = new ProviderConnection("mp-host-smoke-test", new Dictionary<string, string>
        {
            ["sshUser"] = sshUser,
            ["sshKeyPath"] = sshKeyPath,
            ["sshPublicKey"] = File.ReadAllText(sshPublicKeyPath).Trim(),
        }, new Dictionary<string, string>());
        var request = new ProvisionRequest(connection, "ghcr.io/mindattic/hello-world:latest", "ghcr.io", "", "",
            TestManifests.WithHealthPort(),
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "smoke-test", ["MINDATTIC_PUBLIC_ADDRESS"] = "smoke-test" },
            null, null);

        ProvisionResult? result = null;
        try
        {
            result = await provisioner.ProvisionAsync(request, log, CancellationToken.None);
            Assert.That(result.ServerId, Is.Not.Empty);
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
