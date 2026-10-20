using System;
using System.Text;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The join-code format: data characters from a 32-symbol alphabet with no look-alike glyphs,
    /// followed by one check character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Alphabet.</b> <see cref="Alphabet"/> is the digits 2–9 and the letters A–Z without I and O:
    /// exactly 32 symbols, so each character carries 5 bits. A code never contains 0, O, 1 or I, so a
    /// reader never has to tell them apart.
    /// </para>
    /// <para>
    /// <b>Check character.</b> The last character is a Damm check over GF(32) (the quasigroup
    /// <c>x ∘ y = αx + y</c> with α a root of x⁵ + x² + 1). It catches every single-character
    /// substitution, every swap of two neighbouring characters and every swap of two characters one
    /// apart, wherever they fall, the check character included.
    /// </para>
    /// <para>
    /// <b>Typo tolerance.</b> Parsing ignores case, whitespace and the separators <c>- _ .</c> (and
    /// Unicode dashes). Because a correct code never contains 0, O, 1 or I, such a character is a
    /// misread; if exactly one appears, the parser tries the glyphs it is usually mistaken for
    /// (0/O → D or Q; 1/I → L, J, T or 7) and accepts the one candidate the check character confirms.
    /// The check catches every single substitution, so at most one candidate can pass.
    /// </para>
    /// </remarks>
    public static class JoinCodeFormat
    {
        /// <summary>The 32 symbols a code is written in, in value order: 2–9, then A–Z without I and O.</summary>
        public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        /// <summary>Bits each data character carries (log2 of the alphabet size).</summary>
        public const int BitsPerCharacter = 5;

        /// <summary>Fewest data characters a code may have (the check character not counted).</summary>
        public const int MinDataLength = 6;

        /// <summary>Most data characters a code may have (the check character not counted).</summary>
        public const int MaxDataLength = 16;

        /// <summary>Data characters in a freshly generated code: 10, i.e. 50 bits of entropy.</summary>
        public const int DefaultDataLength = 10;

        /// <summary>Characters per group when a code is formatted for display.</summary>
        public const int DefaultGroupSize = 4;

        /// <summary>The separator <see cref="Format(string)"/> puts between groups.</summary>
        public const char DefaultSeparator = '-';

        // Primitive polynomial x^5 + x^2 + 1 for GF(32).
        private const int FieldPolynomial = 0x25;

        private static readonly sbyte[] ValueOf = BuildValueTable();

        private static readonly string[] LookAlikes = BuildLookAlikeTable();

        /// <summary>Bits of entropy in a random code with <paramref name="dataLength"/> data characters.</summary>
        public static int EntropyBits(int dataLength)
        {
            return dataLength * BitsPerCharacter;
        }

        /// <summary>True when <paramref name="c"/> is one of the 32 alphabet symbols (upper case only).</summary>
        public static bool IsAlphabetCharacter(char c)
        {
            return c < ValueOf.Length && ValueOf[c] >= 0;
        }

        /// <summary>
        /// The check character for <paramref name="data"/>, which must be upper-case alphabet symbols.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="data"/> holds a character outside <see cref="Alphabet"/>.</exception>
        public static char ComputeCheckCharacter(string data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            int interim = 0;
            for (int i = 0; i < data.Length; i++)
            {
                int value = SymbolValue(data[i]);
                if (value < 0)
                {
                    throw new ArgumentException(
                        "Character '" + data[i] + "' at position " + i + " is not in the join-code alphabet.", nameof(data));
                }

                interim = Step(interim, value);
            }

            // interim ∘ c = 0  ⇔  αc' + c = 0 with c' = interim  ⇔  c = α·interim (characteristic 2).
            return Alphabet[MultiplyByAlpha(interim)];
        }

        /// <summary>
        /// True when <paramref name="canonical"/> (upper-case alphabet symbols, check character last)
        /// passes the check. Length is not checked here.
        /// </summary>
        public static bool HasValidCheck(string canonical)
        {
            if (string.IsNullOrEmpty(canonical))
            {
                return false;
            }

            int interim = 0;
            for (int i = 0; i < canonical.Length; i++)
            {
                int value = SymbolValue(canonical[i]);
                if (value < 0)
                {
                    return false;
                }

                interim = Step(interim, value);
            }

            return interim == 0;
        }

        /// <summary>
        /// Upper-cases <paramref name="input"/> and strips whitespace and separators (<c>- _ .</c> and
        /// Unicode dashes). It does not correct look-alikes or validate; <see cref="JoinCode.TryParse(string, out JoinCodeParseResult)"/> does.
        /// Null becomes the empty string.
        /// </summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsWhiteSpace(c) || IsSeparator(c))
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(c));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Splits <paramref name="canonical"/> into groups of <see cref="DefaultGroupSize"/> joined by
        /// <see cref="DefaultSeparator"/>, for display: <c>ABCD-EFGH-JKM</c>.
        /// </summary>
        public static string Format(string canonical)
        {
            return Format(canonical, DefaultGroupSize, DefaultSeparator);
        }

        /// <summary>Splits <paramref name="canonical"/> into groups of <paramref name="groupSize"/> joined by <paramref name="separator"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="groupSize"/> is less than 1.</exception>
        public static string Format(string canonical, int groupSize, char separator)
        {
            if (groupSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(groupSize), groupSize, "Group size must be at least 1.");
            }

            if (string.IsNullOrEmpty(canonical))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(canonical.Length + canonical.Length / groupSize);
            for (int i = 0; i < canonical.Length; i++)
            {
                if (i > 0 && i % groupSize == 0)
                {
                    builder.Append(separator);
                }

                builder.Append(canonical[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// The alphabet symbols <paramref name="c"/> is usually a misreading of, or the empty string
        /// when <paramref name="c"/> is not a known look-alike (0, O, 1, I, upper case).
        /// </summary>
        public static string LookAlikeCandidates(char c)
        {
            return c < LookAlikes.Length && LookAlikes[c] != null ? LookAlikes[c] : string.Empty;
        }

        internal static int SymbolValue(char c)
        {
            return c < ValueOf.Length ? ValueOf[c] : -1;
        }

        private static int Step(int interim, int value)
        {
            return MultiplyByAlpha(interim) ^ value;
        }

        private static int MultiplyByAlpha(int x)
        {
            x <<= 1;
            if ((x & 0x20) != 0)
            {
                x ^= FieldPolynomial;
            }

            return x;
        }

        private static bool IsSeparator(char c)
        {
            switch (c)
            {
                case '-':
                case '_':
                case '.':
                case '‐': // hyphen
                case '‑': // non-breaking hyphen
                case '‒': // figure dash
                case '–': // en dash
                case '—': // em dash
                case '―': // horizontal bar
                case '−': // minus sign
                    return true;
                default:
                    return false;
            }
        }

        private static sbyte[] BuildValueTable()
        {
            var table = new sbyte[128];
            for (int i = 0; i < table.Length; i++)
            {
                table[i] = -1;
            }

            for (int i = 0; i < Alphabet.Length; i++)
            {
                table[Alphabet[i]] = (sbyte)i;
            }

            return table;
        }

        private static string[] BuildLookAlikeTable()
        {
            var table = new string[128];
            table['0'] = "DQ";
            table['O'] = "DQ";
            table['1'] = "LJT7";
            table['I'] = "LJT7";
            return table;
        }
    }
}
