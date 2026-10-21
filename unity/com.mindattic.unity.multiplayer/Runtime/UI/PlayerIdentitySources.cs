using System;
using Unity.Multiplayer.Core;
using UnityEngine;
#if MINDATTIC_SHARED
using Unity.Shared;
using Unity.Shared.Core;
#endif

namespace Unity.Multiplayer.UI
{
    /// <summary>
    /// Where the front end reads and saves the name and colour a player plays as. They go into the connection approval
    /// payload (<see cref="PlayerIdentity"/>), so the server's seat and every other player see them.
    /// </summary>
    /// <remarks>
    /// <see cref="PlayerIdentitySources.Default"/> picks the MindAttic profile (Unity.Shared) when that package is
    /// installed and PlayerPrefs otherwise. A game with its own account system sets <see cref="NetFrontEnd.Identity"/>
    /// to its own implementation.
    /// </remarks>
    public interface IPlayerIdentitySource
    {
        /// <summary>The name other players see.</summary>
        string DisplayName { get; }

        /// <summary>The colour the player plays as (opaque).</summary>
        Color Color { get; }

        /// <summary>Where it is kept, in a few words for the Settings screen ("your MindAttic profile", "this computer").</summary>
        string StoredIn { get; }

        /// <summary>Keeps a new name and colour (a blank name goes back to the default).</summary>
        void Save(string displayName, Color color);
    }

    /// <summary>The identity sources the package ships, and the rules for a name the server will accept.</summary>
    public static class PlayerIdentitySources
    {
        /// <summary>
        /// Eight colours that stay distinct from one another and readable on dark and light backgrounds (Core's
        /// <see cref="PlayerColors.Palette"/>, the same palette Unity.Shared picks a default from), offered by the Settings screen.
        /// </summary>
        public static readonly Color32[] Palette = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(PlayerColors.Palette, c => c.ToColor32()));

        /// <summary>The names of <see cref="Palette"/>'s colours, in order (Core's <see cref="PlayerColors.PaletteNames"/>).</summary>
        public static readonly string[] PaletteNames = System.Linq.Enumerable.ToArray(PlayerColors.PaletteNames);

        /// <summary>True when the MindAttic profile (com.mindattic.unity.shared) is compiled in.</summary>
        public static bool ProfilesAvailable
        {
            get
            {
#if MINDATTIC_SHARED
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>The profile-backed source when Unity.Shared is installed, else the PlayerPrefs one.</summary>
        public static IPlayerIdentitySource Default
        {
            get
            {
#if MINDATTIC_SHARED
                return new ProfileIdentitySource();
#else
                return new PlayerPrefsIdentitySource();
#endif
            }
        }

        /// <summary>
        /// <paramref name="name"/> as the server accepts it (Core's <see cref="PlayerNames.Clean"/>, the rule a rename is
        /// checked against): trimmed, invisible and control characters removed, single spaces, at most
        /// <see cref="PlayerNames.MaxLength"/> characters, and <see cref="PlayerIdentity.DefaultDisplayName"/> when nothing is left.
        /// </summary>
        public static string CleanName(string name)
        {
            return PlayerNames.Clean(name);
        }

        /// <summary>A palette colour picked from <paramref name="name"/>, the same on every run.</summary>
        public static Color ColorFor(string name)
        {
            uint hash = 2166136261;
            foreach (char c in (name ?? string.Empty).ToLowerInvariant())
            {
                hash = (hash ^ c) * 16777619;
            }

            return Palette[hash % (uint)Palette.Length];
        }

        /// <summary>The index of the palette colour nearest <paramref name="color"/>.</summary>
        public static int NearestPaletteIndex(Color color)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                Color p = Palette[i];
                float d = (p.r - color.r) * (p.r - color.r) + (p.g - color.g) * (p.g - color.g) + (p.b - color.b) * (p.b - color.b);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>A Unity colour as the engine-free <see cref="PlayerColor"/> the payload carries (opaque).</summary>
        public static PlayerColor ToPlayerColor(Color color)
        {
            Color32 c = color;
            return new PlayerColor(c.r, c.g, c.b, 255);
        }

        /// <summary>A <see cref="PlayerColor"/> as a Unity colour.</summary>
        public static Color ToUnityColor(PlayerColor color)
        {
            return new Color32(color.R, color.G, color.B, color.A);
        }
    }

    /// <summary>
    /// The fallback identity: a name and colour kept in PlayerPrefs on this computer. The default name is the
    /// computer's user name, the default colour a palette colour picked from it.
    /// </summary>
    public sealed class PlayerPrefsIdentitySource : IPlayerIdentitySource
    {
        /// <summary>PlayerPrefs key of the name.</summary>
        public const string NameKey = "MindAttic.Multiplayer.DisplayName";

        /// <summary>PlayerPrefs key of the colour (<c>#RRGGBB</c>).</summary>
        public const string ColorKey = "MindAttic.Multiplayer.Color";

        /// <inheritdoc />
        public string DisplayName
        {
            get
            {
                string stored = PlayerPrefs.GetString(NameKey, string.Empty);
                return PlayerIdentitySources.CleanName(stored.Length > 0 ? stored : DefaultName());
            }
        }

        /// <inheritdoc />
        public Color Color
        {
            get
            {
                Color color;
                return ColorUtility.TryParseHtmlString(PlayerPrefs.GetString(ColorKey, string.Empty), out color)
                    ? new Color(color.r, color.g, color.b, 1f)
                    : PlayerIdentitySources.ColorFor(DisplayName);
            }
        }

        /// <inheritdoc />
        public string StoredIn
        {
            get { return "this computer"; }
        }

        /// <inheritdoc />
        public void Save(string displayName, Color color)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                PlayerPrefs.DeleteKey(NameKey);
            }
            else
            {
                PlayerPrefs.SetString(NameKey, PlayerIdentitySources.CleanName(displayName));
            }

            PlayerPrefs.SetString(ColorKey, "#" + ColorUtility.ToHtmlStringRGB(color));
            PlayerPrefs.Save();
        }

        private static string DefaultName()
        {
            try
            {
                return Environment.UserName;
            }
            catch (Exception)
            {
                return PlayerIdentity.DefaultDisplayName;
            }
        }
    }

#if MINDATTIC_SHARED
    /// <summary>
    /// The player's MindAttic profile (Unity.Shared): <c>Profiles.Current.Settings.Common.DisplayName</c> and
    /// <c>.Color</c>, the same name and colour every MindAttic game uses.
    /// </summary>
    public sealed class ProfileIdentitySource : IPlayerIdentitySource
    {
        /// <inheritdoc />
        public string DisplayName
        {
            get { return PlayerIdentitySources.CleanName(Common.DisplayName); }
        }

        /// <inheritdoc />
        public Color Color
        {
            get { return Common.GetUnityColor(); }
        }

        /// <inheritdoc />
        public string StoredIn
        {
            get { return "your MindAttic profile (" + Profiles.Current.Name + ")"; }
        }

        /// <inheritdoc />
        public void Save(string displayName, Color color)
        {
            CommonSettings common = Common;
            common.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : PlayerIdentitySources.CleanName(displayName);
            common.SetUnityColor(color);
            if (!Profiles.Current.TrySaveSettings())
            {
                Debug.LogWarning("[MindAttic.Multiplayer] Could not save the profile's settings; the name and colour last until the game closes.");
            }
        }

        private static CommonSettings Common
        {
            get
            {
                ProfilesBootstrap.Initialize();
                return Profiles.Current.Settings.Common;
            }
        }
    }
#endif
}
