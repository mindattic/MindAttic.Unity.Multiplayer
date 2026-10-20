using System;
using System.Security.Cryptography;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// A validated join code: <see cref="JoinCodeFormat.MinDataLength"/>–<see cref="JoinCodeFormat.MaxDataLength"/>
    /// data characters plus one check character, all from <see cref="JoinCodeFormat.Alphabet"/>.
    /// </summary>
    /// <remarks>
    /// Every non-empty instance is valid: the only ways to get one are <see cref="Parse"/> /
    /// <see cref="TryParse(string, out JoinCode)"/>, <see cref="FromData"/> and <see cref="Generate()"/>.
    /// <c>default(JoinCode)</c> is the empty code (<see cref="IsEmpty"/>). Equality compares the
    /// canonical form, so <c>abcd-efgh</c> and <c>ABCDEFGH</c> parse to equal codes.
    /// </remarks>
    public readonly struct JoinCode : IEquatable<JoinCode>
    {
        private readonly string value;

        private JoinCode(string canonical)
        {
            value = canonical;
        }

        /// <summary>The empty code, the same as <c>default(JoinCode)</c>.</summary>
        public static JoinCode Empty
        {
            get { return default(JoinCode); }
        }

        /// <summary>True for <c>default(JoinCode)</c>.</summary>
        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(value); }
        }

        /// <summary>The canonical form: upper case, no separators, check character last. Empty for <see cref="Empty"/>.</summary>
        public string Value
        {
            get { return value ?? string.Empty; }
        }

        /// <summary>Total characters, the check character included.</summary>
        public int Length
        {
            get { return Value.Length; }
        }

        /// <summary>The data characters, without the check character.</summary>
        public string Data
        {
            get { return IsEmpty ? string.Empty : value.Substring(0, value.Length - 1); }
        }

        /// <summary>The check character, or <c>'\0'</c> for <see cref="Empty"/>.</summary>
        public char CheckCharacter
        {
            get { return IsEmpty ? '\0' : value[value.Length - 1]; }
        }

        /// <summary>
        /// Builds a code from data characters by appending their check character. Input is normalized
        /// first (case, separators), but look-alikes are not corrected: this is for data a program
        /// produced, not for player input.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
        /// <exception cref="ArgumentException">The data length is out of range or a character is outside the alphabet.</exception>
        public static JoinCode FromData(string data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            string normalized = JoinCodeFormat.Normalize(data);
            if (normalized.Length < JoinCodeFormat.MinDataLength || normalized.Length > JoinCodeFormat.MaxDataLength)
            {
                throw new ArgumentException(
                    "A join code needs " + JoinCodeFormat.MinDataLength + " to " + JoinCodeFormat.MaxDataLength
                    + " data characters; got " + normalized.Length + ".", nameof(data));
            }

            return new JoinCode(normalized + JoinCodeFormat.ComputeCheckCharacter(normalized));
        }

        /// <summary>Generates a random code with <see cref="JoinCodeFormat.DefaultDataLength"/> data characters from a cryptographic RNG.</summary>
        public static JoinCode Generate()
        {
            return Generate(JoinCodeFormat.DefaultDataLength);
        }

        /// <summary>Generates a random code with <paramref name="dataLength"/> data characters from a cryptographic RNG.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="dataLength"/> is out of range.</exception>
        public static JoinCode Generate(int dataLength)
        {
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                return Generate(dataLength, rng);
            }
        }

        /// <summary>
        /// Generates a random code with <paramref name="dataLength"/> data characters, drawing bytes
        /// from <paramref name="rng"/>. Each byte's low 5 bits pick a symbol, so every symbol is
        /// equally likely.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="dataLength"/> is out of range.</exception>
        public static JoinCode Generate(int dataLength, RandomNumberGenerator rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (dataLength < JoinCodeFormat.MinDataLength || dataLength > JoinCodeFormat.MaxDataLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dataLength), dataLength,
                    "Data length must be between " + JoinCodeFormat.MinDataLength + " and " + JoinCodeFormat.MaxDataLength + ".");
            }

            var bytes = new byte[dataLength];
            rng.GetBytes(bytes);

            var chars = new char[dataLength + 1];
            for (int i = 0; i < dataLength; i++)
            {
                chars[i] = JoinCodeFormat.Alphabet[bytes[i] & 0x1F];
            }

            chars[dataLength] = JoinCodeFormat.ComputeCheckCharacter(new string(chars, 0, dataLength));
            return new JoinCode(new string(chars));
        }

        /// <summary>Parses player input, tolerating case, separators and one look-alike character.</summary>
        /// <exception cref="FormatException">The input is not a valid join code; the message says why.</exception>
        public static JoinCode Parse(string input)
        {
            JoinCodeParseResult result;
            if (!TryParse(input, out result))
            {
                throw new FormatException(result.Message);
            }

            return result.Code;
        }

        /// <summary>Parses player input, tolerating case, separators and one look-alike character.</summary>
        public static bool TryParse(string input, out JoinCode code)
        {
            JoinCodeParseResult result;
            bool ok = TryParse(input, out result);
            code = result.Code;
            return ok;
        }

        /// <summary>
        /// Parses player input, tolerating case, separators and one look-alike character, and reports
        /// why it failed (or that a look-alike was corrected) in <paramref name="result"/>.
        /// </summary>
        public static bool TryParse(string input, out JoinCodeParseResult result)
        {
            string normalized = JoinCodeFormat.Normalize(input);

            if (normalized.Length == 0)
            {
                result = JoinCodeParseResult.Fail(JoinCodeError.Empty, -1, "The join code is empty.");
                return false;
            }

            int minLength = JoinCodeFormat.MinDataLength + 1;
            int maxLength = JoinCodeFormat.MaxDataLength + 1;
            if (normalized.Length < minLength)
            {
                result = JoinCodeParseResult.Fail(
                    JoinCodeError.TooShort, -1,
                    "The join code is too short: " + normalized.Length + " characters, at least " + minLength + " expected.");
                return false;
            }

            if (normalized.Length > maxLength)
            {
                result = JoinCodeParseResult.Fail(
                    JoinCodeError.TooLong, -1,
                    "The join code is too long: " + normalized.Length + " characters, at most " + maxLength + " expected.");
                return false;
            }

            int lookAlikePosition = -1;
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (JoinCodeFormat.IsAlphabetCharacter(c))
                {
                    continue;
                }

                if (JoinCodeFormat.LookAlikeCandidates(c).Length > 0 && lookAlikePosition < 0)
                {
                    lookAlikePosition = i;
                    continue;
                }

                result = JoinCodeParseResult.Fail(
                    JoinCodeError.InvalidCharacter, i,
                    "'" + c + "' at position " + (i + 1) + " is not a join-code character.");
                return false;
            }

            if (lookAlikePosition < 0)
            {
                if (!JoinCodeFormat.HasValidCheck(normalized))
                {
                    result = JoinCodeParseResult.Fail(
                        JoinCodeError.ChecksumMismatch, -1,
                        "The join code has a typo: its check character does not match.");
                    return false;
                }

                result = JoinCodeParseResult.Ok(new JoinCode(normalized), false);
                return true;
            }

            string candidates = JoinCodeFormat.LookAlikeCandidates(normalized[lookAlikePosition]);
            char[] chars = normalized.ToCharArray();
            string match = null;
            int matches = 0;
            for (int k = 0; k < candidates.Length; k++)
            {
                chars[lookAlikePosition] = candidates[k];
                var candidate = new string(chars);
                if (JoinCodeFormat.HasValidCheck(candidate))
                {
                    match = candidate;
                    matches++;
                }
            }

            if (matches == 1)
            {
                result = JoinCodeParseResult.Ok(new JoinCode(match), true);
                return true;
            }

            result = JoinCodeParseResult.Fail(
                JoinCodeError.InvalidCharacter, lookAlikePosition,
                "'" + normalized[lookAlikePosition] + "' at position " + (lookAlikePosition + 1)
                + " is not a join-code character (codes never contain 0, O, 1 or I).");
            return false;
        }

        /// <summary>The code grouped for display, <c>ABC-EFG-HJK</c>; empty for <see cref="Empty"/>.</summary>
        public override string ToString()
        {
            return JoinCodeFormat.Format(Value);
        }

        /// <inheritdoc />
        public bool Equals(JoinCode other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is JoinCode other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        /// <summary>True when both codes have the same canonical form.</summary>
        public static bool operator ==(JoinCode left, JoinCode right)
        {
            return left.Equals(right);
        }

        /// <summary>True when the codes differ.</summary>
        public static bool operator !=(JoinCode left, JoinCode right)
        {
            return !left.Equals(right);
        }
    }
}
