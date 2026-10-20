using System;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The server's connection-approval rule, engine-free so it is tested here and merely called by the NGO
    /// adapter's approval callback (UM-T9).
    /// </summary>
    /// <remarks>
    /// Checks run in this order and the first failure decides the reason:
    /// <list type="number">
    /// <item><description><see cref="ApprovalRejection.BadPayload"/>: the bytes do not decode to a valid payload
    /// (except an unreadable newer format whose game version differs, which is a version mismatch).</description></item>
    /// <item><description><see cref="ApprovalRejection.VersionMismatch"/>: game versions differ (ordinal, ignoring surrounding whitespace).</description></item>
    /// <item><description><see cref="ApprovalRejection.ProtocolMismatch"/>: protocol hashes differ.</description></item>
    /// <item><description><see cref="ApprovalRejection.Banned"/>: the player token is banned.</description></item>
    /// <item><description><see cref="ApprovalRejection.Full"/>: no seat is free.</description></item>
    /// </list>
    /// </remarks>
    public sealed class ConnectionApprovalValidator
    {
        /// <summary>Builds the rule for one server.</summary>
        /// <param name="gameVersion">The server's game version.</param>
        /// <param name="protocolHash">The server's protocol hash (from <see cref="ProtocolHash.FromAssemblies(System.Reflection.Assembly[])"/>).</param>
        /// <param name="maxPlayers">Seats, the host's own player included.</param>
        /// <param name="banList">Bans to enforce; null for none.</param>
        /// <exception cref="ArgumentException"><paramref name="gameVersion"/> is empty or <paramref name="maxPlayers"/> is below one.</exception>
        public ConnectionApprovalValidator(string gameVersion, ProtocolHash protocolHash, int maxPlayers, IBanList banList = null)
        {
            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                throw new ArgumentException("The server's game version must not be empty.", nameof(gameVersion));
            }

            if (maxPlayers < 1)
            {
                throw new ArgumentException("MaxPlayers must be at least 1; got " + maxPlayers + ".", nameof(maxPlayers));
            }

            GameVersion = gameVersion.Trim();
            ProtocolHash = protocolHash;
            MaxPlayers = maxPlayers;
            BanList = banList;
        }

        /// <summary>Builds the rule from a server's <see cref="NetConfig"/> (game version and max players).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public static ConnectionApprovalValidator FromConfig(NetConfig config, ProtocolHash protocolHash, IBanList banList = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            return new ConnectionApprovalValidator(config.GameVersion, protocolHash, config.MaxPlayers, banList);
        }

        /// <summary>The server's game version, trimmed.</summary>
        public string GameVersion { get; }

        /// <summary>The server's protocol hash.</summary>
        public ProtocolHash ProtocolHash { get; }

        /// <summary>Seats, the host's own player included.</summary>
        public int MaxPlayers { get; }

        /// <summary>The bans enforced, or null.</summary>
        public IBanList BanList { get; }

        /// <summary>Decides on raw connection data.</summary>
        /// <param name="connectionData">The bytes the client sent (<see cref="ConnectionPayloadCodec.Encode"/>).</param>
        /// <param name="connectedPlayers">Players already connected, not counting this one.</param>
        public ApprovalDecision Validate(byte[] connectionData, int connectedPlayers)
        {
            PayloadDecodeResult decoded = ConnectionPayloadCodec.TryDecode(connectionData);
            if (!decoded.Success)
            {
                if (decoded.Error == PayloadDecodeError.UnsupportedFormatVersion
                    && decoded.GameVersion != null && !VersionMatches(decoded.GameVersion))
                {
                    return VersionMismatch(decoded.GameVersion, null);
                }

                return ApprovalDecision.Reject(ApprovalRejection.BadPayload,
                    "The server could not read your connection request: " + decoded.Message);
            }

            return Validate(decoded.Payload, connectedPlayers);
        }

        /// <summary>Decides on an already decoded payload.</summary>
        /// <param name="payload">The client's payload; null is a bad payload.</param>
        /// <param name="connectedPlayers">Players already connected, not counting this one.</param>
        public ApprovalDecision Validate(ConnectionPayload payload, int connectedPlayers)
        {
            if (payload == null)
            {
                return ApprovalDecision.Reject(ApprovalRejection.BadPayload, "The connection request carried no payload.");
            }

            var problems = payload.Validate();
            if (problems.Count > 0)
            {
                return ApprovalDecision.Reject(ApprovalRejection.BadPayload,
                    "The server could not read your connection request: " + string.Join(" ", problems), payload);
            }

            if (!VersionMatches(payload.GameVersion))
            {
                return VersionMismatch(payload.GameVersion, payload);
            }

            if (payload.ProtocolHash != ProtocolHash)
            {
                return ApprovalDecision.Reject(ApprovalRejection.ProtocolMismatch,
                    "Your game build's network protocol (" + payload.ProtocolHash + ") does not match the server's ("
                    + ProtocolHash + "). Both need the same build of version " + GameVersion + ".", payload);
            }

            if (BanList != null && BanList.IsBanned(payload.PlayerToken))
            {
                return ApprovalDecision.Reject(ApprovalRejection.Banned, "You are banned from this server.", payload);
            }

            if (connectedPlayers >= MaxPlayers)
            {
                return ApprovalDecision.Reject(ApprovalRejection.Full,
                    "The server is full (" + MaxPlayers + " of " + MaxPlayers + " players).", payload);
            }

            return ApprovalDecision.Approve(payload);
        }

        private bool VersionMatches(string clientVersion)
        {
            return string.Equals(clientVersion.Trim(), GameVersion, StringComparison.Ordinal);
        }

        private ApprovalDecision VersionMismatch(string clientVersion, ConnectionPayload payload)
        {
            return ApprovalDecision.Reject(ApprovalRejection.VersionMismatch,
                "The server runs game version " + GameVersion + "; you have " + clientVersion.Trim()
                + ". Install the same version to join.", payload);
        }
    }
}
