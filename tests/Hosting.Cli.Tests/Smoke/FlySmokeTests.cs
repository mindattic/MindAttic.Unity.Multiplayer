using System.Net.Http.Headers;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: creates a real Fly app + machine against the owner's account and always tears it down,
/// even on failure. Never runs as part of a normal <c>dotnet test</c> (NUnit's <see cref="ExplicitAttribute"/>
/// excludes it); run it deliberately with <c>dotnet test --filter FlySmokeTests</c> after setting
/// <c>MP_HOST_FLY_SMOKE_API_TOKEN</c> and <c>MP_HOST_FLY_SMOKE_ORG</c>. Costs a small amount for the
/// dedicated IPv4 while the app exists; the test always destroys the app in its <c>finally</c>.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real Fly app/machine; costs money and needs MP_HOST_FLY_SMOKE_API_TOKEN/MP_HOST_FLY_SMOKE_ORG.")]
public class FlySmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_app()
    {
        string? apiToken = Environment.GetEnvironmentVariable("MP_HOST_FLY_SMOKE_API_TOKEN");
        string? orgSlug = Environment.GetEnvironmentVariable("MP_HOST_FLY_SMOKE_ORG");
        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(orgSlug))
        {
            Assert.Ignore("Set MP_HOST_FLY_SMOKE_API_TOKEN and MP_HOST_FLY_SMOKE_ORG to run this against a real Fly.io account.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri("https://api.machines.dev/v1/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var provisioner = new FlyProvisioner(new HttpFlyApiClient(http), new ProcessFlyCli(new Processes.ProcessRunner()));
        var log = new RecordingDeployLog();

        string appName = "mp-host-smoke-" + Guid.NewGuid().ToString("N")[..8];
        var connection = new ProviderConnection(appName, new Dictionary<string, string> { ["orgSlug"] = orgSlug }, new Dictionary<string, string>());
        var request = new ProvisionRequest(
            connection, "ghcr.io/mindattic/hello-world:latest", "ghcr.io", "", "",
            TestManifests.WithHealthPort(), new Dictionary<string, string>(), "iad", null);

        ProvisionResult? result = null;
        try
        {
            result = await provisioner.ProvisionAsync(request, log, CancellationToken.None);
            Assert.That(result.ServerId, Is.EqualTo(appName));
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
