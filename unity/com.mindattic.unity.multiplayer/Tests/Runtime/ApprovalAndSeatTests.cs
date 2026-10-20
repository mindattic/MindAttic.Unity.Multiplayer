using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T9: connection approval and seats, over a real Unity Transport on loopback. A Host and two Clients,
    /// all driven through <see cref="NetSession"/>: a mismatched client is refused with a reason it can show,
    /// and a player who drops (or whose old process died unnoticed) gets the same seat and the same player
    /// object back when it reconnects within the grace window.
    /// </summary>
    public class ApprovalAndSeatTests : NetcodeIntegrationTest
    {
        private const ushort Port = 47831;
        private const string Version = "1.0";

        private NetSession _host;
        private NetSession _a;
        private NetSession _b;
        private SeatProbe _seats;

        public ApprovalAndSeatTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 2;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            NetSessionTestSupport.PrepareManagers(m_NetworkManagers);
            _host = new NetSession(m_ServerNetworkManager) { ProtocolHash = new ProtocolHash(0x1111), Identity = new PlayerIdentity("host-token", "Host", PlayerColor.White) };
            _a = new NetSession(m_ClientNetworkManagers[0]) { ProtocolHash = new ProtocolHash(0x1111), Identity = new PlayerIdentity("player-a", "Ann", new PlayerColor(200, 10, 10)) };
            _b = new NetSession(m_ClientNetworkManagers[1]) { ProtocolHash = new ProtocolHash(0x1111), Identity = new PlayerIdentity("player-b", "Bob", new PlayerColor(10, 10, 200)) };
            _seats = new SeatProbe(_host);
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _a?.Dispose();
            _b?.Dispose();
            _host?.Dispose();
            _a = _b = _host = null;
            yield return null;
        }

        private static NetConfig Config(string version = Version, int maxPlayers = 4, double grace = 60)
        {
            return new NetConfig { Port = Port, GameVersion = version, MaxPlayers = maxPlayers, ReconnectGraceSeconds = grace };
        }

        private IEnumerator StartHost(int maxPlayers = 4, double grace = 60)
        {
            Assert.That(_host.StartHost(Config(maxPlayers: maxPlayers, grace: grace)), Is.True, "StartHost");
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running (state " + _host.State + ")");
            Assert.That(_host.Seats.Count, Is.EqualTo(1), "the host's own player takes a seat");
            Assert.That(_host.Seats[0].PlayerToken, Is.EqualTo("host-token"));
        }

        private IEnumerator Join(NetSession client, string version = Version)
        {
            Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, Port, Config(version)), Is.True, "JoinByAddress");
            yield return WaitForConditionOrTimeOut(() => client.State == SessionState.Connected);
            AssertOnTimeout("the client never connected (state " + client.State + ", " + client.LastDisconnect + ")");
        }

        private IEnumerator ExpectRejected(NetSession client, ApprovalRejection rejection, string version = Version)
        {
            Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, Port, Config(version)), Is.True, "JoinByAddress");
            yield return WaitForConditionOrTimeOut(() => client.State == SessionState.Disconnected);
            AssertOnTimeout("the client was never turned away (state " + client.State + ")");
            Assert.That(client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Rejected), client.LastDisconnect.ToString());
            Assert.That(client.LastDisconnect.Rejection, Is.EqualTo(rejection), client.LastDisconnect.ToString());
            Assert.That(client.LastDisconnect.Message, Does.Not.Contain("|"), "the client shows the sentence, not the wire format");
        }

        [UnityTest]
        public IEnumerator VersionMismatch_IsRejectedWithAReasonTheClientShows()
        {
            yield return StartHost();

            yield return ExpectRejected(_a, ApprovalRejection.VersionMismatch, version: "0.9");

            Assert.That(_a.LastDisconnect.Message, Does.Contain("1.0").And.Contain("0.9"));
            Assert.That(_seats.Rejections.Single().Decision.Rejection, Is.EqualTo(ApprovalRejection.VersionMismatch));
            Assert.That(_host.Seats.Count, Is.EqualTo(1), "a refused client gets no seat");
            Assert.That(_seats.Disconnects, Is.Empty, "a refused client was never a player");
        }

        [UnityTest]
        public IEnumerator ProtocolMismatch_IsRejectedWithAReasonTheClientShows()
        {
            yield return StartHost();
            _a.ProtocolHash = new ProtocolHash(0x2222);

            yield return ExpectRejected(_a, ApprovalRejection.ProtocolMismatch);

            Assert.That(_a.LastDisconnect.Message, Does.Contain(new ProtocolHash(0x1111).ToString()).And.Contain(new ProtocolHash(0x2222).ToString()));
        }

        [UnityTest]
        public IEnumerator FullServer_IsRejectedWithAReasonTheClientShows()
        {
            yield return StartHost(maxPlayers: 2);
            yield return Join(_a);

            yield return ExpectRejected(_b, ApprovalRejection.Full);

            Assert.That(_b.LastDisconnect.Message, Does.Contain("full"));
            Assert.That(_a.State, Is.EqualTo(SessionState.Connected), "the seated player is unaffected");
        }

        [UnityTest]
        public IEnumerator DroppedClient_ReconnectingWithinGrace_GetsTheSameSeatAndPlayerObject()
        {
            yield return StartHost();
            yield return Join(_b);
            yield return Join(_a);
            ulong firstId = _a.LocalClientId;
            Assert.That(_host.TryGetSeat(firstId, out Seat seat), Is.True);
            Assert.That(seat.Index, Is.EqualTo(2), "host 0, Bob 1, Ann 2");
            Assert.That(seat.DisplayName, Is.EqualTo("Ann"));
            Assert.That(_seats.Taken.Select(s => s.PlayerToken), Is.EqualTo(new[] { "host-token", "player-b", "player-a" }));
            GameObject playerObject = _host.GetSeatPlayerObject(seat.Index);
            Assert.That(playerObject, Is.Not.Null, "the harness's player prefab was spawned for the seat");
            Assert.That(playerObject, Is.SameAs(m_ServerNetworkManager.ConnectedClients[firstId].PlayerObject.gameObject));

            // The player drops.
            _a.Leave();
            yield return WaitForConditionOrTimeOut(() => _seats.Held.Count == 1 && _a.CanStart);
            AssertOnTimeout("the host never held the dropped player's seat");
            Assert.That(_seats.Held[0].Index, Is.EqualTo(seat.Index));
            Assert.That(_seats.Disconnects.Single().ClientId, Is.EqualTo(firstId));
            Assert.That(_seats.Disconnects.Single().Why.Reason, Is.EqualTo(DisconnectReason.ClientLeft), _seats.Disconnects.Single().ToString());
            Assert.That(_seats.Disconnects.Single().Seat.Status, Is.EqualTo(SeatStatus.Held));
            Assert.That(playerObject != null, Is.True, "the player object waits with the seat");
            Assert.That(playerObject.GetComponent<NetworkObject>().IsSpawned, Is.True);

            // ...and reconnects.
            yield return Join(_a);
            ulong secondId = _a.LocalClientId;
            Assert.That(secondId, Is.Not.EqualTo(firstId), "a new connection");
            yield return WaitForConditionOrTimeOut(() => _seats.Reclaimed.Count == 1);
            AssertOnTimeout("the host never gave the seat back");
            Assert.That(_seats.Reclaimed[0].Index, Is.EqualTo(seat.Index), "same seat");
            Assert.That(_host.TryGetSeat(secondId, out Seat back), Is.True);
            Assert.That(back.Index, Is.EqualTo(seat.Index));
            Assert.That(back.Reconnects, Is.EqualTo(1));
            Assert.That(_host.GetSeatPlayerObject(seat.Index), Is.SameAs(playerObject), "same player object");

            NetworkObject serverObject = playerObject.GetComponent<NetworkObject>();
            Assert.That(serverObject.OwnerClientId, Is.EqualTo(secondId));
            Assert.That(serverObject.IsPlayerObject, Is.True);
            Assert.That(m_ServerNetworkManager.ConnectedClients[secondId].PlayerObject, Is.SameAs(serverObject));
            Assert.That(_host.Seats.Count, Is.EqualTo(3), "no extra seat was taken");

            // The client sees it as its own player object.
            NetworkManager client = m_ClientNetworkManagers[0];
            yield return WaitForConditionOrTimeOut(() => client.SpawnManager.SpawnedObjects.ContainsKey(serverObject.NetworkObjectId));
            AssertOnTimeout("the client never saw its player object again");
            NetworkObject clientView = client.SpawnManager.SpawnedObjects[serverObject.NetworkObjectId];
            Assert.That(clientView.IsOwner && clientView.IsPlayerObject, Is.True);
        }

        [UnityTest]
        public IEnumerator KilledClient_ReconnectingBeforeTheServerNotices_TakesItsSeatBack()
        {
            yield return StartHost();
            yield return Join(_a);
            ulong deadId = _a.LocalClientId;
            Assert.That(_host.TryGetSeat(deadId, out Seat seat), Is.True);
            GameObject playerObject = _host.GetSeatPlayerObject(seat.Index);
            Assert.That(playerObject, Is.Not.Null);

            // Ann's process "dies" without the server noticing (the old connection is still up), and her restarted
            // game connects again with the same stored player token: here, the second client manager.
            _b.Identity = _a.Identity;
            yield return Join(_b);
            ulong newId = _b.LocalClientId;

            yield return WaitForConditionOrTimeOut(() => _seats.Reclaimed.Count == 1 && _a.State == SessionState.Disconnected);
            AssertOnTimeout("the new connection never took the seat over (old " + _a.State + ")");
            Assert.That(_seats.Reclaimed[0].Index, Is.EqualTo(seat.Index), "same seat");
            Assert.That(_seats.Reclaimed[0].ClientId, Is.EqualTo(newId));
            Assert.That(_host.GetSeatPlayerObject(seat.Index), Is.SameAs(playerObject), "same player object");
            Assert.That(playerObject.GetComponent<NetworkObject>().OwnerClientId, Is.EqualTo(newId));
            Assert.That(_host.Seats.Count, Is.EqualTo(2), "host plus Ann, once");

            // The stale connection is told why, and its end does not touch the seat.
            Assert.That(_a.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Kicked), _a.LastDisconnect.ToString());
            Assert.That(_a.LastDisconnect.Message, Is.EqualTo(NetSession.ReplacedMessage));
            yield return WaitForConditionOrTimeOut(() => _seats.Disconnects.Any(d => d.ClientId == deadId));
            AssertOnTimeout("the host never reported the stale connection");
            PlayerDisconnect stale = _seats.Disconnects.Single(d => d.ClientId == deadId);
            Assert.That(stale.Why.Reason, Is.EqualTo(DisconnectReason.Kicked));
            Assert.That(stale.Seat, Is.Null, "the seat had already moved to the new connection");
            Assert.That(_seats.Held, Is.Empty);
            Assert.That(_host.TryGetSeat(newId, out Seat now), Is.True);
            Assert.That(now.Status, Is.EqualTo(SeatStatus.Occupied));
        }

        [UnityTest]
        public IEnumerator HeldSeat_ExpiresAfterGrace_FreeingSeatAndPlayerObject()
        {
            yield return StartHost(grace: 0.5);
            yield return Join(_a);
            Assert.That(_host.TryGetSeat(_a.LocalClientId, out Seat seat), Is.True);
            GameObject playerObject = _host.GetSeatPlayerObject(seat.Index);

            _a.Leave();
            var timeout = new TimeoutHelper(5f);
            yield return WaitForConditionOrTimeOut(() => _seats.Released.Count == 1, timeout);
            AssertOnTimeout("the held seat never expired", timeout);
            Assert.That(_seats.Held.Single().Index, Is.EqualTo(seat.Index), "held first");
            Assert.That(_seats.Released.Single().Index, Is.EqualTo(seat.Index));
            Assert.That(_host.Seats.Count, Is.EqualTo(1));
            yield return null;
            Assert.That(playerObject == null, Is.True, "the expired seat's player object is destroyed");

            yield return WaitForConditionOrTimeOut(() => _a.CanStart);
            yield return Join(_a);
            yield return WaitForConditionOrTimeOut(() => _seats.Taken.Count == 3);
            AssertOnTimeout("the returning player was not seated as new");
            Assert.That(_seats.Reclaimed, Is.Empty, "too late to reclaim");
            Assert.That(_host.GetSeatPlayerObject(seat.Index), Is.Not.Null.And.Not.SameAs(playerObject), "a fresh player object");
        }

        [UnityTest]
        public IEnumerator Kick_FreesTheSeat_AndTheClientIsToldWhy()
        {
            yield return StartHost();
            yield return Join(_a);
            ulong id = _a.LocalClientId;

            Assert.That(_host.Kick(id, "Be nice."), Is.True);

            yield return WaitForConditionOrTimeOut(() => _a.State == SessionState.Disconnected);
            AssertOnTimeout("the kicked client never left");
            Assert.That(_a.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Kicked));
            Assert.That(_a.LastDisconnect.Message, Is.EqualTo("Be nice."));
            Assert.That(_seats.Released.Single().PlayerToken, Is.EqualTo("player-a"));
            Assert.That(_host.Seats.Count, Is.EqualTo(1), "no grace for a kick");
            yield return WaitForConditionOrTimeOut(() => _seats.Disconnects.Count == 1);
            AssertOnTimeout("the host never reported the kick");
            Assert.That(_seats.Disconnects[0].Why.Reason, Is.EqualTo(DisconnectReason.Kicked));
            Assert.That(_host.Kick(id), Is.False, "already gone");
        }

        /// <summary>Records the host's seat and disconnect events.</summary>
        private sealed class SeatProbe
        {
            public readonly List<Seat> Taken = new List<Seat>();
            public readonly List<Seat> Reclaimed = new List<Seat>();
            public readonly List<Seat> Held = new List<Seat>();
            public readonly List<Seat> Released = new List<Seat>();
            public readonly List<PlayerDisconnect> Disconnects = new List<PlayerDisconnect>();
            public readonly List<ConnectionRejection> Rejections = new List<ConnectionRejection>();

            public SeatProbe(NetSession session)
            {
                session.SeatTaken += Taken.Add;
                session.SeatReclaimed += Reclaimed.Add;
                session.SeatHeld += Held.Add;
                session.SeatReleased += Released.Add;
                session.PlayerDisconnected += Disconnects.Add;
                session.ConnectionRejected += Rejections.Add;
            }
        }
    }
}
