using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Player colours (UM-T55): the palette every MindAttic game offers, the colour a player who never picked one is drawn
    /// in (their seat's palette colour, so neighbours differ), and the format a colour change must have. Engine-free so
    /// <c>dotnet test</c> covers it; the adapter and the front end convert to Unity colours.
    /// </summary>
    public static class PlayerColors
    {
        private static readonly PlayerColor[] s_Palette =
        {
            new PlayerColor(0xE6, 0x4B, 0x3C),
            new PlayerColor(0x34, 0x98, 0xDB),
            new PlayerColor(0x2E, 0xCC, 0x71),
            new PlayerColor(0xF1, 0xC4, 0x0F),
            new PlayerColor(0x9B, 0x59, 0xB6),
            new PlayerColor(0xE6, 0x7E, 0x22),
            new PlayerColor(0x1A, 0xBC, 0x9C),
            new PlayerColor(0xE8, 0x43, 0x93),
        };

        private static readonly string[] s_Names = { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Teal", "Pink" };

        /// <summary>
        /// Eight opaque colours that stay distinct from one another and readable on dark and light backgrounds (the same
        /// palette Unity.Shared picks a default from), in seat order.
        /// </summary>
        public static IReadOnlyList<PlayerColor> Palette
        {
            get { return s_Palette; }
        }

        /// <summary>The names of <see cref="Palette"/>'s colours, in order.</summary>
        public static IReadOnlyList<string> PaletteNames
        {
            get { return s_Names; }
        }

        /// <summary>The palette colour for seat <paramref name="seat"/> (wrapping round after eight).</summary>
        public static PlayerColor ForSeat(int seat)
        {
            int i = ((seat % s_Palette.Length) + s_Palette.Length) % s_Palette.Length;
            return s_Palette[i];
        }

        /// <summary>
        /// True when <paramref name="color"/> means "not picked": opaque white (<see cref="PlayerColor.White"/>, what a
        /// <see cref="PlayerIdentity"/> carries by default) or fully transparent.
        /// </summary>
        public static bool IsUnset(PlayerColor color)
        {
            return color.A == 0 || (color.R == 255 && color.G == 255 && color.B == 255);
        }

        /// <summary>
        /// The colour a player in seat <paramref name="seat"/> is drawn in: <paramref name="chosen"/> made opaque, or the
        /// seat's palette colour when they never picked one (<see cref="IsUnset"/>).
        /// </summary>
        public static PlayerColor Resolve(PlayerColor chosen, int seat)
        {
            return IsUnset(chosen) ? ForSeat(seat) : new PlayerColor(chosen.R, chosen.G, chosen.B, 255);
        }

        /// <summary>Why <paramref name="color"/> cannot be a player's colour (it must be opaque), or null when it can.</summary>
        public static string Validate(PlayerColor color)
        {
            return color.A == 255 ? null : "A player colour must be fully opaque; got alpha " + color.A + ".";
        }

        /// <summary>
        /// Reads <c>#RRGGBB</c> or <c>#RRGGBBAA</c> (the <c>#</c> optional, any case). False for anything else.
        /// </summary>
        public static bool TryParse(string text, out PlayerColor color)
        {
            color = default(PlayerColor);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string hex = text.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
            }

            uint value;
            if ((hex.Length != 6 && hex.Length != 8)
                || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            color = hex.Length == 6 ? PlayerColor.FromRgba32((value << 8) | 0xFF) : PlayerColor.FromRgba32(value);
            return true;
        }

        /// <summary><paramref name="color"/> as <c>#RRGGBB</c> (alpha left out), upper case.</summary>
        public static string ToHex(PlayerColor color)
        {
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>The index of the palette colour nearest <paramref name="color"/>.</summary>
        public static int NearestPaletteIndex(PlayerColor color)
        {
            int best = 0;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < s_Palette.Length; i++)
            {
                PlayerColor p = s_Palette[i];
                int dr = p.R - color.R;
                int dg = p.G - color.G;
                int db = p.B - color.B;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }
    }
}
