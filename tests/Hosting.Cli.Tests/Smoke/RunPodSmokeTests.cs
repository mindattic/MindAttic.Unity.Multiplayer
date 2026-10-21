using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: creates a real RunPod pod against the owner's account and always tears it down, even on
/// failure. Never runs as part of a normal `dotnet test` (NUnit's [Explicit] excludes it); run it
/// deliberately with <c>dotnet test --filter RunPodSmokeTests</c> after setting
/// <c>MP_HOST_RUNPOD_SMOKE_API_KEY</c> (read via a disposable <see cref="HostingCredentialStore"/>
/// pointed at a throwaway temp directory -- the real vault is never touched by a test run).
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real RunPod pod; costs money and needs MP_HOST_RUNPOD_SMOKE_API_KEY.")]
public class RunPodSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_pod()
    {
        string? apiKey = Environment.GetEnvironmentVariable("MP_HOST_RUNPOD_SMOKE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Ignore("Set MP_HOST_RUNPOD_SMOKE_API_KEY to run this against a real RunPod account.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri("https://api.runpod.io/graphql") };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        var provisioner = new RunPodProvisioner(new HttpRunPodApiClient(http));
        var log = new RecordingDeployLog();

        var request = new ProvisionRequest(
            ProviderConnection.Named("mp-host-smoke-test"),
            "ghcr.io/mindattic/hello-world:latest", "ghcr.io", "", "",
            TestManifests.WithoutHealthPort(), new Dictionary<string, string>(), null, null);

        ProvisionResult? result = null;
        try
        {
            result = await provisioner.ProvisionAsync(request, log, CancellationToken.None);
            Assert.That(result.ServerId, Is.Not.Empty);
            var status = await provisioner.GetStatusAsync(result.ServerId, request.Connection, log, CancellationToken.None);
            Assert.That(status.State, Is.Not.EqualTo(ServerState.Gone));
        }
        finally
        {
            if (result is not null)
            {
                await provisioner.DestroyAsync(result.ServerId, request.Connection, log, CancellationToken.None);
            }
        }
    }
}
