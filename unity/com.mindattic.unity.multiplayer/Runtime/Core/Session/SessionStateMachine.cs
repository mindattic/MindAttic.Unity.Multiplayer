using System;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The engine-free life cycle of one session. The NGO adapter drives it from NetworkManager
    /// callbacks; game code reads <see cref="State"/> and listens to <see cref="StateChanged"/>.
    /// </summary>
    /// <remarks>
    /// <para>Host / Server: <c>Idle → Starting → Running → Stopping → Idle</c>.
    /// Client: <c>Idle → Connecting → Connected → Disconnected</c>; a Disconnected client may
    /// <see cref="Start"/> again or <see cref="Reset"/> to Idle.</para>
    /// <para>Requests from game code (<see cref="Start"/>) throw on an illegal transition, because they are
    /// programming errors. Notifications from the transport (<see cref="NotifyStarted"/>,
    /// <see cref="NotifyConnected"/>, <see cref="NotifyStopped"/>, <see cref="NotifyDisconnected(Disconnection)"/>) and
    /// <see cref="Stop"/> are tolerant: a late or duplicate one is ignored and returns false, since
    /// transports report events after the session has already ended.</para>
    /// <para>Not thread-safe: drive it from one thread (Unity's main thread).</para>
    /// </remarks>
    public sealed class SessionStateMachine
    {
        private Disconnection _pendingStop = Disconnection.None;

        /// <summary>Raised after every state change, once the new state is visible.</summary>
        public event Action<SessionStateChange> StateChanged;

        /// <summary>The current state.</summary>
        public SessionState State { get; private set; } = SessionState.Idle;

        /// <summary>The topology of the current or last session; null before the first <see cref="Start"/>.</summary>
        public Topology? Topology { get; private set; }

        /// <summary>Why the last session ended; <see cref="Disconnection.None"/> while one is live or none has ended.</summary>
        public Disconnection LastDisconnect { get; private set; } = Disconnection.None;

        /// <summary>True from Start until the session has ended (any state but Idle and Disconnected).</summary>
        public bool IsActive
        {
            get { return State != SessionState.Idle && State != SessionState.Disconnected; }
        }

        /// <summary>True when the session is fully up: Running (Host/Server) or Connected (Client).</summary>
        public bool IsLive
        {
            get { return State == SessionState.Running || State == SessionState.Connected; }
        }

        /// <summary>Whether the machine ever moves directly from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static bool IsAllowed(SessionState from, SessionState to)
        {
            switch (from)
            {
                case SessionState.Idle:
                case SessionState.Disconnected:
                    return to == SessionState.Starting || to == SessionState.Connecting
                        || (from == SessionState.Disconnected && to == SessionState.Idle);
                case SessionState.Starting:
                    return to == SessionState.Running || to == SessionState.Stopping || to == SessionState.Idle;
                case SessionState.Running:
                    return to == SessionState.Stopping || to == SessionState.Idle;
                case SessionState.Stopping:
                    return to == SessionState.Idle;
                case SessionState.Connecting:
                    return to == SessionState.Connected || to == SessionState.Disconnected;
                case SessionState.Connected:
                    return to == SessionState.Disconnected;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Begins a session: Host and Server go to Starting, Client goes to Connecting.
        /// Allowed only from Idle or Disconnected.
        /// </summary>
        /// <exception cref="InvalidOperationException">A session is already active.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="topology"/> is not a defined value.</exception>
        public void Start(Topology topology)
        {
            if (!Enum.IsDefined(typeof(Topology), topology))
            {
                throw new ArgumentOutOfRangeException(nameof(topology), topology, "Unknown topology.");
            }

            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Cannot start a " + topology + " session while the session is " + State + "; stop it first.");
            }

            Topology = topology;
            LastDisconnect = Disconnection.None;
            _pendingStop = Disconnection.None;
            MoveTo(topology.RunsServer() ? SessionState.Starting : SessionState.Connecting, Disconnection.None);
        }

        /// <summary>The server is up: Starting → Running. Ignored (returns false) in any other state.</summary>
        public bool NotifyStarted()
        {
            if (State != SessionState.Starting)
            {
                return false;
            }

            MoveTo(SessionState.Running, Disconnection.None);
            return true;
        }

        /// <summary>The client was approved: Connecting → Connected. Ignored (returns false) in any other state.</summary>
        public bool NotifyConnected()
        {
            if (State != SessionState.Connecting)
            {
                return false;
            }

            MoveTo(SessionState.Connected, Disconnection.None);
            return true;
        }

        /// <summary>
        /// Leaves or stops by local request. Host/Server: Starting or Running → Stopping (finish with
        /// <see cref="NotifyStopped"/>). Client: Connecting or Connected → Disconnected with
        /// <see cref="DisconnectReason.LocalLeave"/>. Returns false when there is nothing to stop.
        /// </summary>
        public bool Stop()
        {
            switch (State)
            {
                case SessionState.Starting:
                case SessionState.Running:
                    _pendingStop = new Disconnection(DisconnectReason.LocalLeave, null);
                    MoveTo(SessionState.Stopping, Disconnection.None);
                    return true;
                case SessionState.Connecting:
                case SessionState.Connected:
                    End(SessionState.Disconnected, new Disconnection(DisconnectReason.LocalLeave, null));
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The server finished shutting down: Stopping → Idle. Ignored (returns false) in any other state.</summary>
        public bool NotifyStopped()
        {
            if (State != SessionState.Stopping)
            {
                return false;
            }

            End(SessionState.Idle, _pendingStop);
            return true;
        }

        /// <summary>
        /// The session ended without a local request. Client: Connecting or Connected → Disconnected.
        /// Host/Server: Starting or Running → Idle (a failed start or a lost transport). While Stopping it
        /// completes the stop with the local reason. Ignored (returns false) when nothing is active.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is <see cref="DisconnectReason.None"/>.</exception>
        public bool NotifyDisconnected(DisconnectReason reason, string message = null)
        {
            return NotifyDisconnected(new Disconnection(reason, message));
        }

        /// <summary>
        /// The session ended without a local request; the same as <see cref="NotifyDisconnected(DisconnectReason, string)"/>
        /// but keeping the whole record (for example a server's <see cref="Disconnection.Rejection"/>).
        /// </summary>
        /// <exception cref="ArgumentException">The reason is <see cref="DisconnectReason.None"/> or undefined.</exception>
        public bool NotifyDisconnected(Disconnection disconnection)
        {
            DisconnectReason reason = disconnection.Reason;
            if (reason == DisconnectReason.None || !Enum.IsDefined(typeof(DisconnectReason), reason))
            {
                throw new ArgumentException("A disconnect needs a real reason, not " + reason + ".", nameof(disconnection));
            }

            switch (State)
            {
                case SessionState.Connecting:
                case SessionState.Connected:
                    End(SessionState.Disconnected, disconnection);
                    return true;
                case SessionState.Starting:
                case SessionState.Running:
                    End(SessionState.Idle, disconnection);
                    return true;
                case SessionState.Stopping:
                    End(SessionState.Idle, _pendingStop);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Acknowledges a client disconnect: Disconnected → Idle, keeping <see cref="LastDisconnect"/>.
        /// Returns false in any other state.
        /// </summary>
        public bool Reset()
        {
            if (State != SessionState.Disconnected)
            {
                return false;
            }

            MoveTo(SessionState.Idle, Disconnection.None);
            return true;
        }

        private void End(SessionState to, Disconnection why)
        {
            LastDisconnect = why;
            _pendingStop = Disconnection.None;
            MoveTo(to, why);
        }

        private void MoveTo(SessionState to, Disconnection why)
        {
            SessionState from = State;
            if (!IsAllowed(from, to))
            {
                // Unreachable through the public API; guards the table and the methods against drifting apart.
                throw new InvalidOperationException("Illegal session transition " + from + " -> " + to + ".");
            }

            State = to;
            Action<SessionStateChange> handler = StateChanged;
            if (handler != null)
            {
                handler(new SessionStateChange(from, to, why));
            }
        }
    }
}
