using System;
using System.Collections.Generic;
using MindAttic.Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MindAttic.Unity.Multiplayer
{
    /// <summary>
    /// One multiplayer session: host it, serve it, join it, leave it. The facade game code uses instead of
    /// NGO's <c>NetworkManager</c> (BIBLE UM-LAW-2); it drives a NetworkManager and its Unity Transport and
    /// keeps the engine-free <see cref="SessionStateMachine"/> in step with what the transport reports.
    /// </summary>
    /// <remarks>
    /// <para>Get one from <see cref="NetBootstrap.Session"/>. Host and Server go
    /// <c>Idle → Starting → Running → Stopping → Idle</c>; a Client goes
    /// <c>Idle → Connecting → Connected → Disconnected</c> and can join again from there.</para>
    /// <para>Every start takes a <see cref="NetConfig"/>; the session copies it, so changing the object
    /// afterwards does nothing. The config's transport (<see cref="TransportKind.Udp"/> or
    /// <see cref="TransportKind.WebSocket"/>), address, port and tick rate are applied to the NetworkManager
    /// and its Unity Transport before NGO starts.</para>
    /// <para>Connection approval is always on. A Client (and a Host's own player) sends a
    /// <see cref="ConnectionPayload"/> built from <see cref="Identity"/>, the config's game version and
    /// <see cref="ProtocolHash"/>. A Host or Server admits it through <see cref="ConnectionAdmission"/>: a version or
    /// protocol mismatch, a ban, a bad payload or a full server is refused with a reason the client shows
    /// (<see cref="Disconnection.Rejection"/>), and an approved player gets a <see cref="Seat"/>. A player who drops
    /// keeps its seat and its player object for <see cref="NetConfig.ReconnectGraceSeconds"/>; reconnecting with the
    /// same player token within that window gives both back (<see cref="SeatReclaimed"/>).</para>
    /// <para>Events are raised on Unity's main thread, from NGO's callbacks. An exception thrown by a handler
    /// is logged and does not stop the session's own bookkeeping.</para>
    /// </remarks>
    public sealed class NetSession : IDisposable
    {
        /// <summary>Prefix NGO puts on a disconnect reason it generated from a transport event (as opposed to a reason the server sent).</summary>
        private const string TransportReasonPrefix = "[Disconnect Event]";

        /// <summary>The reasons NGO 2.x sends every client when the server shuts down (NetworkManager: "Disconnected due to {host|server} shutting down.").</summary>
        private static readonly string[] ServerShutdownReasons =
        {
            "Disconnected due to host shutting down.",
            "Disconnected due to server shutting down.",
        };

        private static readonly ulong[] NoClients = new ulong[0];

        private static readonly Seat[] NoSeats = new Seat[0];

        /// <summary>What a replaced connection is told when the same player connects again from elsewhere.</summary>
        internal const string ReplacedMessage = "You connected again from another session, which took over your seat.";

        private readonly NetworkManager _manager;
        private readonly SessionStateMachine _machine = new SessionStateMachine();
        private NetConfig _config;
        private NetworkTickClock _clock;
        private bool _transportFailed;
        private bool _disposed;
        private PlayerIdentity _identity = PlayerIdentity.CreateNew();
        private ProtocolHash? _protocolHash;
        private ConnectionAdmission _admission;
        private readonly Dictionary<int, NetworkObject> _seatObjects = new Dictionary<int, NetworkObject>();
        private readonly Dictionary<ulong, int> _reclaimOnConnect = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, Disconnection> _dropReasons = new Dictionary<ulong, Disconnection>();
        private readonly HashSet<ulong> _rejected = new HashSet<ulong>();

        /// <summary>Wraps <paramref name="manager"/>. The session owns its start, stop and transport settings from now on.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="manager"/> is null.</exception>
        internal NetSession(NetworkManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            _manager = manager;
            _machine.StateChanged += OnMachineStateChanged;
            _manager.OnServerStarted += OnServerStarted;
            _manager.OnServerStopped += OnServerStopped;
            _manager.OnClientStopped += OnClientStopped;
            _manager.OnClientConnectedCallback += OnClientConnected;
            _manager.OnClientDisconnectCallback += OnClientDisconnected;
            _manager.OnTransportFailure += OnTransportFailure;
        }

        /// <summary>Raised after every state change.</summary>
        public event Action<SessionStateChange> StateChanged;

        /// <summary>Raised when the session is fully up: a Host or Server is Running, a Client is Connected. <see cref="Clock"/> is ticking by then.</summary>
        public event Action Started;

        /// <summary>Raised when the session is over (Host/Server back to Idle, Client Disconnected), with why.</summary>
        public event Action<Disconnection> Ended;

        /// <summary>Server side (Host or Server): a client finished connecting. On a Host this includes the host's own local client.</summary>
        public event Action<ulong> ClientConnected;

        /// <summary>Server side (Host or Server): a remote client left or was dropped.</summary>
        public event Action<ulong> ClientDisconnected;

        /// <summary>Server side: a seated player's connection ended, with why and what became of the seat.</summary>
        public event Action<PlayerDisconnect> PlayerDisconnected;

        /// <summary>Server side: connection approval refused a client (it is told the same reason).</summary>
        public event Action<ConnectionRejection> ConnectionRejected;

        /// <summary>Server side: a new player was given a seat (raised when its connection completes).</summary>
        public event Action<Seat> SeatTaken;

        /// <summary>
        /// Server side: a player came back to its seat within the grace window (or replaced a connection of its own
        /// the server had not yet seen die). Its held player object, if any, is its player object again.
        /// </summary>
        public event Action<Seat> SeatReclaimed;

        /// <summary>Server side: a player dropped and its seat (and player object) is held until <see cref="Seat.HeldUntil"/>.</summary>
        public event Action<Seat> SeatHeld;

        /// <summary>Server side: a seat was freed (grace window over, or kicked); its held player object has been despawned.</summary>
        public event Action<Seat> SeatReleased;

        /// <summary>Where the session is in its life.</summary>
        public SessionState State
        {
            get { return _machine.State; }
        }

        /// <summary>The topology of the current or last session; null before the first start.</summary>
        public Core.Topology? Topology
        {
            get { return _machine.Topology; }
        }

        /// <summary>Why the last session ended; <see cref="Disconnection.None"/> while one is live or before any has ended.</summary>
        public Disconnection LastDisconnect
        {
            get { return _machine.LastDisconnect; }
        }

        /// <summary>True from a start until the session has ended.</summary>
        public bool IsActive
        {
            get { return _machine.IsActive; }
        }

        /// <summary>True while the session is fully up (Running or Connected).</summary>
        public bool IsLive
        {
            get { return _machine.IsLive; }
        }

        /// <summary>True when this process runs the authoritative server of a live session (Host or Server).</summary>
        public bool IsServer
        {
            get { return _machine.IsLive && _manager.IsServer; }
        }

        /// <summary>True when this process is a Host (server plus local player) of a live session.</summary>
        public bool IsHost
        {
            get { return _machine.IsLive && _manager.IsHost; }
        }

        /// <summary>True when this process has a connected local player (Host or Client) in a live session.</summary>
        public bool IsClient
        {
            get { return _machine.IsLive && _manager.IsClient; }
        }

        /// <summary>
        /// True when a new session can start now: none is active and NGO has finished shutting down the last
        /// one (a shutdown completes at the end of the frame it was asked for).
        /// </summary>
        public bool CanStart
        {
            get { return !_disposed && !_machine.IsActive && !_manager.IsListening && !_manager.ShutdownInProgress; }
        }

        /// <summary>This process's client id in the session (the server's is 0).</summary>
        public ulong LocalClientId
        {
            get { return _manager.LocalClientId; }
        }

        /// <summary>
        /// Server side: the ids of every connected client (on a Host, the host's own client too). Empty on a
        /// client and when no session is running.
        /// </summary>
        public IReadOnlyList<ulong> ConnectedClientIds
        {
            get { return _manager.IsServer && _manager.IsListening ? _manager.ConnectedClientsIds : NoClients; }
        }

        /// <summary>The fixed-rate clock of the live session; null when no session is live.</summary>
        public ITickClock Clock
        {
            get { return _clock; }
        }

        /// <summary>
        /// Who this process plays as: the player token a server seats it by, and its name and colour. Defaults to
        /// a fresh random identity; set a stored one (see <see cref="NetBootstrap"/>) so a restarted game gets its
        /// seat back. Read when a Client or Host starts.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public PlayerIdentity Identity
        {
            get { return _identity; }
            set { _identity = value ?? throw new ArgumentNullException(nameof(value)); }
        }

        /// <summary>
        /// This build's network protocol hash, sent by clients and required by servers. Defaults to the hash the
        /// generator emits into the loaded assemblies (<see cref="Core.ProtocolHash.FromAssemblies(System.Reflection.Assembly[])"/>);
        /// set it to override. Read when a session starts.
        /// </summary>
        public ProtocolHash ProtocolHash
        {
            get
            {
                if (!_protocolHash.HasValue)
                {
                    _protocolHash = Core.ProtocolHash.FromAssemblies(AppDomain.CurrentDomain.GetAssemblies());
                }

                return _protocolHash.Value;
            }

            set { _protocolHash = value; }
        }

        /// <summary>Server side: the bans connection approval enforces; null for none. Read when a Host or Server starts.</summary>
        public IBanList BanList { get; set; }

        /// <summary>Server side: every taken seat (occupied or held), by index. Empty on a client and when no server is running.</summary>
        public IReadOnlyList<Seat> Seats
        {
            get { return _admission != null ? _admission.Seats.Seats : NoSeats; }
        }

        /// <summary>Server side: the seat of a connected client.</summary>
        public bool TryGetSeat(ulong clientId, out Seat seat)
        {
            seat = null;
            return _admission != null && _admission.Seats.TryGetByClient(clientId, out seat);
        }

        /// <summary>
        /// Server side: the player object kept with a seat (spawned for the seat's first connection and handed back
        /// on a reconnect), or null when the seat has none.
        /// </summary>
        public GameObject GetSeatPlayerObject(int seatIndex)
        {
            NetworkObject playerObject;
            return _seatObjects.TryGetValue(seatIndex, out playerObject) && playerObject != null ? playerObject.gameObject : null;
        }

        /// <summary>
        /// Server side: removes a client now, frees its seat with no grace window (its player object is despawned)
        /// and tells it <paramref name="reason"/>; it ends with <see cref="DisconnectReason.Kicked"/>.
        /// </summary>
        /// <returns>False when this is not a running server or the client is not connected (or is the host itself).</returns>
        public bool Kick(ulong clientId, string reason = null)
        {
            if (!IsServer || clientId == NetworkManager.ServerClientId || !_manager.ConnectedClients.ContainsKey(clientId))
            {
                return false;
            }

            string message = string.IsNullOrWhiteSpace(reason) ? "You were removed from the server." : reason;
            Seat freed = _admission != null ? _admission.Seats.Free(clientId) : null;
            if (freed != null)
            {
                ReleaseSeatObject(freed);
            }

            _dropReasons[clientId] = new Disconnection(DisconnectReason.Kicked, message);
            _manager.DisconnectClient(clientId, message);
            if (freed != null)
            {
                Raise(SeatReleased, freed);
            }

            return true;
        }

        /// <summary>A copy of the config the current or last session started with; null before the first start.</summary>
        public NetConfig Config
        {
            get { return _config == null ? null : _config.Clone(); }
        }

        /// <summary>Hosts a listen server: this process serves and plays. <paramref name="config"/>'s topology is ignored.</summary>
        /// <returns>True when NGO started; the session is then Starting or already Running.</returns>
        /// <exception cref="ArgumentException">The config is invalid (see <see cref="NetConfig.Validate"/>).</exception>
        /// <exception cref="InvalidOperationException">A session is already active.</exception>
        public bool StartHost(NetConfig config = null)
        {
            return Start(WithTopology(config, Core.Topology.Host));
        }

        /// <summary>Runs a dedicated server: no local player. <paramref name="config"/>'s topology is ignored.</summary>
        /// <returns>True when NGO started; the session is then Starting or already Running.</returns>
        /// <exception cref="ArgumentException">The config is invalid (see <see cref="NetConfig.Validate"/>).</exception>
        /// <exception cref="InvalidOperationException">A session is already active.</exception>
        public bool StartServer(NetConfig config = null)
        {
            return Start(WithTopology(config, Core.Topology.Server));
        }

        /// <summary>
        /// Joins the host or server at <paramref name="address"/>:<paramref name="port"/>. The rest of the
        /// settings (transport, tick rate, game version) come from <paramref name="config"/> when given.
        /// </summary>
        /// <returns>True when NGO began connecting; the session is then Connecting until <see cref="Started"/> or <see cref="Ended"/>.</returns>
        /// <exception cref="ArgumentException">The address, port or config is invalid.</exception>
        /// <exception cref="InvalidOperationException">A session is already active.</exception>
        public bool JoinByAddress(string address, ushort port, NetConfig config = null)
        {
            NetConfig join = WithTopology(config, Core.Topology.Client);
            join.Address = address;
            join.Port = port;
            return Start(join);
        }

        /// <summary>Starts whatever <paramref name="config"/>'s topology says: host, serve or join.</summary>
        /// <returns>
        /// True when NGO started. False when NGO refused (the session then ends at once with
        /// <see cref="DisconnectReason.TransportFailure"/> and the reason is logged) or when the last session
        /// is still shutting down (see <see cref="CanStart"/>; nothing changes).
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        /// <exception cref="ArgumentException">The config is invalid (see <see cref="NetConfig.Validate"/>).</exception>
        /// <exception cref="InvalidOperationException">A session is already active.</exception>
        /// <exception cref="ObjectDisposedException">The session was disposed.</exception>
        public bool Start(NetConfig config)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NetSession));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            NetConfig copy = config.Clone();
            copy.EnsureValid();

            if (_machine.IsActive)
            {
                throw new InvalidOperationException(
                    "Cannot start a " + copy.Topology + " session while the session is " + _machine.State + "; call Leave first.");
            }

            if (_manager.IsListening || _manager.ShutdownInProgress)
            {
                Debug.LogWarning("[MindAttic.Multiplayer] The last session is still shutting down; start again once CanStart is true.");
                return false;
            }

            Configure(copy);
            _config = copy;
            _transportFailed = false;
            _machine.Start(copy.Topology);

            bool started;
            try
            {
                switch (copy.Topology)
                {
                    case Core.Topology.Host:
                        started = _manager.StartHost();
                        break;
                    case Core.Topology.Server:
                        started = _manager.StartServer();
                        break;
                    default:
                        started = _manager.StartClient();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                started = false;
            }

            if (!started)
            {
                string message = "NGO could not start a " + copy.Topology + " on " + copy.EffectiveAddress + ":" + copy.Port
                    + " over " + copy.Transport + " (is the port already in use?).";
                Debug.LogWarning("[MindAttic.Multiplayer] " + message);
                if (_machine.IsActive)
                {
                    _machine.NotifyDisconnected(DisconnectReason.TransportFailure, message);
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Leaves the session. A Client is Disconnected at once with <see cref="DisconnectReason.LocalLeave"/>;
        /// a Host or Server goes to Stopping and reaches Idle when NGO has shut down (normally by the next frame).
        /// </summary>
        /// <returns>False when there was nothing to leave.</returns>
        public bool Leave()
        {
            if (!_machine.Stop())
            {
                return false;
            }

            if (_manager.IsListening)
            {
                _manager.Shutdown();
            }
            else
            {
                // NGO is not running (it never got going), so no stop callback will come: finish here.
                _machine.NotifyStopped();
            }

            return true;
        }

        /// <summary>Leaves any active session and stops listening to the NetworkManager. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Leave();

            // NGO finishes the shutdown by itself at the end of the frame, but this session stops listening
            // now, so it closes its own books: a disposed session always ends Idle (or Disconnected).
            _machine.NotifyStopped();
            _disposed = true;
            _manager.OnServerStarted -= OnServerStarted;
            _manager.OnServerStopped -= OnServerStopped;
            _manager.OnClientStopped -= OnClientStopped;
            _manager.OnClientConnectedCallback -= OnClientConnected;
            _manager.OnClientDisconnectCallback -= OnClientDisconnected;
            _manager.OnTransportFailure -= OnTransportFailure;
            if (_manager.ConnectionApprovalCallback == (Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse>)OnApproval)
            {
                _manager.ConnectionApprovalCallback = null;
            }

            StopClock();
            ResetSeats();
        }

        /// <summary>Applies the config to the NetworkManager and its Unity Transport (added if missing).</summary>
        private void Configure(NetConfig config)
        {
            if (_manager.NetworkConfig == null)
            {
                _manager.NetworkConfig = new NetworkConfig();
            }

            _manager.NetworkConfig.TickRate = (uint)config.TickRate;

            // Approval is always on, on both ends (it is part of NGO's config hash, so client and server must agree).
            _manager.NetworkConfig.ConnectionApproval = true;
            ResetSeats();
            if (config.Topology == Core.Topology.Client)
            {
                _manager.ConnectionApprovalCallback = null;
            }
            else
            {
                _admission = new ConnectionAdmission(
                    ConnectionApprovalValidator.FromConfig(config, ProtocolHash, BanList),
                    new SeatRegistry(config.MaxPlayers, config.ReconnectGraceSeconds));
                _manager.ConnectionApprovalCallback = OnApproval;
            }

            _manager.NetworkConfig.ConnectionData = config.Topology == Core.Topology.Server
                ? new byte[0]
                : ConnectionPayloadCodec.Encode(LocalPayload(config));

            UnityTransport transport = _manager.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null)
            {
                transport = _manager.GetComponent<UnityTransport>();
            }

            if (transport == null)
            {
                transport = _manager.gameObject.AddComponent<UnityTransport>();
            }

            _manager.NetworkConfig.NetworkTransport = transport;
            transport.UseWebSockets = config.Transport == TransportKind.WebSocket;

            // forceOverrideCommandLineArgs: the config already folded in the command line (NetCommandLine),
            // so Unity Transport must not apply its own -port / -ip parsing on top.
            if (config.Topology == Core.Topology.Client)
            {
                transport.SetConnectionData(true, config.EffectiveAddress, config.Port);
            }
            else
            {
                transport.SetConnectionData(true, NetConfig.LoopbackAddress, config.Port, config.EffectiveAddress);
            }
        }

        private static NetConfig WithTopology(NetConfig config, Core.Topology topology)
        {
            NetConfig copy = config == null ? new NetConfig() : config.Clone();
            copy.Topology = topology;
            return copy;
        }

        private void OnServerStarted()
        {
            _machine.NotifyStarted();
        }

        private void OnServerStopped(bool wasHost)
        {
            if (_machine.State == SessionState.Stopping)
            {
                _machine.NotifyStopped();
            }
            else if (_machine.IsActive)
            {
                _machine.NotifyDisconnected(
                    _transportFailed ? DisconnectReason.TransportFailure : DisconnectReason.ServerShutdown,
                    _transportFailed ? "The network transport failed." : "The server stopped.");
            }
        }

        private void OnClientStopped(bool wasHost)
        {
            if (wasHost || !_machine.IsActive)
            {
                return; // a host's stop is handled by OnServerStopped; a client that already ended has nothing to do
            }

            // The disconnect callback normally ends the session first with a precise reason; this is the fallback.
            if (_transportFailed)
            {
                _machine.NotifyDisconnected(DisconnectReason.TransportFailure, "The network transport failed.");
            }
            else if (_machine.State == SessionState.Connecting)
            {
                _machine.NotifyDisconnected(DisconnectReason.ConnectFailed, "Could not reach the server.");
            }
            else
            {
                _machine.NotifyDisconnected(DisconnectReason.ServerShutdown, "The connection to the server closed.");
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (_manager.IsServer)
            {
                SeatConnectedClient(clientId);
                Raise(ClientConnected, clientId);
                return;
            }

            if (clientId == _manager.LocalClientId)
            {
                _machine.NotifyConnected();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_manager.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId)
                {
                    OnRemoteClientDisconnected(clientId);
                }

                return;
            }

            if (_machine.IsActive)
            {
                Disconnection why = ClassifyClientDisconnect();
                _machine.NotifyDisconnected(why);
            }
        }

        private void OnTransportFailure()
        {
            _transportFailed = true;
        }

        /// <summary>Turns what NGO and the transport know about a client-side disconnect into a <see cref="DisconnectReason"/>.</summary>
        private Disconnection ClassifyClientDisconnect()
        {
            string reason = _manager.DisconnectReason ?? string.Empty;
            bool serverSentReason = reason.Length > 0 && !reason.StartsWith(TransportReasonPrefix, StringComparison.Ordinal);
            bool connecting = _machine.State == SessionState.Connecting;

            if (serverSentReason && Array.IndexOf(ServerShutdownReasons, reason.Trim()) >= 0)
            {
                return new Disconnection(DisconnectReason.ServerShutdown, reason);
            }

            if (serverSentReason)
            {
                ApprovalRejection rejection;
                string message;
                if (connecting && ApprovalDecision.TryParseWireReason(reason, out rejection, out message))
                {
                    return new Disconnection(DisconnectReason.Rejected, message, rejection);
                }

                return new Disconnection(connecting ? DisconnectReason.Rejected : DisconnectReason.Kicked, reason);
            }

            if (_transportFailed)
            {
                return new Disconnection(DisconnectReason.TransportFailure, reason);
            }

            NetworkTransport transport = _manager.NetworkConfig != null ? _manager.NetworkConfig.NetworkTransport : null;
            NetworkTransport.DisconnectEvents? transportEvent = transport != null ? transport.DisconnectEvent : (NetworkTransport.DisconnectEvents?)null;

            if (connecting)
            {
                return new Disconnection(
                    transportEvent == NetworkTransport.DisconnectEvents.AuthenticationFailure ? DisconnectReason.Rejected : DisconnectReason.ConnectFailed,
                    reason);
            }

            if (transportEvent == NetworkTransport.DisconnectEvents.ProtocolTimeout)
            {
                return new Disconnection(DisconnectReason.TimedOut, reason);
            }

            if (transportEvent == NetworkTransport.DisconnectEvents.ProtocolError)
            {
                return new Disconnection(DisconnectReason.TransportFailure, reason);
            }

            return new Disconnection(DisconnectReason.ServerShutdown, reason);
        }

        private void OnMachineStateChanged(SessionStateChange change)
        {
            bool live = change.To == SessionState.Running || change.To == SessionState.Connected;
            bool ended = change.To == SessionState.Disconnected
                || (change.To == SessionState.Idle && change.From != SessionState.Disconnected);

            if (live)
            {
                StartClock();
            }
            else if (ended || change.To == SessionState.Stopping)
            {
                StopClock();
            }

            if (ended)
            {
                ResetSeats();
            }

            Raise(StateChanged, change);

            if (live)
            {
                Raise(Started);
            }
            else if (ended)
            {
                Raise(Ended, change.Disconnection);
            }
        }

        private void StartClock()
        {
            StopClock();
            if (_manager.NetworkTickSystem != null)
            {
                _clock = new NetworkTickClock(_manager.NetworkTickSystem);
                _clock.Ticked += OnTick;
            }
        }

        private void StopClock()
        {
            if (_clock != null)
            {
                _clock.Ticked -= OnTick;
                _clock.Dispose();
                _clock = null;
            }
        }

        /// <summary>Seconds on the clock seats are held by: Unity's realtime clock, which runs while paused or unfocused.</summary>
        private static double Now
        {
            get { return Time.realtimeSinceStartupAsDouble; }
        }

        /// <summary>The payload this process sends as a player (Client, or a Host's own player).</summary>
        private ConnectionPayload LocalPayload(NetConfig config)
        {
            return _identity.ToPayload(config.GameVersion, ProtocolHash);
        }

        /// <summary>NGO's approval callback (Host or Server): admit or refuse, and seat the admitted.</summary>
        private void OnApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = false;
            ulong clientId = request.ClientNetworkId;
            if (_admission == null)
            {
                response.Approved = false;
                response.Reason = ApprovalDecision.Reject(ApprovalRejection.Full, "The server is not accepting players.").ToWireReason();
                return;
            }

            Admission admission;
            try
            {
                admission = clientId == NetworkManager.ServerClientId && _config != null
                    ? _admission.AdmitLocal(clientId, LocalPayload(_config), Now)
                    : _admission.Admit(clientId, request.Payload, Now);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                response.Approved = false;
                response.Reason = ApprovalDecision.Reject(ApprovalRejection.BadPayload, "The server could not process your connection request.").ToWireReason();
                return;
            }

            ReleaseExpired(admission.Expired);

            if (!admission.Approved)
            {
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Reason = admission.Decision.ToWireReason();
                _rejected.Add(clientId);
                Debug.Log("[MindAttic.Multiplayer] Refused client " + clientId + ": " + admission.Decision);
                Raise(ConnectionRejected, new ConnectionRejection(clientId, admission.Decision));
                return;
            }

            SeatClaim claim = admission.Claim;
            NetworkObject held;
            bool keepsPlayerObject = claim.IsReturn
                && _seatObjects.TryGetValue(claim.Seat.Index, out held) && held != null && held.IsSpawned;

            response.Approved = true;
            response.CreatePlayerObject = !keepsPlayerObject && _manager.NetworkConfig.PlayerPrefab != null;
            _reclaimOnConnect[clientId] = claim.IsReturn ? claim.Seat.Index : -1;

            if (claim.ReplacedClientId.HasValue)
            {
                // The same player is back on a new connection while its old one still looks alive (a killed
                // client the transport has not timed out yet). NGO drops the old one later this frame, before
                // the new one completes, so the player object is free to hand over when it does.
                ulong stale = claim.ReplacedClientId.Value;
                _dropReasons[stale] = new Disconnection(DisconnectReason.Kicked, ReplacedMessage);
                if (_manager.ConnectedClients.ContainsKey(stale))
                {
                    _manager.DisconnectClient(stale, ReplacedMessage);
                }
            }
        }

        /// <summary>Server: a client finished connecting; keep its player object with its seat (or hand the held one back).</summary>
        private void SeatConnectedClient(ulong clientId)
        {
            Seat seat;
            if (_admission == null || !_admission.Seats.TryGetByClient(clientId, out seat))
            {
                return;
            }

            int reclaimIndex;
            bool returning = _reclaimOnConnect.TryGetValue(clientId, out reclaimIndex) && reclaimIndex >= 0;
            _reclaimOnConnect.Remove(clientId);

            NetworkObject playerObject;
            if (returning && _seatObjects.TryGetValue(seat.Index, out playerObject) && playerObject != null && playerObject.IsSpawned)
            {
                HandOver(playerObject, clientId);
            }
            else
            {
                NetworkClient client;
                playerObject = _manager.ConnectedClients.TryGetValue(clientId, out client) ? client.PlayerObject : null;
            }

            if (playerObject != null)
            {
                // Kept on disconnect (NGO hands it to the server) so it can wait for the player with the seat.
                playerObject.DontDestroyWithOwner = true;
                _seatObjects[seat.Index] = playerObject;
            }

            Raise(returning ? SeatReclaimed : SeatTaken, seat);
        }

        /// <summary>Makes a held player object <paramref name="clientId"/>'s player object again: same GameObject, same state.</summary>
        private void HandOver(NetworkObject playerObject, ulong clientId)
        {
            if (playerObject.OwnerClientId != NetworkManager.ServerClientId && playerObject.OwnerClientId != clientId
                && _manager.ConnectedClients.ContainsKey(playerObject.OwnerClientId))
            {
                Debug.LogWarning("[MindAttic.Multiplayer] Seat player object is still owned by connected client "
                    + playerObject.OwnerClientId + "; not handing it to client " + clientId + ".");
                return;
            }

            // NGO has no public "make this the client's player object" for a live object, so it is respawned:
            // the server keeps the same instance (and every component's state); clients get a fresh spawn of it.
            playerObject.Despawn(false);
            playerObject.SpawnAsPlayerObject(clientId);
        }

        /// <summary>Server: a remote client's connection ended; hold or free its seat and report why.</summary>
        private void OnRemoteClientDisconnected(ulong clientId)
        {
            _reclaimOnConnect.Remove(clientId);
            if (_rejected.Remove(clientId))
            {
                return; // never admitted: ConnectionRejected already said why
            }

            Disconnection why;
            if (!_dropReasons.TryGetValue(clientId, out why))
            {
                NetworkTransport transport = _manager.NetworkConfig != null ? _manager.NetworkConfig.NetworkTransport : null;
                why = transport != null && transport.DisconnectEvent == NetworkTransport.DisconnectEvents.ProtocolTimeout
                    ? new Disconnection(DisconnectReason.TimedOut, "The connection stopped responding.")
                    : new Disconnection(DisconnectReason.ClientLeft, "The client closed its connection.");
            }

            _dropReasons.Remove(clientId);

            Seat seat = _admission != null ? _admission.Seats.Release(clientId, Now) : null;
            Raise(ClientDisconnected, clientId);
            Raise(PlayerDisconnected, new PlayerDisconnect(clientId, why, seat));

            if (seat == null)
            {
                return;
            }

            if (seat.Status == SeatStatus.Held)
            {
                Raise(SeatHeld, seat);
            }
            else
            {
                ReleaseSeatObject(seat);
                Raise(SeatReleased, seat);
            }
        }

        private void OnTick(long tick)
        {
            if (_admission != null && _admission.Seats.HeldCount > 0)
            {
                ReleaseExpired(_admission.Seats.Expire(Now));
            }
        }

        private void ReleaseExpired(IReadOnlyList<Seat> expired)
        {
            for (int i = 0; i < expired.Count; i++)
            {
                ReleaseSeatObject(expired[i]);
                Raise(SeatReleased, expired[i]);
            }
        }

        /// <summary>Despawns and destroys the player object kept with a freed seat.</summary>
        private void ReleaseSeatObject(Seat seat)
        {
            NetworkObject playerObject;
            if (!_seatObjects.TryGetValue(seat.Index, out playerObject))
            {
                return;
            }

            _seatObjects.Remove(seat.Index);
            if (playerObject != null && playerObject.IsSpawned && _manager.IsServer && !_manager.ShutdownInProgress)
            {
                playerObject.Despawn(true);
            }
        }

        /// <summary>Forgets every seat (the server stopped, or a new session is starting).</summary>
        private void ResetSeats()
        {
            if (_admission != null)
            {
                _admission.Seats.Clear();
                _admission = null;
            }

            _seatObjects.Clear();
            _reclaimOnConnect.Clear();
            _dropReasons.Clear();
            _rejected.Clear();
        }

        private static void Raise(Action handler)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(value);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
