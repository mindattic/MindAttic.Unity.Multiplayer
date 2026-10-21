using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// Opt-in: deploys a real Edgegap container against the owner's account and always tears it down,
    /// even on failure. Never runs as part of a normal <c>dotnet test</c> (NUnit's <see cref="ExplicitAttribute"/>
    /// excludes it); run it deliberately with <c>dotnet test --filter EdgegapSmokeTests</c> after setting
    /// <c>UM_EDGEGAP_SMOKE_API_TOKEN</c>, <c>UM_EDGEGAP_SMOKE_APP_NAME</c> (an app already created in the
    /// Edgegap dashboard) and <c>UM_EDGEGAP_SMOKE_IMAGE</c> (a public GHCR image good enough to boot and
    /// hold open a UDP port, such as a throwaway echo server).
    /// </summary>
    [TestFixture]
    [Explicit("Deploys and tears down a real Edgegap container; costs money and needs UM_EDGEGAP_SMOKE_API_TOKEN et al.")]
    public class EdgegapSmokeTests
    {
        [Test]
        public async Task Deploy_then_stop_a_real_container()
        {
            string apiToken = Environment.GetEnvironmentVariable("UM_EDGEGAP_SMOKE_API_TOKEN");
            string appName = Environment.GetEnvironmentVariable("UM_EDGEGAP_SMOKE_APP_NAME");
            string image = Environment.GetEnvironmentVariable("UM_EDGEGAP_SMOKE_IMAGE");
            if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(image))
            {
                Assert.Ignore("Set UM_EDGEGAP_SMOKE_API_TOKEN, UM_EDGEGAP_SMOKE_APP_NAME and UM_EDGEGAP_SMOKE_IMAGE to run this against a real Edgegap account.");
                return;
            }

            using (var api = new HttpEdgegapApiClient(apiToken))
            {
                var joinCodes = new InMemoryJoinCodeService();
                var allocator = new EdgegapMatchAllocator(api, joinCodes, appName, "um-t29-smoke-" + DateTime.UtcNow.Ticks)
                {
                    Timeout = TimeSpan.FromMinutes(3),
                };

                var ports = new[] { new EdgegapPortSpec("game", 7777, TransportKind.Udp) };
                await allocator.EnsureAppVersionAsync(image, ports);

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
