using System;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>Where a session is in its life.</summary>
    /// <remarks>
    /// A Host or Server goes <c>Idle → Starting → Running → Stopping → Idle</c>.
    /// A Client goes <c>Idle → Connecting → Connected → Disconnected</c>, and can start again from Disconnected.
    /// </remarks>
    public enum SessionState
    {
        /// <summary>Nothing running. The initial state, and where a Host or Server returns after stopping.</summary>
        Idle = 0,

        /// <summary>Host or Server: the transport is binding and the server is coming up.</summary>
        Starting = 1,

        /// <summary>Host or Server: accepting players and ticking.</summary>
        Running = 2,

        /// <summary>Host or Server: shutting down; players are being dropped.</summary>
        Stopping = 3,

        /// <summary>Client: connecting to the server and awaiting approval.</summary>
        Connecting = 4,

        /// <summary>Client: approved and in the session.</summary>
        Connected = 5,

        /// <summary>Client: out of the session; <see cref="SessionStateMachine.LastDisconnect"/> says why.</summary>
        Disconnected = 6,
    }

    /// <summary>Why a session ended or a client was dropped.</summary>
    public enum DisconnectReason
    {
        /// <summary>No disconnect has happened.</summary>
        None = 0,

        /// <summary>This process asked to leave or stop.</summary>
        LocalLeave = 1,

        /// <summary>The server shut down.</summary>
        ServerShutdown = 2,

        /// <summary>The client could not reach the server.</summary>
        ConnectFailed = 3,

        /// <summary>The connection stopped responding.</summary>
        TimedOut = 4,

        /// <summary>Connection approval refused the client (version, protocol, full, banned, bad payload).</summary>
        Rejected = 5,

        /// <summary>The server removed the client.</summary>
        Kicked = 6,

        /// <summary>The transport failed (for example the port was already in use).</summary>
        TransportFailure = 7,
    }

    /// <summary>A disconnect reason plus an optional human-readable message.</summary>
    public readonly struct Disconnection : IEquatable<Disconnection>
    {
        /// <summary>No disconnect.</summary>
        public static readonly Disconnection None = new Disconnection(DisconnectReason.None, null);

        /// <summary>Creates a disconnection record.</summary>
        public Disconnection(DisconnectReason reason, string message)
        {
            Reason = reason;
            _message = message;
        }

        private readonly string _message;

        /// <summary>Why it happened.</summary>
        public DisconnectReason Reason { get; }

        /// <summary>Detail for the player or the log (for example the approval rejection text). Never null.</summary>
        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        /// <inheritdoc />
        public bool Equals(Disconnection other)
        {
            return Reason == other.Reason && string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is Disconnection other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ((int)Reason * 397) ^ StringComparer.Ordinal.GetHashCode(Message);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Message.Length == 0 ? Reason.ToString() : Reason + ": " + Message;
        }
    }

    /// <summary>One state change, raised by <see cref="SessionStateMachine.StateChanged"/>.</summary>
    public readonly struct SessionStateChange
    {
        /// <summary>Creates a change record.</summary>
        public SessionStateChange(SessionState from, SessionState to, Disconnection disconnection)
        {
            From = from;
            To = to;
            Disconnection = disconnection;
        }

        /// <summary>The state before.</summary>
        public SessionState From { get; }

        /// <summary>The state after.</summary>
        public SessionState To { get; }

        /// <summary>Why the session ended, when this change ends one; otherwise <see cref="Core.Disconnection.None"/>.</summary>
        public Disconnection Disconnection { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Disconnection.Reason == DisconnectReason.None
                ? From + " -> " + To
                : From + " -> " + To + " (" + Disconnection + ")";
        }
    }
}
