using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// Who is in the game, on every peer (UM-T55): one <see cref="NetPlayer"/> per seat with the player's name, colour,
    /// seat, round-trip time, whether they host and whether they are this peer's own player. The server fills it from
    /// the connection approval payload (the name and colour a player joined with) and keeps every client's copy in step,
    /// late joiners and reconnecting players included; a player who drops keeps their record (shown as not connected)
    /// for as long as their seat is held, and gets it back, same seat, when they return.
    /// </summary>
    /// <remarks>
    /// <para>Get it from <see cref="NetSession.Roster"/> (or <see cref="Current"/>). Events are raised on Unity's main
    /// thread; a handler that throws is logged and does not stop the others.</para>
    /// <para>A player changes their own name or colour at runtime with <see cref="RequestChange"/>; the server checks it
    /// (<see cref="PlayerChangeRequest.Validate"/>: <see cref="PlayerNames"/>, <see cref="PlayerColors"/>, and a rate
    /// limit) and answers. An accepted change reaches every peer as <see cref="Changed"/> and is kept with the seat (and
    /// in <see cref="NetSession.Identity"/>), so a reconnect keeps it too.</para>
    /// <para>The roster travels in named messages beside the game's own traffic; no prefab or <c>NetworkObject</c> is
    /// involved, so it works in any scene the session runs.</para>
    /// </remarks>
    public sealed class NetRoster
    {
        /// <summary>Server to client: a roster update (<see cref="RosterWire.EncodeUpdate"/>).</summary>
        internal const string UpdateMessage = "MindAttic.Multiplayer.Roster";

        /// <summary>Client to server: "send me the roster" (a new or returning connection, once it is in).</summary>
        internal const string HelloMessage = "MindAttic.Multiplayer.RosterHello";

        /// <summary>Client to server: a change request (<see cref="RosterWire.EncodeChangeRequest"/>).</summary>
        internal const string ChangeMessage = "MindAttic.Multiplayer.RosterChange";

        /// <summary>Server to client: the answer to a change request.</summary>
        internal const string ReplyMessage = "MindAttic.Multiplayer.RosterReply";

        /// <summary>Server to client and back: a round-trip time probe carrying the server's clock.</summary>
        internal const string PingMessage = "MindAttic.Multiplayer.RosterPing";

        /// <summary>How often the server measures every client's round trip, in seconds.</summary>
        public const double PingIntervalSeconds = 1.0;

        /// <summary>A measured round trip changes a player's record only when it moved by at least this much (milliseconds).</summary>
        public const int PingChangeThresholdMs = 5;

        /// <summary>How many name or colour changes a player may make in a burst, and per second after it.</summary>
        public const int ChangeBurst = 5;

        private const string ChangeRpcName = "MindAttic.Multiplayer.RosterChange";
        private const double ChangesPerSecond = 1.0;

        private readonly NetSession _session;
        private readonly PlayerRoster _roster = new PlayerRoster();
        private readonly HashSet<ulong> _ready = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> _pings = new Dictionary<ulong, int>();
        private readonly Dictionary<uint, Pending> _pending = new Dictionary<uint, Pending>();
        private readonly RpcRateLimiter _limiter = new RpcRateLimiter();
        private uint _nextRequest;
        private readonly Dictionary<ulong, double> _probeAt = new Dictionary<ulong, double>();

        internal NetRoster(NetSession session)
        {
            _session = session;
            _roster.Joined += player => Raise(Joined, player);
            _roster.Left += player => Raise(Left, player);
            _roster.Changed += change => Raise(Changed, change);
        }

        /// <summary>The roster of <see cref="NetBootstrap.Instance"/>'s session; null when there is none.</summary>
        public static NetRoster Current
        {
            get
            {
                NetBootstrap bootstrap = NetBootstrap.Instance;
                return bootstrap != null && bootstrap.Session != null ? bootstrap.Session.Roster : null;
            }
        }

        /// <summary>A player took a seat, or this peer learned of one already seated (late join, reconnect).</summary>
        public event Action<NetPlayer> Joined;

        /// <summary>A player's seat was freed (they left, were removed, their reconnect window ran out), or this peer's session ended.</summary>
        public event Action<NetPlayer> Left;

        /// <summary>Something about a player changed: name, colour, dropped or back (<see cref="NetPlayer.IsConnected"/>), round trip.</summary>
        public event Action<NetPlayerChange> Changed;

        /// <summary>Every player, by seat. Empty before the session is in and after it ends.</summary>
        public IReadOnlyList<NetPlayer> All
        {
            get { return _roster.Players; }
        }

        /// <summary>How many players there are (held seats included).</summary>
        public int Count
        {
            get { return _roster.Count; }
        }

        /// <summary>This peer's own player; null on a dedicated server and before the roster arrives.</summary>
        public NetPlayer Local
        {
            get { return _roster.Local; }
        }

        /// <summary>The player on connection <paramref name="clientId"/> (for example a <c>NetworkObject</c>'s owner, or a <see cref="NetSender"/>).</summary>
        public bool TryGet(ulong clientId, out NetPlayer player)
        {
            return _roster.TryGet(clientId, out player);
        }

        /// <summary>The player in seat <paramref name="seat"/> (stable across a reconnect, unlike the client id).</summary>
        public bool TryGetBySeat(int seat, out NetPlayer player)
        {
            return _roster.TryGetBySeat(seat, out player);
        }

        /// <summary>
        /// Asks the server to change this player's name and/or colour (null keeps that part). The server checks the request
        /// (<see cref="PlayerChangeRequest.Validate"/> and a rate limit of <see cref="ChangeBurst"/> at once, then one a
        /// second) and answers; an accepted change reaches every peer as <see cref="Changed"/>.
        /// </summary>
        /// <returns>The server's answer. Refused at once when this peer has no player in a live session.</returns>
        public Task<PlayerChangeResult> RequestChange(string displayName = null, PlayerColor? color = null)
        {
            var request = new PlayerChangeRequest(displayName, color);
            if (!_session.IsLive || !_session.IsClient)
            {
                return Task.FromResult(PlayerChangeResult.Refuse("You are not in a game as a player."));
            }

            if (_session.IsServer)
            {
                return Task.FromResult(Apply(_session.LocalClientId, request));
            }

            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (messaging == null)
            {
                return Task.FromResult(PlayerChangeResult.Refuse("The connection is not ready."));
            }

            uint id = ++_nextRequest;
            var pending = new Pending(request);
            _pending[id] = pending;
            if (!Send(messaging, ChangeMessage, NetworkManager.ServerClientId, RosterWire.EncodeChangeRequest(id, request)))
            {
                _pending.Remove(id);
                return Task.FromResult(PlayerChangeResult.Refuse("The request could not be sent."));
            }

            return pending.Completion.Task;
        }

        // ---- Session hooks (NetSession calls these) ------------------------------------------------------------------

        /// <summary>
        /// A session started: register the roster's messages on the new messaging manager (NGO makes one per start). A host's
        /// own player may already be in the roster by now (NGO starts a host synchronously), so nothing is cleared here.
        /// </summary>
        internal void Attach(Core.Topology topology)
        {
            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (messaging == null)
            {
                return;
            }

            if (topology == Core.Topology.Client)
            {
                messaging.RegisterNamedMessageHandler(UpdateMessage, OnUpdate);
                messaging.RegisterNamedMessageHandler(ReplyMessage, OnReply);
                messaging.RegisterNamedMessageHandler(PingMessage, OnPingFromServer);
            }
            else
            {
                messaging.RegisterNamedMessageHandler(HelloMessage, OnHello);
                messaging.RegisterNamedMessageHandler(ChangeMessage, OnChangeRequest);
                messaging.RegisterNamedMessageHandler(PingMessage, OnPingBack);
            }
        }

        /// <summary>Client: this connection is in; ask the server for the roster.</summary>
        internal void OnLocalClientConnected()
        {
            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (messaging != null)
            {
                Send(messaging, HelloMessage, NetworkManager.ServerClientId, new byte[] { RosterWire.Version });
            }
        }

        /// <summary>Server: a connection ended; stop sending it updates.</summary>
        internal void OnClientGone(ulong clientId)
        {
            _ready.Remove(clientId);
            _probeAt.Remove(clientId);
            _pings.Remove(clientId);
            _limiter.Forget(clientId);
        }

        /// <summary>Server: rebuild the roster from the seats and send what changed to every client that has it.</summary>
        internal void Refresh()
        {
            if (!_session.IsActive || !_session.Manager.IsServer || !_session.Manager.IsListening || _session.State == SessionState.Stopping)
            {
                return;
            }

            bool listen = _session.Topology == Core.Topology.Host;
            ulong? local = listen ? _session.LocalClientId : (ulong?)null;
            var players = new List<NetPlayer>();
            foreach (Seat seat in _session.Seats)
            {
                int ping;
                players.Add(NetPlayer.FromSeat(seat, listen, _pings.TryGetValue(seat.ClientId, out ping) ? ping : NetPlayer.UnknownPing, local));
            }

            RosterUpdate update = _roster.Replace(players);
            if (!update.IsEmpty && _ready.Count > 0)
            {
                Broadcast(RosterWire.EncodeUpdate(update));
            }
        }

        /// <summary>
        /// Server: every tick; probes each client's round trip once per <see cref="PingIntervalSeconds"/>, the first time one
        /// interval after it got the roster (a client that only just connected is left alone while it settles).
        /// </summary>
        internal void Tick(double now)
        {
            if (!_session.IsServer || _probeAt.Count == 0)
            {
                return;
            }

            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (messaging == null)
            {
                return;
            }

            List<ulong> due = null;
            foreach (KeyValuePair<ulong, double> entry in _probeAt)
            {
                if (entry.Value <= now)
                {
                    (due ?? (due = new List<ulong>())).Add(entry.Key);
                }
            }

            if (due == null)
            {
                return;
            }

            byte[] stamp = BitConverter.GetBytes(now);
            foreach (ulong clientId in due)
            {
                _probeAt[clientId] = now + PingIntervalSeconds;
                if (_session.Manager.ConnectedClients.ContainsKey(clientId))
                {
                    Send(messaging, PingMessage, clientId, stamp, NetworkDelivery.Unreliable);
                }
            }
        }

        /// <summary>The session ended (or a new one is starting): empty the roster (raising Left) and refuse what is pending.</summary>
        internal void Reset()
        {
            _ready.Clear();
            _pings.Clear();
            _limiter.Clear();
            _probeAt.Clear();
            if (_pending.Count > 0)
            {
                var pending = new List<Pending>(_pending.Values);
                _pending.Clear();
                foreach (Pending p in pending)
                {
                    p.Completion.TrySetResult(PlayerChangeResult.Refuse("The game ended before the server answered."));
                }
            }

            _roster.Clear();
        }

        // ---- Server side ------------------------------------------------------------------------------------------------

        private void OnHello(ulong senderClientId, FastBufferReader reader)
        {
            if (!_session.IsServer || senderClientId == NetworkManager.ServerClientId || !_session.TryGetSeat(senderClientId, out _))
            {
                return;
            }

            Refresh();
            _ready.Add(senderClientId);
            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (messaging != null)
            {
                Send(messaging, UpdateMessage, senderClientId, RosterWire.EncodeUpdate(RosterUpdate.Snapshot(_roster.Players)));
            }

            _probeAt[senderClientId] = Time.realtimeSinceStartupAsDouble + PingIntervalSeconds;
        }

        private void OnChangeRequest(ulong senderClientId, FastBufferReader reader)
        {
            if (!_session.IsServer)
            {
                return;
            }

            byte[] bytes = ReadBytes(reader);
            uint id;
            PlayerChangeRequest request;
            if (bytes == null || !RosterWire.TryDecodeChangeRequest(bytes, out id, out request))
            {
                Debug.LogWarning("[MindAttic.Multiplayer] Client " + senderClientId + " sent an unreadable roster change; ignored.");
                return;
            }

            PlayerChangeResult result = Apply(senderClientId, request);
            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (messaging != null)
            {
                Send(messaging, ReplyMessage, senderClientId, RosterWire.EncodeChangeReply(id, result.Accepted, result.Reason));
            }
        }

        /// <summary>Server: checks and applies a change for the player on <paramref name="clientId"/>.</summary>
        private PlayerChangeResult Apply(ulong clientId, PlayerChangeRequest request)
        {
            if (!_limiter.TryAcquire(clientId, ChangeRpcName, ChangesPerSecond, ChangeBurst, Time.realtimeSinceStartupAsDouble))
            {
                return Refused(clientId, request, "Too many changes at once; wait a moment and try again.");
            }

            string problem = request.Validate();
            if (problem != null)
            {
                return Refused(clientId, request, problem);
            }

            Seat seat;
            if (!_session.TryGetSeat(clientId, out seat))
            {
                return Refused(clientId, request, "You have no seat in this game.");
            }

            string name = request.DisplayName ?? seat.DisplayName;
            PlayerColor color = request.Color ?? seat.Color;
            _session.RenameSeat(clientId, name, color);
            if (clientId == _session.LocalClientId)
            {
                _session.Identity = _session.Identity.With(name, color);
            }

            Refresh();
            NetPlayer player;
            _roster.TryGet(clientId, out player);
            Debug.Log("[MindAttic.Multiplayer] Client " + clientId + " is now " + player + ".");
            return PlayerChangeResult.Accept(player);
        }

        private static PlayerChangeResult Refused(ulong clientId, PlayerChangeRequest request, string reason)
        {
            Debug.Log("[MindAttic.Multiplayer] Refused client " + clientId + "'s " + request + ": " + reason);
            return PlayerChangeResult.Refuse(reason);
        }

        private void OnPingBack(ulong senderClientId, FastBufferReader reader)
        {
            byte[] bytes = ReadBytes(reader);
            if (!_session.IsServer || bytes == null || bytes.Length != sizeof(double) || !_ready.Contains(senderClientId))
            {
                return;
            }

            double sent = BitConverter.ToDouble(bytes, 0);
            double rtt = (Time.realtimeSinceStartupAsDouble - sent) * 1000.0;
            if (double.IsNaN(rtt) || rtt < 0 || rtt > 60000)
            {
                return;
            }

            int ms = (int)Math.Round(rtt);
            int old;
            if (_pings.TryGetValue(senderClientId, out old) && Math.Abs(old - ms) < PingChangeThresholdMs)
            {
                return;
            }

            _pings[senderClientId] = ms;
            Refresh();
        }

        // ---- Client side ------------------------------------------------------------------------------------------------

        private void OnUpdate(ulong senderClientId, FastBufferReader reader)
        {
            if (_session.IsServer || senderClientId != NetworkManager.ServerClientId || !_session.IsActive)
            {
                return;
            }

            byte[] bytes = ReadBytes(reader);
            RosterUpdate update;
            if (bytes == null || !RosterWire.TryDecodeUpdate(bytes, out update))
            {
                Debug.LogWarning("[MindAttic.Multiplayer] The server sent an unreadable roster update; ignored.");
                return;
            }

            ulong me = _session.LocalClientId;
            var players = new List<NetPlayer>(update.Players.Count);
            foreach (NetPlayer player in update.Players)
            {
                players.Add(player.WithLocal(player.IsConnected && player.ClientId == me));
            }

            _roster.Apply(new RosterUpdate(update.Full, players, update.Removed));
        }

        private void OnReply(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            byte[] bytes = ReadBytes(reader);
            uint id;
            bool accepted;
            string reason;
            Pending pending;
            if (bytes == null || !RosterWire.TryDecodeChangeReply(bytes, out id, out accepted, out reason) || !_pending.TryGetValue(id, out pending))
            {
                return;
            }

            _pending.Remove(id);
            if (!accepted)
            {
                pending.Completion.TrySetResult(PlayerChangeResult.Refuse(reason));
                return;
            }

            // Kept for a reconnect: the approval payload carries the new name and colour from now on.
            PlayerIdentity identity = _session.Identity;
            _session.Identity = identity.With(pending.Request.DisplayName ?? identity.DisplayName, pending.Request.Color ?? identity.Color);
            pending.Completion.TrySetResult(PlayerChangeResult.Accept(_roster.Local));
        }

        private void OnPingFromServer(ulong senderClientId, FastBufferReader reader)
        {
            byte[] bytes = ReadBytes(reader);
            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (senderClientId == NetworkManager.ServerClientId && bytes != null && messaging != null)
            {
                Send(messaging, PingMessage, NetworkManager.ServerClientId, bytes, NetworkDelivery.Unreliable);
            }
        }

        // ---- Plumbing -----------------------------------------------------------------------------------------------------

        private void Broadcast(byte[] bytes, string message = UpdateMessage, NetworkDelivery delivery = NetworkDelivery.ReliableFragmentedSequenced)
        {
            CustomMessagingManager messaging = _session.Manager.CustomMessagingManager;
            if (messaging == null)
            {
                return;
            }

            var gone = new List<ulong>();
            foreach (ulong clientId in _ready)
            {
                if (!_session.Manager.ConnectedClients.ContainsKey(clientId) || !Send(messaging, message, clientId, bytes, delivery))
                {
                    gone.Add(clientId);
                }
            }

            foreach (ulong clientId in gone)
            {
                _ready.Remove(clientId);
            }
        }

        /// <summary>
        /// Sends <paramref name="bytes"/> as a named message. Roster updates and replies share one reliable, ordered channel,
        /// so a reply never overtakes the update it follows.
        /// </summary>
        private static bool Send(CustomMessagingManager messaging, string message, ulong clientId, byte[] bytes,
            NetworkDelivery delivery = NetworkDelivery.ReliableFragmentedSequenced)
        {
            try
            {
                using (var writer = new FastBufferWriter(bytes.Length + 16, Allocator.Temp))
                {
                    writer.WriteValueSafe(bytes);
                    messaging.SendNamedMessage(message, clientId, writer, delivery);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private static byte[] ReadBytes(FastBufferReader reader)
        {
            try
            {
                byte[] bytes;
                reader.ReadValueSafe(out bytes);
                return bytes;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null)
            {
                return;
            }

            foreach (Delegate each in handler.GetInvocationList())
            {
                try
                {
                    ((Action<T>)each)(value);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private sealed class Pending
        {
            public Pending(PlayerChangeRequest request)
            {
                Request = request;
                Completion = new TaskCompletionSource<PlayerChangeResult>();
            }

            public PlayerChangeRequest Request { get; }

            public TaskCompletionSource<PlayerChangeResult> Completion { get; }
        }
    }
}
