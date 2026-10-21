using System;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class MatchRequestTests
    {
        [Test]
        public void Constructor_TrimsIdAndRegion_DefaultsMetadataToEmpty()
        {
            var request = new MatchRequest("  match-1  ", 4, "  eu-west  ");

            Assert.That(request.MatchId, Is.EqualTo("match-1"));
            Assert.That(request.MaxPlayers, Is.EqualTo(4));
            Assert.That(request.Region, Is.EqualTo("eu-west"));
            Assert.That(request.Metadata, Is.Empty);
        }

        [Test]
        public void Constructor_NoRegion_IsNull()
        {
            Assert.That(new MatchRequest("match-1", 1).Region, Is.Null);
            Assert.That(new MatchRequest("match-1", 1, "   ").Region, Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Constructor_EmptyMatchId_Throws(string matchId)
        {
            Assert.Throws<ArgumentException>(() => new MatchRequest(matchId, 1));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Constructor_MaxPlayersLessThanOne_Throws(int maxPlayers)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRequest("match-1", maxPlayers));
        }

        [Test]
        public void ToString_MentionsMatchIdAndPlayers()
        {
            Assert.That(new MatchRequest("match-1", 4).ToString(), Does.Contain("match-1").And.Contain("4 players"));
        }
    }

    [TestFixture]
    public class MatchAllocationTests
    {
        private static readonly JoinTarget Endpoint = JoinTarget.ForEndpoint("203.0.113.10", 7777, TransportKind.Udp);

        [Test]
        public void Constructor_TrimsAllocationId()
        {
            var allocation = new MatchAllocation("  alloc-1  ", Endpoint, JoinCode.Generate());
            Assert.That(allocation.AllocationId, Is.EqualTo("alloc-1"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Constructor_EmptyAllocationId_Throws(string allocationId)
        {
            Assert.Throws<ArgumentException>(() => new MatchAllocation(allocationId, Endpoint, JoinCode.Generate()));
        }

        [Test]
        public void Constructor_NullEndpoint_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MatchAllocation("alloc-1", null, JoinCode.Generate()));
        }

        [Test]
        public void Constructor_EmptyCode_Throws()
        {
            Assert.Throws<ArgumentException>(() => new MatchAllocation("alloc-1", Endpoint, JoinCode.Empty));
        }
    }

    [TestFixture]
    public class MatchAllocationExceptionTests
    {
        [Test]
        public void IsTransient_DefaultsToFalse()
        {
            var ex = new MatchAllocationException("nope");
            Assert.That(ex.IsTransient, Is.False);
            Assert.That(ex.Message, Is.EqualTo("nope"));
        }

        [Test]
        public void IsTransient_CarriesInnerException()
        {
            var inner = new InvalidOperationException("boom");
            var ex = new MatchAllocationException("wrapped", true, inner);
            Assert.That(ex.IsTransient, Is.True);
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }
}
