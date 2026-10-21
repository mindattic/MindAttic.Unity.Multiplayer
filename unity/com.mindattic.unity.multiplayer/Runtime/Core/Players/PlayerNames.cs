using System.Globalization;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The rules for a player's display name (UM-T55), engine-free so <c>dotnet test</c> covers them. The server checks a
    /// rename against <see cref="Validate"/> and refuses it with the reason; a name that arrives in a connection approval
    /// payload is <see cref="Clean"/>ed into the roster instead, so an old client never fails to join over its name.
    /// </summary>
    /// <remarks>
    /// Allowed: letters and digits in any script, combining marks, punctuation and symbols (emoji included), and single
    /// ASCII spaces between words. Refused: control and invisible formatting characters (zero-width joiners, direction
    /// overrides), private-use and unassigned code points, broken surrogate pairs, other kinds of blank space, line
    /// breaks, two spaces in a row, a leading or trailing space, and more than <see cref="MaxLength"/> characters.
    /// </remarks>
    public static class PlayerNames
    {
        /// <summary>The longest name, in UTF-16 characters (the approval payload's limit).</summary>
        public const int MaxLength = ConnectionPayload.MaxDisplayNameLength;

        /// <summary>Why <paramref name="name"/> is not an acceptable display name, or null when it is.</summary>
        public static string Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A name cannot be empty.";
            }

            if (name.Length > MaxLength)
            {
                return "A name can be at most " + MaxLength + " characters.";
            }

            if (name[0] == ' ' || name[name.Length - 1] == ' ')
            {
                return "A name cannot start or end with a space.";
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ')
                {
                    if (name[i + 1] == ' ')
                    {
                        return "A name cannot have two spaces in a row.";
                    }

                    continue;
                }

                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1]))
                    {
                        return "A name cannot contain a broken character.";
                    }

                    if (!IsAllowed(CharUnicodeInfo.GetUnicodeCategory(name, i)))
                    {
                        return "A name cannot contain invisible or unprintable characters.";
                    }

                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(c))
                {
                    return "A name cannot contain a broken character.";
                }

                if (!IsAllowed(CharUnicodeInfo.GetUnicodeCategory(c)))
                {
                    return char.IsWhiteSpace(c) ? "A name can only use plain spaces between words." : "A name cannot contain invisible or unprintable characters.";
                }
            }

            return null;
        }

        /// <summary>True when <paramref name="name"/> passes <see cref="Validate"/>.</summary>
        public static bool IsValid(string name)
        {
            return Validate(name) == null;
        }

        /// <summary>
        /// <paramref name="name"/> made acceptable: characters <see cref="Validate"/> refuses dropped, any run of blank space
        /// made one space, trimmed, cut to <see cref="MaxLength"/>, and <paramref name="fallback"/> when nothing is left.
        /// </summary>
        public static string Clean(string name, string fallback = PlayerIdentity.DefaultDisplayName)
        {
            var kept = new StringBuilder();
            bool space = false;
            string text = name ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    space = kept.Length > 0;
                    continue;
                }

                string unit;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]) || !IsAllowed(CharUnicodeInfo.GetUnicodeCategory(text, i)))
                    {
                        continue;
                    }

                    unit = text.Substring(i, 2);
                    i++;
                }
                else if (char.IsLowSurrogate(c) || !IsAllowed(CharUnicodeInfo.GetUnicodeCategory(c)))
                {
                    continue;
                }
                else
                {
                    unit = c.ToString();
                }

                int needed = unit.Length + (space ? 1 : 0);
                if (kept.Length + needed > MaxLength)
                {
                    break;
                }

                if (space)
                {
                    kept.Append(' ');
                    space = false;
                }

                kept.Append(unit);
            }

            return kept.Length > 0 ? kept.ToString() : (fallback ?? PlayerIdentity.DefaultDisplayName);
        }

        private static bool IsAllowed(UnicodeCategory category)
        {
            switch (category)
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.MathSymbol:
                case UnicodeCategory.CurrencySymbol:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.OtherSymbol:
                    return true;
                default:
                    return false; // controls, formats, separators, surrogates, private use, unassigned
            }
        }
    }
}
