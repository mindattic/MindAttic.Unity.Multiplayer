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

        /// <summary>Server side: the client closed its connection (it left, quit or its process ended cleanly).</summary>
        ClientLeft = 8,
    }

    /// <summary>
    /// A disconnect reason plus an optional human-readable message and, for a refused connection, the
    /// typed approval reason the server sent.
    /// </summary>
    public readonly struct Disconnection : IEquatable<Disconnection>
    {
        /// <summary>No disconnect.</summary>
        public static readonly Disconnection None = new Disconnection(DisconnectReason.None, null);

        /// <summary>Creates a disconnection record.</summary>
        public Disconnection(DisconnectReason reason, string message)
            : this(reason, message, ApprovalRejection.None)
        {
        }

        /// <summary>Creates a disconnection record that carries the server's approval rejection.</summary>
        /// <param name="reason">Why it happened.</param>
        /// <param name="message">Detail for the player or the log.</param>
        /// <param name="rejection">
        /// Why connection approval refused the client (with <see cref="DisconnectReason.Rejected"/>);
        /// <see cref="ApprovalRejection.None"/> otherwise.
        /// </param>
        public Disconnection(DisconnectReason reason, string message, ApprovalRejection rejection)
        {
            Reason = reason;
            _message = message;
            Rejection = rejection;
        }

        private readonly string _message;

        /// <summary>Why it happened.</summary>
        public DisconnectReason Reason { get; }

        /// <summary>Detail for the player or the log (for example the approval rejection text). Never null.</summary>
        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        /// <summary>
        /// When <see cref="Reason"/> is <see cref="DisconnectReason.Rejected"/> and the server said why in the
        /// MindAttic wire format: version, protocol, password, full, banned or bad payload. Otherwise <see cref="ApprovalRejection.None"/>.
        /// </summary>
        public ApprovalRejection Rejection { get; }

        /// <inheritdoc />
        public bool Equals(Disconnection other)
        {
            return Reason == other.Reason && Rejection == other.Rejection && string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is Disconnection other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (((int)Reason * 397) ^ ((int)Rejection * 31)) ^ StringComparer.Ordinal.GetHashCode(Message);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string reason = Rejection == ApprovalRejection.None ? Reason.ToString() : Reason + " (" + Rejection + ")";
            return Message.Length == 0 ? reason : reason + ": " + Message;
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
