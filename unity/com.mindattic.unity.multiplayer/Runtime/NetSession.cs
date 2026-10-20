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
    /// and its Unity Transport before NGO starts. Connection approval and the player cap arrive with UM-T9.</para>
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

        private readonly NetworkManager _manager;
        private readonly SessionStateMachine _machine = new SessionStateMachine();
        private NetConfig _config;
        private NetworkTickClock _clock;
        private bool _transportFailed;
        private bool _disposed;

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
            StopClock();
        }

        /// <summary>Applies the config to the NetworkManager and its Unity Transport (added if missing).</summary>
        private void Configure(NetConfig config)
        {
            if (_manager.NetworkConfig == null)
            {
                _manager.NetworkConfig = new NetworkConfig();
            }

            _manager.NetworkConfig.TickRate = (uint)config.TickRate;

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
                    Raise(ClientDisconnected, clientId);
                }

                return;
            }

            if (_machine.IsActive)
            {
                Disconnection why = ClassifyClientDisconnect();
                _machine.NotifyDisconnected(why.Reason, why.Message);
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
            }
        }

        private void StopClock()
        {
            if (_clock != null)
            {
                _clock.Dispose();
                _clock = null;
            }
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
