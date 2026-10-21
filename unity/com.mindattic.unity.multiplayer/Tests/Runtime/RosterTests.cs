using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T55: the player roster on every peer. A host (Ann) and two clients (Bob, and Cat who joins late), over real
    /// Unity Transport on loopback through the test kit's <see cref="NetSessionTest"/>: names and colours from the
    /// approval payload seen everywhere, a late joiner getting the whole roster, a reconnect keeping seat and identity, a
    /// rename reaching every peer, and a bad name refused by the server.
    /// </summary>
    public class RosterTests : NetSessionTest
    {
        private static readonly PlayerColor AnnRed = PlayerColors.Palette[0];
        private static readonly PlayerColor BobBlue = PlayerColors.Palette[1];
        private static readonly PlayerColor Green = PlayerColors.Palette[2];

        public RosterTests() : base(Topology.Host, clients: 2)
        {
        }

        private NetSession Host => Server;

        private NetSession Bob => Clients[0];

        private NetSession Cat => Clients[1];

        protected override void OnSessionsCreated()
        {
            Host.Identity = new PlayerIdentity("ann-token", "Ann", AnnRed);
            Bob.Identity = new PlayerIdentity("bob-token", "Bob", BobBlue);

            // Cat never picked a colour (the default white), and her name carries an invisible zero-width space.
            Cat.Identity = new PlayerIdentity("cat-token", "Cat​", PlayerColor.White);
        }

        private static string Names(NetSession peer)
        {
            return string.Join(", ", peer.Roster.All.Select(p => p.ToString()));
        }

        private IEnumerator WaitForRoster(NetSession peer, int count)
        {
            yield return WaitUntil(() => peer.Roster.Count == count, () => "the roster never had " + count + " players: " + Names(peer));
        }

        private static NetPlayer Named(NetSession peer, string name)
        {
            return peer.Roster.All.SingleOrDefault(p => p.DisplayName == name);
        }

        [UnityTest]
        public IEnumerator NamesAndColours_AreSeenByEveryPeer_IncludingALateJoiner()
        {
            var joinedOnBob = new List<string>();
            Bob.Roster.Joined += p => joinedOnBob.Add(p.DisplayName);

            yield return StartServer();
            yield return Join(Bob);
            yield return WaitForRoster(Host, 2);
            yield return WaitForRoster(Bob, 2);

            // Cat joins after Bob: everyone, Cat included, ends up with all three.
            yield return Join(Cat);
            foreach (NetSession peer in Sessions)
            {
                yield return WaitForRoster(peer, 3);
            }

            foreach (NetSession peer in Sessions)
            {
                NetPlayer ann = Named(peer, "Ann");
                NetPlayer bob = Named(peer, "Bob");
                NetPlayer cat = Named(peer, "Cat");
                Assert.That(ann.IsHost && !bob.IsHost && !cat.IsHost, Is.True, "only Ann hosts");
                Assert.That(ann.Color, Is.EqualTo(AnnRed));
                Assert.That(bob.Color, Is.EqualTo(BobBlue));
                Assert.That(cat.Color, Is.EqualTo(PlayerColors.ForSeat(cat.Seat)), "Cat never picked a colour: her seat's");
                Assert.That(new[] { ann.Seat, bob.Seat, cat.Seat }, Is.EqualTo(new[] { 0, 1, 2 }));
                Assert.That(bob.ClientId, Is.EqualTo(Bob.LocalClientId));
                Assert.That(cat.ClientId, Is.EqualTo(Cat.LocalClientId));
                Assert.That(peer.Roster.All.All(p => p.IsConnected), Is.True);
            }

            Assert.That(Host.Roster.Local.DisplayName, Is.EqualTo("Ann"));
            Assert.That(Bob.Roster.Local.DisplayName, Is.EqualTo("Bob"));
            Assert.That(Cat.Roster.Local.DisplayName, Is.EqualTo("Cat"));
            Assert.That(Cat.Roster.All.Count(p => p.IsLocal), Is.EqualTo(1));
            Assert.That(Cat.Roster.TryGet(Bob.LocalClientId, out NetPlayer bobOnCat) && bobOnCat.DisplayName == "Bob", Is.True, "by client id");
            Assert.That(Cat.Roster.TryGetBySeat(0, out NetPlayer seat0) && seat0.DisplayName == "Ann", Is.True, "by seat");
            Assert.That(joinedOnBob, Is.EqualTo(new[] { "Ann", "Bob", "Cat" }));

            // The server measures every client's round trip and tells everyone.
            yield return WaitUntil(() => Cat.Roster.All.All(p => p.PingMs >= 0) && Bob.Roster.All.All(p => p.PingMs >= 0),
                () => "round trips never arrived: " + Names(Cat));
            Assert.That(Named(Bob, "Ann").PingMs, Is.Zero, "the host's own round trip");
        }

        [UnityTest]
        public IEnumerator ARename_ReachesEveryPeer_AndABadNameIsRefusedByTheServer()
        {
            yield return StartAndJoinAll();
            foreach (NetSession peer in Sessions)
            {
                yield return WaitForRoster(peer, 3);
            }

            var changesOnCat = new List<NetPlayerChange>();
            Cat.Roster.Changed += c =>
            {
                if (!c.OnlyPingChanged)
                {
                    changesOnCat.Add(c);
                }
            };

            Task<PlayerChangeResult> rename = Bob.Roster.RequestChange("Bobby", Green);
            yield return WaitFor(rename, "Bob's rename");
            Assert.That(rename.Result.Accepted, Is.True, rename.Result.Reason);
            Assert.That(rename.Result.Player.DisplayName, Is.EqualTo("Bobby"));
            Assert.That(Bob.Roster.Local.DisplayName, Is.EqualTo("Bobby"), "the update arrives before the answer");
            Assert.That(Bob.Identity.DisplayName, Is.EqualTo("Bobby"), "kept for a reconnect");
            Assert.That(Bob.Identity.Color, Is.EqualTo(Green));
            foreach (NetSession peer in Sessions)
            {
                yield return WaitUntil(() => peer.Roster.TryGetBySeat(1, out NetPlayer p) && p.DisplayName == "Bobby" && p.Color == Green,
                    () => "the rename never arrived: " + Names(peer));
            }

            Assert.That(changesOnCat.Single().NameChanged && changesOnCat.Single().ColorChanged, Is.True);
            Assert.That(changesOnCat.Single().Previous.DisplayName, Is.EqualTo("Bob"));
            Assert.That(Host.GetSender(Bob.LocalClientId).DisplayName, Is.EqualTo("Bobby"), "the server's own view follows");

            // The server refuses what the rules refuse, and nothing changes anywhere.
            Task<PlayerChangeResult> invisible = Bob.Roster.RequestChange("Bo​b");
            Task<PlayerChangeResult> tooLong = Bob.Roster.RequestChange(new string('x', 33));
            Task<PlayerChangeResult> clear = Cat.Roster.RequestChange(null, new PlayerColor(1, 2, 3, 0));
            yield return WaitFor(Task.WhenAll(invisible, tooLong, clear), "the refusals");
            Assert.That(invisible.Result.Accepted, Is.False);
            Assert.That(invisible.Result.Reason, Does.Contain("invisible"));
            Assert.That(tooLong.Result.Reason, Does.Contain("at most 32"));
            Assert.That(clear.Result.Reason, Does.Contain("opaque"));
            Assert.That(Bob.Identity.DisplayName, Is.EqualTo("Bobby"), "a refused change is not kept");
            Assert.That(Named(Host, "Bobby"), Is.Not.Null);
            Assert.That(changesOnCat.Count, Is.EqualTo(1), "no change reached anyone");

            // The host renames itself the same way, applied at once.
            Task<PlayerChangeResult> annie = Host.Roster.RequestChange("Annie");
            Assert.That(annie.IsCompleted && annie.Result.Accepted, Is.True);
            Assert.That(Host.Identity.DisplayName, Is.EqualTo("Annie"));
            yield return WaitUntil(() => Cat.Roster.TryGetBySeat(0, out NetPlayer p) && p.DisplayName == "Annie", () => "Annie never arrived: " + Names(Cat));
            Assert.That(Named(Cat, "Annie").Color, Is.EqualTo(AnnRed), "a name-only change keeps the colour");
        }

        [UnityTest]
        public IEnumerator AReconnect_KeepsTheSeatAndTheIdentity()
        {
            yield return StartAndJoinAll();
            foreach (NetSession peer in Sessions)
            {
                yield return WaitForRoster(peer, 3);
            }

            Task<PlayerChangeResult> rename = Bob.Roster.RequestChange("Bobby", Green);
            yield return WaitFor(rename);
            Assert.That(rename.Result.Accepted, Is.True, rename.Result.Reason);
            yield return WaitUntil(() => Named(Cat, "Bobby") != null, () => Names(Cat));

            var leftOnCat = new List<NetPlayer>();
            var joinedOnCat = new List<NetPlayer>();
            Cat.Roster.Left += leftOnCat.Add;
            Cat.Roster.Joined += joinedOnCat.Add;
            ulong before = Bob.LocalClientId;

            // Bob drops: his seat is held, and every peer still lists him, not connected.
            Bob.Leave();
            yield return WaitUntil(() => Cat.Roster.TryGetBySeat(1, out NetPlayer p) && !p.IsConnected, () => "Bob never showed as dropped: " + Names(Cat));
            Assert.That(Bob.Roster.Count, Is.Zero, "Bob's own roster empties when his session ends");
            Assert.That(Named(Host, "Bobby").IsConnected, Is.False);

            // He comes back with the same player token: same seat, same (renamed) identity, a new connection.
            yield return WaitUntil(() => Bob.CanStart, "Bob never finished leaving");
            yield return Join(Bob);
            foreach (NetSession peer in Sessions)
            {
                yield return WaitUntil(() => peer.Roster.TryGetBySeat(1, out NetPlayer p) && p.IsConnected && p.ClientId == Bob.LocalClientId,
                    () => "Bob never came back: " + Names(peer));
                NetPlayer bob = Named(peer, "Bobby");
                Assert.That(bob.Seat, Is.EqualTo(1));
                Assert.That(bob.Color, Is.EqualTo(Green));
            }

            Assert.That(Bob.LocalClientId, Is.Not.EqualTo(before), "a new connection");
            Assert.That(Bob.Roster.Local.Seat, Is.EqualTo(1));
            Assert.That(Bob.Roster.Count, Is.EqualTo(3), "the returning player gets the whole roster");
            Assert.That(leftOnCat, Is.Empty, "a held seat never left");
            Assert.That(joinedOnCat, Is.Empty, "and never joined again");
        }

        [UnityTest]
        public IEnumerator AKick_RemovesThePlayerFromEveryRoster()
        {
            yield return StartAndJoinAll();
            foreach (NetSession peer in Sessions)
            {
                yield return WaitForRoster(peer, 3);
            }

            var left = new List<string>();
            Cat.Roster.Left += p => left.Add(p.DisplayName);
            Assert.That(Host.Kick(Bob.LocalClientId, "Bye"), Is.True);
            yield return WaitForRoster(Cat, 2);
            yield return WaitForRoster(Host, 2);
            Assert.That(left, Is.EqualTo(new[] { "Bob" }));
        }
    }
}
