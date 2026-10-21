using System;
using System.Globalization;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// One player in a session, as every peer sees them (UM-T55): the seat they sit in (stable across a reconnect), the
    /// connection they are on now, their name and colour, whether they host, their round-trip time to the server, and
    /// whether they are this peer's own player. The server fills it from the connection approval payload and keeps every
    /// peer's <c>NetRoster</c> in step.
    /// </summary>
    /// <remarks>
    /// Immutable: the roster hands out a new record whenever something about the player changes. Compare records by
    /// <see cref="Seat"/>, which identifies a player for as long as their seat lives; <see cref="ClientId"/> changes when
    /// they reconnect.
    /// </remarks>
    public sealed class NetPlayer : IEquatable<NetPlayer>
    {
        /// <summary><see cref="PingMs"/> before the server has measured it (and always for a dedicated server's own view of nobody).</summary>
        public const int UnknownPing = -1;

        /// <summary>Builds a record.</summary>
        /// <param name="seat">The seat index, 0 or more.</param>
        /// <param name="clientId">The connection's client id now (the last one while <paramref name="isConnected"/> is false).</param>
        /// <param name="displayName">The name everyone sees; null is stored as empty.</param>
        /// <param name="color">The colour everyone draws the player in (already resolved: see <see cref="PlayerColors.Resolve"/>).</param>
        /// <param name="isHost">True for a listen server's own player.</param>
        /// <param name="isConnected">False while the player has dropped and their seat is held for a reconnect.</param>
        /// <param name="pingMs">Round-trip time to the server in milliseconds; <see cref="UnknownPing"/> when not measured.</param>
        /// <param name="isLocal">True on the peer whose own player this is.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seat"/> is negative.</exception>
        public NetPlayer(int seat, ulong clientId, string displayName, PlayerColor color, bool isHost, bool isConnected, int pingMs, bool isLocal = false)
        {
            if (seat < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seat), seat, "A seat index is 0 or more.");
            }

            Seat = seat;
            ClientId = clientId;
            DisplayName = displayName ?? string.Empty;
            Color = color;
            IsHost = isHost;
            IsConnected = isConnected;
            PingMs = pingMs < 0 ? UnknownPing : pingMs;
            IsLocal = isLocal;
        }

        /// <summary>The seat index: who this player is for as long as their seat lives, across reconnects.</summary>
        public int Seat { get; }

        /// <summary>The client id of the player's connection now (of the last one while <see cref="IsConnected"/> is false).</summary>
        public ulong ClientId { get; }

        /// <summary>The name everyone sees.</summary>
        public string DisplayName { get; }

        /// <summary>The colour everyone draws the player in: their own, or their seat's palette colour when they never picked one.</summary>
        public PlayerColor Color { get; }

        /// <summary>True for a listen server's own player (the host).</summary>
        public bool IsHost { get; }

        /// <summary>False while the player has dropped and their seat is held for them to reconnect.</summary>
        public bool IsConnected { get; }

        /// <summary>Round-trip time between the player and the server in milliseconds (0 for the host); <see cref="UnknownPing"/> until measured.</summary>
        public int PingMs { get; }

        /// <summary>True on the peer whose own player this is.</summary>
        public bool IsLocal { get; }

        /// <summary>
        /// The record a server keeps for <paramref name="seat"/>: the name as <see cref="PlayerNames.Clean"/> makes it, the
        /// colour <see cref="PlayerColors.Resolve"/>d for the seat, the host flag for a listen server's own seat (client 0),
        /// and connected while the seat is occupied.
        /// </summary>
        /// <param name="seat">The server's seat.</param>
        /// <param name="listenServer">True when the server is a Host (its own player sits in a seat).</param>
        /// <param name="pingMs">The connection's measured round-trip time; ignored (unknown) while the seat is held.</param>
        /// <param name="localClientId">The server's own client id when it plays (a Host), else null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="seat"/> is null.</exception>
        public static NetPlayer FromSeat(Seat seat, bool listenServer, int pingMs, ulong? localClientId)
        {
            if (seat == null)
            {
                throw new ArgumentNullException(nameof(seat));
            }

            bool connected = seat.IsOccupied;
            bool host = listenServer && connected && seat.ClientId == NetSender.ServerClientId;
            return new NetPlayer(seat.Index, seat.ClientId, PlayerNames.Clean(seat.DisplayName), PlayerColors.Resolve(seat.Color, seat.Index),
                host, connected, connected ? (host ? 0 : pingMs) : UnknownPing,
                connected && localClientId.HasValue && seat.ClientId == localClientId.Value);
        }

        /// <summary>This record with <see cref="IsLocal"/> set to <paramref name="isLocal"/>.</summary>
        public NetPlayer WithLocal(bool isLocal)
        {
            return isLocal == IsLocal ? this : new NetPlayer(Seat, ClientId, DisplayName, Color, IsHost, IsConnected, PingMs, isLocal);
        }

        /// <summary>This record with a new name and colour.</summary>
        public NetPlayer WithIdentity(string displayName, PlayerColor color)
        {
            return new NetPlayer(Seat, ClientId, displayName, color, IsHost, IsConnected, PingMs, IsLocal);
        }

        /// <inheritdoc />
        public bool Equals(NetPlayer other)
        {
            return other != null && Seat == other.Seat && ClientId == other.ClientId
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) && Color == other.Color
                && IsHost == other.IsHost && IsConnected == other.IsConnected && PingMs == other.PingMs && IsLocal == other.IsLocal;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as NetPlayer);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Seat * 397) ^ ClientId.GetHashCode() ^ (DisplayName.GetHashCode() * 31) ^ Color.GetHashCode();
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string ping = PingMs == UnknownPing ? "?" : PingMs.ToString(CultureInfo.InvariantCulture);
            return "'" + DisplayName + "' " + Color + " seat " + Seat + " (client " + ClientId
                + (IsHost ? ", host" : string.Empty) + (IsLocal ? ", local" : string.Empty)
                + (IsConnected ? ", " + ping + " ms" : ", reconnecting") + ")";
        }
    }

    /// <summary>What changed about a player (a roster's <c>Changed</c> event).</summary>
    public readonly struct NetPlayerChange
    {
        /// <summary>A change from <paramref name="previous"/> to <paramref name="player"/> (the same seat).</summary>
        public NetPlayerChange(NetPlayer previous, NetPlayer player)
        {
            Previous = previous;
            Player = player;
        }

        /// <summary>The record before.</summary>
        public NetPlayer Previous { get; }

        /// <summary>The record now.</summary>
        public NetPlayer Player { get; }

        /// <summary>True when the name changed.</summary>
        public bool NameChanged
        {
            get { return Previous != null && Player != null && !string.Equals(Previous.DisplayName, Player.DisplayName, StringComparison.Ordinal); }
        }

        /// <summary>True when the colour changed.</summary>
        public bool ColorChanged
        {
            get { return Previous != null && Player != null && Previous.Color != Player.Color; }
        }

        /// <summary>True when the player dropped (their seat is held) or came back.</summary>
        public bool ConnectionChanged
        {
            get { return Previous != null && Player != null && (Previous.IsConnected != Player.IsConnected || Previous.ClientId != Player.ClientId); }
        }

        /// <summary>True when only the measured round-trip time changed.</summary>
        public bool OnlyPingChanged
        {
            get
            {
                return Previous != null && Player != null && Previous.PingMs != Player.PingMs && !NameChanged && !ColorChanged
                    && !ConnectionChanged && Previous.IsHost == Player.IsHost && Previous.IsLocal == Player.IsLocal;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Previous + " -> " + Player;
        }
    }

    /// <summary>The server's answer to a request to change a player's name or colour.</summary>
    public readonly struct PlayerChangeResult
    {
        private PlayerChangeResult(bool accepted, string reason, NetPlayer player)
        {
            Accepted = accepted;
            Reason = reason ?? string.Empty;
            Player = player;
        }

        /// <summary>True when the server applied the change.</summary>
        public bool Accepted { get; }

        /// <summary>Why the server refused, in words fit to show; empty when accepted.</summary>
        public string Reason { get; }

        /// <summary>The player after the change; null when refused.</summary>
        public NetPlayer Player { get; }

        /// <summary>An accepted change.</summary>
        public static PlayerChangeResult Accept(NetPlayer player)
        {
            return new PlayerChangeResult(true, null, player);
        }

        /// <summary>A refused change, with <paramref name="reason"/>.</summary>
        public static PlayerChangeResult Refuse(string reason)
        {
            return new PlayerChangeResult(false, string.IsNullOrEmpty(reason) ? "The server refused the change." : reason, null);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Accepted ? "Accepted: " + Player : "Refused: " + Reason;
        }
    }
}
