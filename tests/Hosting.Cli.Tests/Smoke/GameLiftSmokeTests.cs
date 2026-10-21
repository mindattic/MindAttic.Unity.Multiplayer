using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GameLift;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Smoke;

/// <summary>
/// Opt-in: uploads a real (placeholder) build and creates a real fleet (plus its alias) in the
/// owner's AWS account, then tears both down. Never runs as part of a normal <c>dotnet test</c>
/// (NUnit's <see cref="ExplicitAttribute"/> excludes it); run it deliberately with
/// <c>dotnet test --filter GameLiftSmokeTests</c> after setting every <c>MP_HOST_GAMELIFT_SMOKE_*</c>
/// variable below. Costs real money for as long as the fleet exists, which is why the
/// <c>finally</c> block always destroys it, even on assertion failure. The placeholder package has
/// no real GameLift Server SDK integration, so the fleet never reports a healthy game session --
/// this only exercises the build-upload/fleet-create/destroy wiring against the real API, the same
/// way the other UM-T27 smoke tests check their wire format rather than full game functionality.
/// </summary>
[TestFixture]
[Explicit("Creates and tears down a real GameLift build/fleet/alias; costs money and needs MP_HOST_GAMELIFT_SMOKE_* and an AWS account.")]
public class GameLiftSmokeTests
{
    [Test]
    public async Task Provision_then_destroy_a_real_fleet()
    {
        string? accessKeyId = Environment.GetEnvironmentVariable("MP_HOST_GAMELIFT_SMOKE_ACCESS_KEY_ID");
        string? secretAccessKey = Environment.GetEnvironmentVariable("MP_HOST_GAMELIFT_SMOKE_SECRET_ACCESS_KEY");
        string? region = Environment.GetEnvironmentVariable("MP_HOST_GAMELIFT_SMOKE_REGION");
        if (new[] { accessKeyId, secretAccessKey, region }.Any(string.IsNullOrWhiteSpace))
        {
            Assert.Ignore("Set MP_HOST_GAMELIFT_SMOKE_ACCESS_KEY_ID/_SECRET_ACCESS_KEY/_REGION to run this against a real AWS account.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri($"https://gamelift.{region}.amazonaws.com/") };
        var provisioner = new GameLiftProvisioner(new HttpGameLiftFleetApiClient(http, new HttpClient(), accessKeyId!, secretAccessKey!, region!));
        var log = new RecordingDeployLog();
        var connection = new ProviderConnection("mp-host-gamelift-smoke", new Dictionary<string, string>(), new Dictionary<string, string>());

        string packageDirectory = Path.Combine(Path.GetTempPath(), "um-t30-gamelift-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "Game.x86_64"), "placeholder, not a real build");
        File.WriteAllText(Path.Combine(packageDirectory, "start.sh"), "#!/bin/sh\nexec ./Game.x86_64 \"$@\"\n");

        var request = new ProvisionRequest(connection, "", "", "", "",
            TestManifests.WithHealthPort(), new Dictionary<string, string>(), region, null, packageDirectory);

        ProvisionResult? result = null;
        try
        {
            result = await provisioner.ProvisionAsync(request, log, CancellationToken.None);
            var status = await provisioner.GetStatusAsync(result.ServerId, connection, log, CancellationToken.None);
            Assert.That(status.State, Is.Not.EqualTo(ServerState.Gone));
        }
        finally
        {
            Directory.Delete(packageDirectory, recursive: true);
            if (result is not null)
            {
                await provisioner.DestroyAsync(result.ServerId, connection, log, CancellationToken.None);
            }
        }
    }
}
