using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class JoinTargetTests
    {
        [Test]
        public void ForEndpoint_TrimsAndKeepsFields()
        {
            JoinTarget target = JoinTarget.ForEndpoint(" 203.0.113.7 ", 7777, TransportKind.WebSocket);

            Assert.That(target.Kind, Is.EqualTo(JoinTargetKind.Endpoint));
            Assert.That(target.Address, Is.EqualTo("203.0.113.7"));
            Assert.That(target.Port, Is.EqualTo(7777));
            Assert.That(target.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(target.RelayJoinCode, Is.Null);
            Assert.That(target.ToString(), Is.EqualTo("203.0.113.7:7777 over WebSocket"));
        }

        [TestCase(null)]
        [TestCase("  ")]
        [TestCase("0.0.0.0")]
        [TestCase("::")]
        [TestCase("my host")]
        public void ForEndpoint_RejectsBadAddress(string address)
        {
            Assert.Throws<ArgumentException>(() => JoinTarget.ForEndpoint(address, 7777));
        }

        [Test]
        public void ForEndpoint_RejectsPortZeroAndUnknownTransport()
        {
            Assert.Throws<ArgumentException>(() => JoinTarget.ForEndpoint("10.0.0.1", 0));
            Assert.Throws<ArgumentException>(() => JoinTarget.ForEndpoint("10.0.0.1", 7777, (TransportKind)9));
        }

        [Test]
        public void ForRelay_KeepsTheRelayCode()
        {
            JoinTarget target = JoinTarget.ForRelay(" BCDF67 ");

            Assert.That(target.Kind, Is.EqualTo(JoinTargetKind.Relay));
            Assert.That(target.RelayJoinCode, Is.EqualTo("BCDF67"));
            Assert.That(target.Address, Is.Null);
            Assert.That(target.Port, Is.EqualTo(0));
            Assert.That(target.Transport, Is.EqualTo(TransportKind.Udp));
            Assert.That(target.ToString(), Is.EqualTo("relay BCDF67 over Udp"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("AB CD")]
        public void ForRelay_RejectsBadCode(string relayCode)
        {
            Assert.Throws<ArgumentException>(() => JoinTarget.ForRelay(relayCode));
        }

        [Test]
        public void Equality_IsByValue()
        {
            Assert.That(JoinTarget.ForEndpoint("Example.com", 7777), Is.EqualTo(JoinTarget.ForEndpoint("example.com", 7777)));
            Assert.That(JoinTarget.ForEndpoint("example.com", 7777).GetHashCode(),
                Is.EqualTo(JoinTarget.ForEndpoint("EXAMPLE.COM", 7777).GetHashCode()));
            Assert.That(JoinTarget.ForEndpoint("example.com", 7777), Is.Not.EqualTo(JoinTarget.ForEndpoint("example.com", 7778)));
            Assert.That(JoinTarget.ForEndpoint("example.com", 7777),
                Is.Not.EqualTo(JoinTarget.ForEndpoint("example.com", 7777, TransportKind.WebSocket)));
            Assert.That(JoinTarget.ForRelay("BCDF67"), Is.EqualTo(JoinTarget.ForRelay("BCDF67")));
            Assert.That(JoinTarget.ForRelay("BCDF67"), Is.Not.EqualTo(JoinTarget.ForRelay("bcdf67")));
            Assert.That(JoinTarget.ForRelay("BCDF67").Equals(null), Is.False);
        }
    }

    /// <summary>
    /// The <see cref="IJoinCodeService"/> contract, run against <see cref="InMemoryJoinCodeService"/>.
    /// The adapter's Relay and rendezvous implementations (UM-T13) keep the same contract.
    /// </summary>
    [TestFixture]
    public class InMemoryJoinCodeServiceTests
    {
        private InMemoryJoinCodeService service;

        [SetUp]
        public void SetUp()
        {
            service = new InMemoryJoinCodeService();
        }

        [TearDown]
        public void TearDown()
        {
            service.Dispose();
        }

        [Test]
        public async Task Publish_ThenResolve_ReturnsTheEndpoint()
        {
            JoinTarget endpoint = JoinTarget.ForEndpoint("203.0.113.7", 7777);

            JoinCode code = await service.PublishAsync(endpoint);

            Assert.That(code.IsEmpty, Is.False);
            Assert.That(code.Data.Length, Is.EqualTo(JoinCodeFormat.DefaultDataLength));
            Assert.That(await service.ResolveAsync(code), Is.EqualTo(endpoint));
        }

        [Test]
        public async Task Publish_ThenResolve_ReturnsTheRelayAllocation()
        {
            JoinTarget relay = JoinTarget.ForRelay("BCDF67", TransportKind.WebSocket);

            JoinCode code = await service.PublishAsync(relay);

            Assert.That(await service.ResolveAsync(code), Is.EqualTo(relay));
        }

        [Test]
        public async Task Resolve_AcceptsWhatAPlayerTypes()
        {
            JoinTarget endpoint = JoinTarget.ForEndpoint("203.0.113.7", 7777);
            JoinCode code = await service.PublishAsync(endpoint);

            string typed = code.ToString().ToLowerInvariant().Replace("-", " ");
            JoinCode parsed = JoinCode.Parse(typed);

            Assert.That(await service.ResolveAsync(parsed), Is.EqualTo(endpoint));
        }

        [Test]
        public async Task Resolve_UnknownOrEmptyCode_ReturnsNull()
        {
            Assert.That(await service.ResolveAsync(JoinCode.Generate()), Is.Null);
            Assert.That(await service.ResolveAsync(JoinCode.Empty), Is.Null);
        }

        [Test]
        public async Task Revoke_StopsTheCodeResolving_AndIsIdempotent()
        {
            JoinCode code = await service.PublishAsync(JoinTarget.ForEndpoint("10.0.0.2", 7777));

            Assert.That(await service.RevokeAsync(code), Is.True);
            Assert.That(await service.ResolveAsync(code), Is.Null);
            Assert.That(await service.RevokeAsync(code), Is.False);
            Assert.That(await service.RevokeAsync(JoinCode.Generate()), Is.False);
            Assert.That(service.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task Revoke_LeavesOtherCodesAlone()
        {
            JoinTarget first = JoinTarget.ForEndpoint("10.0.0.2", 7777);
            JoinTarget second = JoinTarget.ForEndpoint("10.0.0.3", 7777);
            JoinCode a = await service.PublishAsync(first);
            JoinCode b = await service.PublishAsync(second);

            await service.RevokeAsync(a);

            Assert.That(await service.ResolveAsync(b), Is.EqualTo(second));
            Assert.That(service.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Publish_GivesEveryPublicationItsOwnCode()
        {
            JoinTarget target = JoinTarget.ForEndpoint("10.0.0.2", 7777);
            var codes = new HashSet<JoinCode>();

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(codes.Add(await service.PublishAsync(target)), Is.True);
            }

            Assert.That(service.Count, Is.EqualTo(1000));
        }

        [Test]
        public async Task Publish_SkipsACodeThatIsAlreadyLive()
        {
            // The RNG repeats one 6-byte block, then moves on: the second publish must not reuse the first code.
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            using (var rng = new JoinCodeTests.SequenceRng(bytes))
            using (var seeded = new InMemoryJoinCodeService(6, rng))
            {
                JoinCode first = await seeded.PublishAsync(JoinTarget.ForEndpoint("10.0.0.2", 7777));
                JoinCode second = await seeded.PublishAsync(JoinTarget.ForEndpoint("10.0.0.3", 7777));

                Assert.That(second, Is.Not.EqualTo(first));
                Assert.That(second.Data, Is.EqualTo(JoinCode.Generate(6, new JoinCodeTests.SequenceRng(7, 8, 9, 10, 11, 12)).Data));
            }
        }

        [Test]
        public void Publish_GivesUpWhenTheRngNeverChanges()
        {
            using (var rng = new JoinCodeTests.SequenceRng(5))
            using (var stuck = new InMemoryJoinCodeService(6, rng))
            {
                Assert.DoesNotThrowAsync(() => stuck.PublishAsync(JoinTarget.ForRelay("BCDF67")));
                Assert.ThrowsAsync<InvalidOperationException>(() => stuck.PublishAsync(JoinTarget.ForRelay("BCDF67")));
            }
        }

        [Test]
        public void Publish_RejectsNullTarget()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => service.PublishAsync(null));
        }

        [Test]
        public void Calls_HonourCancellation()
        {
            var cancelled = new CancellationToken(true);

            Assert.That(() => service.PublishAsync(JoinTarget.ForRelay("BCDF67"), cancelled), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(() => service.ResolveAsync(JoinCode.Generate(), cancelled), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(() => service.RevokeAsync(JoinCode.Generate(), cancelled), Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void Constructor_RejectsLengthOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryJoinCodeService(JoinCodeFormat.MinDataLength - 1, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryJoinCodeService(JoinCodeFormat.MaxDataLength + 1, null));
        }

        [Test]
        public async Task Service_IsSafeUnderConcurrentPublishes()
        {
            JoinTarget target = JoinTarget.ForEndpoint("10.0.0.2", 7777);
            var tasks = new List<Task<JoinCode>>();
            for (int i = 0; i < 64; i++)
            {
                tasks.Add(Task.Run(() => service.PublishAsync(target)));
            }

            JoinCode[] codes = await Task.WhenAll(tasks);

            Assert.That(new HashSet<JoinCode>(codes).Count, Is.EqualTo(64));
            Assert.That(service.Count, Is.EqualTo(64));
        }
    }
}
