using System;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>UM-T10: the token bucket, the per-sender rate limiter and the RPC gate every generated stub runs.</summary>
    [TestFixture]
    public class TokenBucketTests
    {
        [Test]
        public void StartsFull_ThenRefusesUntilItRefills()
        {
            var bucket = new TokenBucket(10, 3, 0);

            Assert.That(bucket.TryTake(0), Is.True);
            Assert.That(bucket.TryTake(0), Is.True);
            Assert.That(bucket.TryTake(0), Is.True);
            Assert.That(bucket.TryTake(0), Is.False, "three tokens, three calls");

            Assert.That(bucket.TryTake(0.05), Is.False, "half a token after 50 ms at 10/s");
            Assert.That(bucket.TryTake(0.1), Is.True, "one token after 100 ms");
            Assert.That(bucket.TryTake(0.1), Is.False);
        }

        [Test]
        public void NeverHoldsMoreThanItsCapacity()
        {
            var bucket = new TokenBucket(100, 2, 0);
            Assert.That(bucket.Available(3600), Is.EqualTo(2));
            Assert.That(bucket.TryTake(3600) && bucket.TryTake(3600), Is.True);
            Assert.That(bucket.TryTake(3600), Is.False);
        }

        [Test]
        public void AClockGoingBackwardsAddsNothing()
        {
            var bucket = new TokenBucket(1, 1, 10);
            Assert.That(bucket.TryTake(10), Is.True);
            Assert.That(bucket.TryTake(5), Is.False);
            Assert.That(bucket.TryTake(10.5), Is.False);
            Assert.That(bucket.TryTake(11), Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsABadRate(double rate)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(rate, 1, 0));
        }

        [Test]
        public void RejectsACapacityBelowOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(1, 0, 0));
        }
    }

    [TestFixture]
    public class RpcRateLimiterTests
    {
        [Test]
        public void ZeroRateMeansUnlimited_AndKeepsNoBucket()
        {
            var limiter = new RpcRateLimiter();
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(limiter.TryAcquire(1, "A.Move", 0, 0, 0), Is.True);
            }

            Assert.That(limiter.BucketCount, Is.EqualTo(0));
            Assert.That(limiter.RejectedCount(1), Is.EqualTo(0));
        }

        [Test]
        public void BurstDefaultsToOneSecondOfRate()
        {
            var limiter = new RpcRateLimiter();
            int accepted = 0;
            for (int i = 0; i < 50; i++)
            {
                if (limiter.TryAcquire(1, "A.Move", 30, 0, 0))
                {
                    accepted++;
                }
            }

            Assert.That(accepted, Is.EqualTo(30));
            Assert.That(limiter.RejectedCount(1), Is.EqualTo(20));
        }

        [Test]
        public void AFractionalRateStillAllowsOneCall()
        {
            var limiter = new RpcRateLimiter();
            Assert.That(limiter.TryAcquire(1, "A.Vote", 0.5, 0, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, "A.Vote", 0.5, 0, 1), Is.False);
            Assert.That(limiter.TryAcquire(1, "A.Vote", 0.5, 0, 2), Is.True, "one token every two seconds");
        }

        [Test]
        public void ExplicitBurstSetsTheBucketSize()
        {
            var limiter = new RpcRateLimiter();
            Assert.That(limiter.TryAcquire(1, "A.Fire", 10, 2, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, "A.Fire", 10, 2, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, "A.Fire", 10, 2, 0), Is.False);
        }

        [Test]
        public void SendersAndRpcsHaveSeparateBuckets()
        {
            var limiter = new RpcRateLimiter();
            Assert.That(limiter.TryAcquire(1, "A.Move", 1, 1, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, "A.Move", 1, 1, 0), Is.False, "client 1 used its Move budget");
            Assert.That(limiter.TryAcquire(2, "A.Move", 1, 1, 0), Is.True, "client 2 is unaffected");
            Assert.That(limiter.TryAcquire(1, "A.Grab", 1, 1, 0), Is.True, "client 1's other RPCs are unaffected");
            Assert.That(limiter.RejectedCount(1), Is.EqualTo(1));
            Assert.That(limiter.RejectedCount(2), Is.EqualTo(0));
        }

        [Test]
        public void ForgetDropsOnlyThatSender()
        {
            var limiter = new RpcRateLimiter();
            limiter.TryAcquire(1, "A.Move", 1, 1, 0);
            limiter.TryAcquire(1, "A.Move", 1, 1, 0);
            limiter.TryAcquire(2, "A.Move", 1, 1, 0);

            limiter.Forget(1);

            Assert.That(limiter.BucketCount, Is.EqualTo(1));
            Assert.That(limiter.RejectedCount(1), Is.EqualTo(0));
            Assert.That(limiter.TryAcquire(1, "A.Move", 1, 1, 0), Is.True, "a fresh, full bucket");
            Assert.That(limiter.TryAcquire(2, "A.Move", 1, 1, 0), Is.False, "client 2 still spent");
        }

        [Test]
        public void ClearDropsEverything()
        {
            var limiter = new RpcRateLimiter();
            limiter.TryAcquire(1, "A.Move", 1, 1, 0);
            limiter.TryAcquire(1, "A.Move", 1, 1, 0);
            limiter.Clear();
            Assert.That(limiter.BucketCount, Is.EqualTo(0));
            Assert.That(limiter.RejectedCount(1), Is.EqualTo(0));
        }

        [Test]
        public void ChangingTheLimitStartsAFreshBucket()
        {
            var limiter = new RpcRateLimiter();
            Assert.That(limiter.TryAcquire(1, "A.Move", 1, 1, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, "A.Move", 5, 5, 0), Is.True);
        }

        [Test]
        public void NullRpcKeyThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new RpcRateLimiter().TryAcquire(1, null, 1, 1, 0));
        }
    }

    [TestFixture]
    public class RpcGateTests
    {
        [Test]
        public void ToServer_OnlyRunsOnTheServer()
        {
            var gate = new RpcGate();
            Assert.That(gate.CheckToServer("A.Move", false, 1, 1, true, 0, 0, 0), Is.EqualTo(RpcVerdict.NotServer));
        }

        [Test]
        public void ToServer_RequiresTheOwnerByDefault()
        {
            var gate = new RpcGate();
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 0, 0, 0), Is.EqualTo(RpcVerdict.Accepted));
            Assert.That(gate.CheckToServer("A.Move", true, 2, 1, true, 0, 0, 0), Is.EqualTo(RpcVerdict.NotOwner));
        }

        [Test]
        public void ToServer_WithoutOwnershipAcceptsAnyClient()
        {
            var gate = new RpcGate();
            Assert.That(gate.CheckToServer("A.Vote", true, 2, 1, false, 0, 0, 0), Is.EqualTo(RpcVerdict.Accepted));
        }

        [Test]
        public void ToServer_TheServerItselfPassesOwnership()
        {
            var gate = new RpcGate();
            Assert.That(gate.CheckToServer("A.Move", true, NetSender.ServerClientId, 3, true, 0, 0, 0), Is.EqualTo(RpcVerdict.Accepted));
        }

        [Test]
        public void ToServer_RateLimitsPerSender()
        {
            var gate = new RpcGate();
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 2, 0, 0), Is.EqualTo(RpcVerdict.Accepted));
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 2, 0, 0), Is.EqualTo(RpcVerdict.Accepted));
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 2, 0, 0), Is.EqualTo(RpcVerdict.RateLimited));
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 2, 0, 0.5), Is.EqualTo(RpcVerdict.Accepted));
            Assert.That(gate.Limiter.RejectedCount(1), Is.EqualTo(1));
        }

        [Test]
        public void ToServer_ANonOwnerSpendsNoneOfTheBudget()
        {
            var gate = new RpcGate();
            for (int i = 0; i < 10; i++)
            {
                Assert.That(gate.CheckToServer("A.Move", true, 2, 1, true, 1, 1, 0), Is.EqualTo(RpcVerdict.NotOwner));
            }

            Assert.That(gate.Limiter.BucketCount, Is.EqualTo(0));
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 1, 1, 0), Is.EqualTo(RpcVerdict.Accepted));
        }

        [Test]
        public void ForgetAndClearResetTheBudget()
        {
            var gate = new RpcGate();
            gate.CheckToServer("A.Move", true, 1, 1, true, 1, 1, 0);
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 1, 1, 0), Is.EqualTo(RpcVerdict.RateLimited));
            gate.Forget(1);
            Assert.That(gate.CheckToServer("A.Move", true, 1, 1, true, 1, 1, 0), Is.EqualTo(RpcVerdict.Accepted));
            gate.Clear();
            Assert.That(gate.Limiter.BucketCount, Is.EqualTo(0));
        }

        [Test]
        public void FromServer_AcceptsOnlyTheServer()
        {
            Assert.That(RpcGate.CheckFromServer(NetSender.ServerClientId), Is.EqualTo(RpcVerdict.Accepted));
            Assert.That(RpcGate.CheckFromServer(4), Is.EqualTo(RpcVerdict.NotFromServer));
        }

        [Test]
        public void NullLimiterThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new RpcGate(null));
        }
    }

    [TestFixture]
    public class NetSenderTests
    {
        [Test]
        public void CarriesClientSeatAndName()
        {
            var sender = new NetSender(7, 2, "Ann");
            Assert.That(sender.ClientId, Is.EqualTo(7UL));
            Assert.That(sender.SeatIndex, Is.EqualTo(2));
            Assert.That(sender.HasSeat, Is.True);
            Assert.That(sender.DisplayName, Is.EqualTo("Ann"));
            Assert.That(sender.IsServer, Is.False);
            Assert.That(sender.ToString(), Does.Contain("Ann").And.Contain("client 7").And.Contain("seat 2"));
        }

        [Test]
        public void NoSeatIsMinusOne_AndNullNameIsEmpty()
        {
            var sender = new NetSender(3, -9, null);
            Assert.That(sender.SeatIndex, Is.EqualTo(-1));
            Assert.That(sender.HasSeat, Is.False);
            Assert.That(sender.DisplayName, Is.Empty);
            Assert.That(default(NetSender).DisplayName, Is.Empty);
        }

        [Test]
        public void ConvertsToItsClientId()
        {
            ulong id = new NetSender(9, 0, "x");
            Assert.That(id, Is.EqualTo(9UL));
        }

        [Test]
        public void ServerIsClientZero()
        {
            Assert.That(NetSender.Server.IsServer, Is.True);
            Assert.That(NetSender.Server.ClientId, Is.EqualTo(NetSender.ServerClientId));
        }

        [Test]
        public void EqualityIsByValue()
        {
            Assert.That(new NetSender(1, 0, "a"), Is.EqualTo(new NetSender(1, 0, "a")));
            Assert.That(new NetSender(1, 0, "a") == new NetSender(1, 0, "a"), Is.True);
            Assert.That(new NetSender(1, 0, "a") != new NetSender(1, 1, "a"), Is.True);
            Assert.That(new NetSender(1, 0, "a").GetHashCode(), Is.EqualTo(new NetSender(1, 0, "a").GetHashCode()));
        }
    }

    [TestFixture]
    public class RpcAttributeTests
    {
        [Test]
        public void ToServerDefaults_ReliableOwnedUnlimited()
        {
            var a = new ToServerAttribute();
            Assert.That(a.Delivery, Is.EqualTo(Delivery.Reliable));
            Assert.That(a.RequireOwnership, Is.True);
            Assert.That(a.RateLimit, Is.EqualTo(0));
            Assert.That(a.Burst, Is.EqualTo(0));
        }

        [Test]
        public void EveryAttributeTargetsOneMemberKind()
        {
            AssertUsage(typeof(ToServerAttribute), AttributeTargets.Method);
            AssertUsage(typeof(ToClientsAttribute), AttributeTargets.Method);
            AssertUsage(typeof(ToOwnerAttribute), AttributeTargets.Method);
            AssertUsage(typeof(ToClientAttribute), AttributeTargets.Method);
            AssertUsage(typeof(SyncedAttribute), AttributeTargets.Field);
        }

        private static void AssertUsage(Type attribute, AttributeTargets targets)
        {
            var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(attribute, typeof(AttributeUsageAttribute));
            Assert.That(usage.ValidOn, Is.EqualTo(targets), attribute.Name);
            Assert.That(usage.AllowMultiple, Is.False, attribute.Name);
        }
    }
}
