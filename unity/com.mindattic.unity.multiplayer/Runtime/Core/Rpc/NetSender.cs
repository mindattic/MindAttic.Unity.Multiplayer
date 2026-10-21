using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Who sent a <see cref="ToServerAttribute"/> call, as the server knows them: the connection's client id, the seat it
    /// sits in and the player's display name. Passed to every <c>On&lt;Name&gt;</c> server handler.
    /// </summary>
    /// <remarks>
    /// Converts to its <c>ulong</c> client id, so a handler can answer the caller through a
    /// <see cref="ToClientAttribute"/> call: <c>Denied(sender, "Not your turn")</c>.
    /// </remarks>
    public readonly struct NetSender : IEquatable<NetSender>
    {
        private readonly string _displayName;

        /// <summary>The client id of the server itself (and of a host's own player).</summary>
        public const ulong ServerClientId = 0;

        /// <summary>Creates a sender record.</summary>
        /// <param name="clientId">The connection's client id.</param>
        /// <param name="seatIndex">The seat it sits in, or -1 for none.</param>
        /// <param name="displayName">The player's display name; null is stored as empty.</param>
        public NetSender(ulong clientId, int seatIndex, string displayName)
        {
            ClientId = clientId;
            SeatIndex = seatIndex < 0 ? -1 : seatIndex;
            _displayName = displayName ?? string.Empty;
        }

        /// <summary>The server (or a host's own player) with no seat information.</summary>
        public static NetSender Server
        {
            get { return new NetSender(ServerClientId, -1, string.Empty); }
        }

        /// <summary>The sending connection's client id.</summary>
        public ulong ClientId { get; }

        /// <summary>The sender's seat index, or -1 when it has none (no session seats players, or the server itself on a dedicated server).</summary>
        public int SeatIndex { get; }

        /// <summary>The name the player connected with; empty when unknown.</summary>
        public string DisplayName
        {
            get { return _displayName ?? string.Empty; }
        }

        /// <summary>True when the sender holds a seat.</summary>
        public bool HasSeat
        {
            get { return SeatIndex >= 0; }
        }

        /// <summary>True when the call came from the server process itself (a host's own player).</summary>
        public bool IsServer
        {
            get { return ClientId == ServerClientId; }
        }

        /// <summary>The sender's client id.</summary>
        public static implicit operator ulong(NetSender sender)
        {
            return sender.ClientId;
        }

        /// <inheritdoc />
        public bool Equals(NetSender other)
        {
            return ClientId == other.ClientId && SeatIndex == other.SeatIndex && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is NetSender other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (ClientId.GetHashCode() * 397) ^ SeatIndex;
            }
        }

        /// <summary>Equality by client id, seat and name.</summary>
        public static bool operator ==(NetSender left, NetSender right)
        {
            return left.Equals(right);
        }

        /// <summary>Inequality by client id, seat and name.</summary>
        public static bool operator !=(NetSender left, NetSender right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string who = DisplayName.Length > 0 ? "'" + DisplayName + "' " : string.Empty;
            return who + "(client " + ClientId + (HasSeat ? ", seat " + SeatIndex : string.Empty) + ")";
        }
    }
}
