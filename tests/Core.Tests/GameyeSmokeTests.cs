using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// Opt-in: starts a real Gameye session against the owner's account and always tears it down, even
    /// on failure. Never runs as part of a normal <c>dotnet test</c> (NUnit's <see cref="ExplicitAttribute"/>
    /// excludes it); run it deliberately with <c>dotnet test --filter GameyeSmokeTests</c> after setting
    /// <c>UM_GAMEYE_SMOKE_API_KEY</c>, <c>UM_GAMEYE_SMOKE_LOCATION</c> and <c>UM_GAMEYE_SMOKE_IMAGE</c>
    /// (a public GHCR image good enough to boot and hold open a UDP port).
    /// </summary>
    [TestFixture]
    [Explicit("Starts and tears down a real Gameye session; costs money and needs UM_GAMEYE_SMOKE_API_KEY et al.")]
    public class GameyeSmokeTests
    {
        [Test]
        public async Task Start_then_stop_a_real_session()
        {
            string apiKey = Environment.GetEnvironmentVariable("UM_GAMEYE_SMOKE_API_KEY");
            string location = Environment.GetEnvironmentVariable("UM_GAMEYE_SMOKE_LOCATION");
            string image = Environment.GetEnvironmentVariable("UM_GAMEYE_SMOKE_IMAGE");
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(image))
            {
                Assert.Ignore("Set UM_GAMEYE_SMOKE_API_KEY, UM_GAMEYE_SMOKE_LOCATION and UM_GAMEYE_SMOKE_IMAGE to run this against a real Gameye account.");
                return;
            }

            using (var api = new HttpGameyeApiClient(apiKey))
            {
                var joinCodes = new InMemoryJoinCodeService();
                string imageName = "um-t29-smoke-" + DateTime.UtcNow.Ticks;
                var ports = new[] { new GameyePortSpec("game", 7777, TransportKind.Udp) };
                var allocator = new GameyeMatchAllocator(api, joinCodes, imageName, location, ports)
                {
                    Timeout = TimeSpan.FromMinutes(3),
                };

                await allocator.RegisterImageAsync(image);

                MatchAllocation allocation = null;
                try
                {
                    allocation = await allocator.AllocateAsync(new MatchRequest("um-t29-smoke-match", 2));
                    Assert.That(allocation.Endpoint.Address, Is.Not.Empty);
                    Assert.That(allocation.Code.IsEmpty, Is.False);
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
