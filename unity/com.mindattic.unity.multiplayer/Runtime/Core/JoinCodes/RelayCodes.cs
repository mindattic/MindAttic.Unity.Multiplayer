using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Maps Unity Relay's own join codes to <see cref="JoinCode"/>s and back. Relay codes are 6 to 12 characters
    /// from <see cref="Alphabet"/>, every one of which is in <see cref="JoinCodeFormat.Alphabet"/>, so a Relay code
    /// is used as the join code's data and gains the check character: players get the same typo-tolerant format
    /// whichever service minted the code.
    /// </summary>
    public static class RelayCodes
    {
        /// <summary>The characters Unity Relay writes its join codes in (its documentation's restricted set).</summary>
        public const string Alphabet = "6789BCDFGHJKLMNPQRTW";

        /// <summary>Shortest Relay join code.</summary>
        public const int MinLength = 6;

        /// <summary>Longest Relay join code.</summary>
        public const int MaxLength = 12;

        /// <summary>The join code players type for Relay code <paramref name="relayJoinCode"/> (case and separators ignored).</summary>
        /// <exception cref="NotSupportedException">The Relay code is not 6-12 characters of <see cref="Alphabet"/>.</exception>
        public static JoinCode ToJoinCode(string relayJoinCode)
        {
            string normalized = JoinCodeFormat.Normalize(relayJoinCode ?? string.Empty);
            if (!IsRelayShaped(normalized))
            {
                throw new NotSupportedException(
                    "'" + relayJoinCode + "' is not a Unity Relay join code (" + MinLength + "-" + MaxLength + " characters of " + Alphabet + ").");
            }

            return JoinCode.FromData(normalized);
        }

        /// <summary>
        /// The Relay code inside <paramref name="code"/>, when its data could be one (6-12 characters of
        /// <see cref="Alphabet"/>). A rendezvous code can look Relay-shaped by chance, so a true result means "worth
        /// asking Relay", not "certainly Relay".
        /// </summary>
        public static bool TryGetRelayCode(JoinCode code, out string relayJoinCode)
        {
            relayJoinCode = null;
            if (code.IsEmpty || !IsRelayShaped(code.Data))
            {
                return false;
            }

            relayJoinCode = code.Data;
            return true;
        }

        private static bool IsRelayShaped(string data)
        {
            if (data.Length < MinLength || data.Length > MaxLength)
            {
                return false;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (Alphabet.IndexOf(data[i]) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
