using System;
using System.Collections.Generic;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// Who this player is, as far as the server knows: the stable player token that gets a reconnecting
    /// player its seat back, plus the name and colour it plays as. A client sends it inside its
    /// <see cref="ConnectionPayload"/>.
    /// </summary>
    /// <remarks>
    /// Immutable. Keep the token across launches (for example in local storage) so a player who drops,
    /// crashes or restarts the game gets the same seat back within the server's reconnect grace window.
    /// </remarks>
    public sealed class PlayerIdentity
    {
        /// <summary>The display name <see cref="CreateNew"/> uses when none is given.</summary>
        public const string DefaultDisplayName = "Player";

        /// <summary>Builds an identity; the fields follow <see cref="ConnectionPayload"/>'s rules.</summary>
        /// <exception cref="ArgumentException">The token or display name is invalid (see <see cref="ConnectionPayload.Validate"/>).</exception>
        public PlayerIdentity(string playerToken, string displayName, PlayerColor color)
        {
            var probe = new ConnectionPayload("probe", ProtocolHash.None, playerToken, displayName, color);
            var problems = new List<string>();
            foreach (string problem in probe.Validate())
            {
                if (!problem.StartsWith("GameVersion", StringComparison.Ordinal))
                {
                    problems.Add(problem);
                }
            }

            if (problems.Count > 0)
            {
                throw new ArgumentException("Invalid PlayerIdentity: " + string.Join(" ", problems));
            }

            PlayerToken = playerToken;
            DisplayName = displayName;
            Color = color;
        }

        /// <summary>The opaque, stable identifier for this player.</summary>
        public string PlayerToken { get; }

        /// <summary>The name other players see.</summary>
        public string DisplayName { get; }

        /// <summary>The colour the player picked.</summary>
        public PlayerColor Color { get; }

        /// <summary>A new identity with a fresh random token (32 hex digits).</summary>
        /// <param name="displayName">The name other players see; <see cref="DefaultDisplayName"/> when null or blank.</param>
        /// <param name="color">The colour; white when null.</param>
        public static PlayerIdentity CreateNew(string displayName = null, PlayerColor? color = null)
        {
            return new PlayerIdentity(NewToken(),
                string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName.Trim(),
                color ?? PlayerColor.White);
        }

        /// <summary>A fresh random player token: 32 lower-case hex digits.</summary>
        public static string NewToken()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>The same player under another name or colour (the token is kept).</summary>
        public PlayerIdentity With(string displayName = null, PlayerColor? color = null)
        {
            return new PlayerIdentity(PlayerToken, displayName ?? DisplayName, color ?? Color);
        }

        /// <summary>The connection payload this player sends to a server of <paramref name="gameVersion"/>.</summary>
        public ConnectionPayload ToPayload(string gameVersion, ProtocolHash protocolHash)
        {
            return new ConnectionPayload(gameVersion, protocolHash, PlayerToken, DisplayName, Color);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return "'" + DisplayName + "' " + Color;
        }
    }
}
