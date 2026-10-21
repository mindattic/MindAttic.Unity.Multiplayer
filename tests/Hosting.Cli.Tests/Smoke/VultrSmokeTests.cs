using System.Net.Http.Headers;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Vultr;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: creates a real Vultr instance against the owner's account and always tears it down, even
/// on failure. Never runs as part of a normal `dotnet test` (NUnit's [Explicit] excludes it); run it
/// deliberately with <c>dotnet test --filter VultrSmokeTests</c> after setting
/// <c>MP_HOST_VULTR_SMOKE_TOKEN</c>, <c>_SSH_USER</c>, <c>_SSH_KEY_PATH</c> and
/// <c>_SSH_PUBLIC_KEY_PATH</c> to a disposable key you're fine registering with Vultr.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real Vultr instance; costs money and needs MP_HOST_VULTR_SMOKE_TOKEN/_SSH_USER/_SSH_KEY_PATH/_SSH_PUBLIC_KEY_PATH.")]
public class VultrSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_instance()
    {
        string? token = Environment.GetEnvironmentVariable("MP_HOST_VULTR_SMOKE_TOKEN");
        string? sshUser = Environment.GetEnvironmentVariable("MP_HOST_VULTR_SMOKE_SSH_USER");
        string? sshKeyPath = Environment.GetEnvironmentVariable("MP_HOST_VULTR_SMOKE_SSH_KEY_PATH");
        string? sshPublicKeyPath = Environment.GetEnvironmentVariable("MP_HOST_VULTR_SMOKE_SSH_PUBLIC_KEY_PATH");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(sshKeyPath) || string.IsNullOrWhiteSpace(sshPublicKeyPath))
        {
            Assert.Ignore("Set MP_HOST_VULTR_SMOKE_TOKEN/_SSH_USER/_SSH_KEY_PATH/_SSH_PUBLIC_KEY_PATH to run this against a real Vultr account.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri("https://api.vultr.com/v2/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var provisioner = new VultrProvisioner(new HttpVultrApiClient(http), new ProcessSshCommandRunner(new Processes.ProcessRunner()));
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
