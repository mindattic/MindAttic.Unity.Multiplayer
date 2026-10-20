namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>Server side: a connection request that connection approval turned down.</summary>
    public readonly struct ConnectionRejection
    {
        /// <summary>Creates the record.</summary>
        public ConnectionRejection(ulong clientId, ApprovalDecision decision)
        {
            ClientId = clientId;
            Decision = decision;
        }

        /// <summary>The connection that was refused.</summary>
        public ulong ClientId { get; }

        /// <summary>Why: the typed <see cref="ApprovalDecision.Rejection"/> and the sentence the client was sent.</summary>
        public ApprovalDecision Decision { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return "Client " + ClientId + " " + Decision;
        }
    }

    /// <summary>Server side: a seated player's connection ended.</summary>
    public readonly struct PlayerDisconnect
    {
        /// <summary>Creates the record.</summary>
        public PlayerDisconnect(ulong clientId, Disconnection why, Seat seat)
        {
            ClientId = clientId;
            Why = why;
            Seat = seat;
        }

        /// <summary>The connection that ended.</summary>
        public ulong ClientId { get; }

        /// <summary>
        /// Why, as the server saw it: <see cref="DisconnectReason.ClientLeft"/>, <see cref="DisconnectReason.TimedOut"/>,
        /// or <see cref="DisconnectReason.Kicked"/> (including a connection replaced by the same player reconnecting).
        /// </summary>
        public Disconnection Why { get; }

        /// <summary>
        /// The seat after the disconnect: <see cref="SeatStatus.Held"/> while it waits for the player,
        /// <see cref="SeatStatus.Released"/> when it was freed, or null when the seat had already moved to a newer
        /// connection of the same player.
        /// </summary>
        public Seat Seat { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return "Client " + ClientId + " " + Why + (Seat == null ? string.Empty : " " + Seat);
        }
    }
}
