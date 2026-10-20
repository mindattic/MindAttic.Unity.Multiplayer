using System;
using System.Collections.Generic;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// What a client sends with its connection request: the game version and protocol hash it was built with,
    /// the player token that identifies it across reconnects, and the name and colour it wants to play as.
    /// <see cref="ConnectionPayloadCodec"/> turns it into bytes; <see cref="ConnectionApprovalValidator"/>
    /// decides whether the server lets it in.
    /// </summary>
    /// <remarks>
    /// Immutable. The constructor accepts anything so a decoder can build an instance and then report every
    /// problem from <see cref="Validate"/>; <see cref="ConnectionPayloadCodec.Encode"/> refuses an invalid one.
    /// </remarks>
    public sealed class ConnectionPayload
    {
        /// <summary>Longest accepted <see cref="GameVersion"/>, in characters.</summary>
        public const int MaxGameVersionLength = 64;

        /// <summary>Longest accepted <see cref="PlayerToken"/>, in characters.</summary>
        public const int MaxPlayerTokenLength = 128;

        /// <summary>Longest accepted <see cref="DisplayName"/>, in characters.</summary>
        public const int MaxDisplayNameLength = 32;

        /// <summary>Builds a payload; call <see cref="Validate"/> to check it.</summary>
        /// <param name="gameVersion">The client's game version; must equal the server's.</param>
        /// <param name="protocolHash">The client's protocol hash; must equal the server's.</param>
        /// <param name="playerToken">
        /// An opaque, stable identifier for this player (for example a GUID kept in local storage). The server
        /// uses it to give a reconnecting player its seat back and to check bans. Printable ASCII, no spaces.
        /// </param>
        /// <param name="displayName">The name other players see.</param>
        /// <param name="color">The colour the player picked.</param>
        public ConnectionPayload(string gameVersion, ProtocolHash protocolHash, string playerToken, string displayName, PlayerColor color)
        {
            GameVersion = gameVersion ?? string.Empty;
            ProtocolHash = protocolHash;
            PlayerToken = playerToken ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Color = color;
        }

        /// <summary>The client's game version.</summary>
        public string GameVersion { get; }

        /// <summary>The client's protocol hash.</summary>
        public ProtocolHash ProtocolHash { get; }

        /// <summary>The opaque, stable identifier for this player.</summary>
        public string PlayerToken { get; }

        /// <summary>The name other players see.</summary>
        public string DisplayName { get; }

        /// <summary>The colour the player picked.</summary>
        public PlayerColor Color { get; }

        /// <summary>Lists every problem with this payload; an empty list means it is valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (GameVersion.Trim().Length == 0)
            {
                errors.Add("GameVersion must not be empty.");
            }
            else if (GameVersion.Length > MaxGameVersionLength)
            {
                errors.Add("GameVersion must be at most " + MaxGameVersionLength + " characters; got " + GameVersion.Length + ".");
            }
            else if (HasControlCharacter(GameVersion))
            {
                errors.Add("GameVersion must not contain control characters.");
            }

            if (PlayerToken.Length == 0)
            {
                errors.Add("PlayerToken must not be empty.");
            }
            else if (PlayerToken.Length > MaxPlayerTokenLength)
            {
                errors.Add("PlayerToken must be at most " + MaxPlayerTokenLength + " characters; got " + PlayerToken.Length + ".");
            }
            else if (!IsPrintableAsciiWithoutSpaces(PlayerToken))
            {
                errors.Add("PlayerToken must be printable ASCII without spaces.");
            }

            if (DisplayName.Trim().Length == 0)
            {
                errors.Add("DisplayName must not be empty.");
            }
            else if (DisplayName.Length > MaxDisplayNameLength)
            {
                errors.Add("DisplayName must be at most " + MaxDisplayNameLength + " characters; got " + DisplayName.Length + ".");
            }
            else if (HasControlCharacter(DisplayName))
            {
                errors.Add("DisplayName must not contain control characters.");
            }
            else if (DisplayName.Trim().Length != DisplayName.Length)
            {
                errors.Add("DisplayName must not start or end with whitespace.");
            }

            return errors;
        }

        /// <summary>True when <see cref="Validate"/> finds no problems.</summary>
        public bool IsValid
        {
            get { return Validate().Count == 0; }
        }

        /// <summary>Throws <see cref="ArgumentException"/> listing every problem if the payload is invalid.</summary>
        public void EnsureValid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
            {
                throw new ArgumentException("Invalid ConnectionPayload: " + string.Join(" ", errors));
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return "'" + DisplayName + "' " + Color + " version " + GameVersion + " protocol " + ProtocolHash;
        }

        private static bool HasControlCharacter(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsControl(text[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPrintableAsciiWithoutSpaces(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '!' || text[i] > '~')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
