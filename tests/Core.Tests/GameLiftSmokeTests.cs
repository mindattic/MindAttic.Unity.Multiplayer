using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// Opt-in: creates a real GameLift game session (and a player session) against the owner's
    /// account and always tears them down, even on failure. Never runs as part of a normal
    /// <c>dotnet test</c> (NUnit's <see cref="ExplicitAttribute"/> excludes it); run it deliberately
    /// with <c>dotnet test --filter GameLiftSmokeTests</c> after setting <c>UM_GAMELIFT_SMOKE_ACCESS_KEY_ID</c>,
    /// <c>UM_GAMELIFT_SMOKE_SECRET_ACCESS_KEY</c>, <c>UM_GAMELIFT_SMOKE_REGION</c> and
    /// <c>UM_GAMELIFT_SMOKE_FLEET_ID</c> (a fleet already ACTIVE -- mp-host's <c>GameLiftProvisioner</c>
    /// created one, see <c>docs/articles/hosting/gamelift.md</c>).
    /// </summary>
    [TestFixture]
    [Explicit("Starts and stops a real GameLift game session; needs an ACTIVE fleet and UM_GAMELIFT_SMOKE_* env vars.")]
    public class GameLiftSmokeTests
    {
        [Test]
        public async Task Start_then_stop_a_real_session()
        {
            string accessKeyId = Environment.GetEnvironmentVariable("UM_GAMELIFT_SMOKE_ACCESS_KEY_ID");
            string secretAccessKey = Environment.GetEnvironmentVariable("UM_GAMELIFT_SMOKE_SECRET_ACCESS_KEY");
            string region = Environment.GetEnvironmentVariable("UM_GAMELIFT_SMOKE_REGION");
            string fleetId = Environment.GetEnvironmentVariable("UM_GAMELIFT_SMOKE_FLEET_ID");
            if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey)
                || string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(fleetId))
            {
                Assert.Ignore("Set UM_GAMELIFT_SMOKE_ACCESS_KEY_ID, _SECRET_ACCESS_KEY, _REGION and _FLEET_ID to run this against a real GameLift account.");
                return;
            }

            using (var api = new HttpGameLiftApiClient(accessKeyId, secretAccessKey, region))
            {
                var joinCodes = new InMemoryJoinCodeService();
                var allocator = new GameLiftMatchAllocator(api, joinCodes, fleetId)
                {
                    Timeout = TimeSpan.FromMinutes(2),
                };

                MatchAllocation allocation = null;
                try
                {
                    allocation = await allocator.AllocateAsync(new MatchRequest("um-t30-smoke-match", 2));
                    Assert.That(allocation.Endpoint.Address, Is.Not.Empty);
                    Assert.That(allocation.Code.IsEmpty, Is.False);

                    string playerSessionId = await allocator.CreatePlayerSessionAsync(allocation, "um-t30-smoke-player");
                    Assert.That(playerSessionId, Is.Not.Empty);
                }
                finally
                {
                    if (allocation != null)
                    {
                        await allocator.ReleaseAsync(allocation);
                    }
                }
            }
        }
    }
}
