using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: bootstraps and then tears the compose stack down on a real, owner-provided box reached
/// over SSH. Never runs as part of a normal `dotnet test` (NUnit's [Explicit] excludes it); run it
/// deliberately after setting <c>MP_HOST_SSH_SMOKE_HOST</c>/<c>_USER</c>/<c>_KEY_PATH</c> to a
/// disposable VPS you're fine running `docker compose down -v` on afterwards.
/// </summary>
[TestFixture]
[Explicit("Bootstraps and tears down a real VPS over SSH; needs MP_HOST_SSH_SMOKE_HOST/_USER/_KEY_PATH.")]
public class GenericVpsSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_box()
    {
        string? host = Environment.GetEnvironmentVariable("MP_HOST_SSH_SMOKE_HOST");
        string? user = Environment.GetEnvironmentVariable("MP_HOST_SSH_SMOKE_USER");
        string? keyPath = Environment.GetEnvironmentVariable("MP_HOST_SSH_SMOKE_KEY_PATH");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(keyPath))
        {
            Assert.Ignore("Set MP_HOST_SSH_SMOKE_HOST/_USER/_KEY_PATH to run this against a real VPS.");
            return;
        }

        var provisioner = new GenericVpsProvisioner(new ProcessSshCommandRunner(new Processes.ProcessRunner()));
        var log = new RecordingDeployLog();
        var connection = new ProviderConnection("mp-host-smoke-test",
            new Dictionary<string, string> { ["sshHost"] = host, ["sshUser"] = user, ["sshKeyPath"] = keyPath },
            new Dictionary<string, string>());
        var request = new ProvisionRequest(connection, "ghcr.io/mindattic/hello-world:latest", "ghcr.io", "", "",
            TestManifests.WithHealthPort(),
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "smoke-test", ["MINDATTIC_PUBLIC_ADDRESS"] = host },
            null, null);

        ProvisionResult? result = null;
        try
        {
            result = await provisioner.ProvisionAsync(request, log, CancellationToken.None);
            var status = await provisioner.GetStatusAsync(result.ServerId, connection, log, CancellationToken.None);
            Assert.That(status.State, Is.EqualTo(ServerState.Running));
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
