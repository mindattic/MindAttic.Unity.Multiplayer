using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>Unity colours for the engine-free <see cref="PlayerColor"/> and <see cref="NetPlayer"/> (UM-T55).</summary>
    public static class PlayerColorExtensions
    {
        /// <summary><paramref name="color"/> as a Unity <see cref="Color32"/>.</summary>
        public static Color32 ToColor32(this PlayerColor color)
        {
            return new Color32(color.R, color.G, color.B, color.A);
        }

        /// <summary>A Unity colour as a <see cref="PlayerColor"/> (made opaque, as a player's colour must be).</summary>
        public static PlayerColor ToPlayerColor(this Color color)
        {
            Color32 c = color;
            return new PlayerColor(c.r, c.g, c.b, 255);
        }

        /// <summary>A Unity colour as a <see cref="PlayerColor"/> (made opaque, as a player's colour must be).</summary>
        public static PlayerColor ToPlayerColor(this Color32 color)
        {
            return new PlayerColor(color.r, color.g, color.b, 255);
        }

        /// <summary>The colour every peer draws <paramref name="player"/> in, as a Unity colour.</summary>
        public static Color32 UnityColor(this NetPlayer player)
        {
            return player != null ? player.Color.ToColor32() : new Color32(255, 255, 255, 255);
        }
    }
}
