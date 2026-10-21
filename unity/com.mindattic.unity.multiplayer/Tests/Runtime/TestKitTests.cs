using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T56: the test kit's own guarantees, without a network: free ports really are free, a wait that times out fails
    /// with a message saying what never happened, and an <see cref="Awaiting{T}"/> refuses to be read early.
    /// </summary>
    public class TestKitTests
    {
        [Test]
        public void FreePorts_CanBeBoundStraightAway()
        {
            ushort udp = FreePort.Udp();
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                Assert.DoesNotThrow(() => socket.Bind(new IPEndPoint(IPAddress.Any, udp)), "UDP " + udp);
            }

            ushort tcp = FreePort.Tcp();
            var listener = new TcpListener(IPAddress.Any, tcp);
            Assert.DoesNotThrow(() => listener.Start(), "TCP " + tcp);
            listener.Stop();

            Assert.That(udp, Is.Not.Zero);
            Assert.That(tcp, Is.Not.Zero);
        }

        [Test]
        public void FreePortFor_PicksTheTransportsProtocol()
        {
            ushort web = FreePort.For(TransportKind.WebSocket);
            var listener = new TcpListener(IPAddress.Any, web);
            Assert.DoesNotThrow(() => listener.Start(), "a WebSocket port is a free TCP port");
            listener.Stop();
            Assert.That(FreePort.For(TransportKind.Udp), Is.Not.Zero);
        }

        [Test]
        public void Until_TimingOut_FailsSayingWhatNeverHappened()
        {
            IEnumerator wait = NetAwait.Until(() => false, () => "the door never opened (it is " + "shut" + ")", timeoutSeconds: 0.05f);
            AssertionException failure = Assert.Throws<AssertionException>(() => Drain(wait));
            Assert.That(failure.Message, Does.StartWith("Timed out after "));
            Assert.That(failure.Message, Does.Contain("the door never opened (it is shut)"));
        }

        [Test]
        public void ForSynced_TimingOut_ReportsTheLastValueSeen()
        {
            int value = 3;
            IEnumerator wait = NetAwait.ForSynced(() => value, 5, "Ann's score", timeoutSeconds: 0.05f);
            AssertionException failure = Assert.Throws<AssertionException>(() => Drain(wait));
            Assert.That(failure.Message, Does.Contain("Ann's score never became 5 (last 3)"));
        }

        [Test]
        public void ForSynced_EndsAsSoonAsTheValueArrives()
        {
            int value = 5;
            Assert.DoesNotThrow(() => Drain(NetAwait.ForSynced(() => value, 5, "the score", timeoutSeconds: 0.05f)));
        }

        [Test]
        public void ForTask_EndsWithTheResult_AndRethrowsAFault()
        {
            Awaiting<int> done = NetAwait.ForTask(Task.FromResult(42));
            Assert.That(() => done.Result, Throws.InvalidOperationException, "not read before it was waited for");
            Drain(done);
            Assert.That(done.IsDone, Is.True);
            Assert.That(done.Result, Is.EqualTo(42));

            IEnumerator faulted = NetAwait.ForTask(Task.FromException(new InvalidOperationException("boom")));
            Assert.That(() => Drain(faulted), Throws.InvalidOperationException.With.Message.EqualTo("boom"));
        }

        [Test]
        public void ForTask_TimingOut_NamesTheTask()
        {
            var never = new TaskCompletionSource<bool>();
            AssertionException failure = Assert.Throws<AssertionException>(() => Drain(NetAwait.ForTask(never.Task, "the lookup", 0.05f)));
            Assert.That(failure.Message, Does.Contain("the lookup never finished"));
        }

        [Test]
        public void InProcessRendezvous_PointsConfigsAtItself()
        {
            using (var rendezvous = new InProcessRendezvousServer(ttlSeconds: 5, secret: "s3cret"))
            {
                NetConfig server = rendezvous.ForServer(new NetConfig { Port = 1234 });
                Assert.That(server.Port, Is.EqualTo(1234), "the rest of the config is kept");
                Assert.That(server.RendezvousUrl, Is.EqualTo(rendezvous.Url));
                Assert.That(server.RendezvousSecret, Is.EqualTo("s3cret"));
                NetConfig client = rendezvous.ForClient();
                Assert.That(client.RendezvousUrl, Is.EqualTo(rendezvous.Url));
                Assert.That(client.RendezvousSecret, Is.Null.Or.Empty, "a client never carries the secret");
                Assert.That(rendezvous.Url, Does.StartWith("http://127.0.0.1:"));
                Assert.That(rendezvous.Count, Is.Zero);
            }
        }

        /// <summary>Runs a wait to its end on this thread, a millisecond per step (real time is what the waits measure).</summary>
        private static void Drain(IEnumerator wait)
        {
            int steps = 0;
            while (wait.MoveNext())
            {
                if (wait.Current is IEnumerator nested)
                {
                    Drain(nested);
                }

                Thread.Sleep(1);
                Assert.That(++steps, Is.LessThan(100000), "the wait never ended");
            }
        }
    }

    /// <summary>
    /// UM-T56: the test kit's fixture end to end, the way a game's test uses it: a host and one client on a free port, a
    /// spawned entity found on the client, a synced field awaited there, and the async waits.
    /// </summary>
    public class NetSessionTestFixtureTests : NetSessionTest
    {
        private UnityEngine.GameObject _prefab;

        public NetSessionTestFixtureTests() : base(Topology.Host, clients: 1)
        {
        }

        protected override void OnSessionsCreated()
        {
            _prefab = CreatePrefab<TestAvatar>();
        }

        [UnityTest]
        public IEnumerator HostAndClient_SpawnAndSynced_AreAwaitable()
        {
            Assert.That(Port, Is.Not.Zero);
            Assert.That(Clients.Count, Is.EqualTo(1));
            yield return StartAndJoinAll();
            Assert.That(Server.IsHost, Is.True);
            Assert.That(Client.State, Is.EqualTo(SessionState.Connected));
            Assert.That(Client.Config.Port, Is.EqualTo(Port));

            TestAvatar onHost = Spawn<TestAvatar>(_prefab, owner: Client);
            Awaiting<TestAvatar> owned = WaitForSpawn<TestAvatar>(Client, owner: Client.LocalClientId);
            yield return owned;
            Assert.That(owned.Result.IsOwner, Is.True, "the client's copy is the one it owns");
            Awaiting<TestAvatar> copy = WaitForCopy(Client, onHost);
            yield return copy;
            Assert.That(copy.Result, Is.SameAs(owned.Result));

            onHost.Score = 7;
            yield return WaitForSynced(() => copy.Result.Score, 7, "the client's Score");

            // The async forms, for test code written with async/await.
            Task<TestAvatar> found = NetAwait.ForSpawnAsync<TestAvatar>(Client, owner: Client.LocalClientId);
            Awaiting<TestAvatar> foundAsync = WaitFor(found, "ForSpawnAsync");
            yield return foundAsync;
            Assert.That(foundAsync.Result, Is.SameAs(copy.Result));

            onHost.Score = 8;
            yield return WaitFor(NetAwait.ForSyncedAsync(() => copy.Result.Score, 8, "the client's Score"), "ForSyncedAsync");
            yield return WaitFor(NetAwait.UntilAsync(() => Server.ConnectedClientIds.Count == 2, "two connections"), "UntilAsync");
        }

        [UnityTest]
        public IEnumerator AWaitThatNeverEnds_FailsTheTestWithItsMessage()
        {
            yield return StartServer();
            IEnumerator wait = NetAwait.ForSpawn<TestAvatar>(Server, timeoutSeconds: 0.2f);
            AssertionException failure = null;
            while (true)
            {
                try
                {
                    if (!wait.MoveNext())
                    {
                        break;
                    }
                }
                catch (AssertionException e)
                {
                    failure = e;
                    break;
                }

                yield return null;
            }

            Assert.That(failure, Is.Not.Null, "nothing was spawned, so the wait has to fail");
            Assert.That(failure.Message, Does.Contain("never saw a spawned TestAvatar").And.Contain("Running"));
        }
    }
}
