using MindAttic.Components.Unity;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Multiplayer.UI
{
    /// <summary>
    /// The optional settings asset for <see cref="NetFrontEnd"/>: what the menus say and offer, and how a lost connection
    /// is retried. Every field has a working default, so a game needs one only to change something. Create one with
    /// Assets ▸ Create ▸ MindAttic ▸ Multiplayer ▸ Front End Settings and drop it on the NetFrontEnd.
    /// </summary>
    [CreateAssetMenu(menuName = "MindAttic/Multiplayer/Front End Settings", fileName = "FrontEndSettings")]
    public sealed class NetFrontEndSettings : ScriptableObject
    {
        [Header("Main menu")]
        [Tooltip("The main menu's title. Empty uses the product name (Project Settings > Player).")]
        public string title = string.Empty;

        [Tooltip("Offer Join by IP (address and port).")]
        public bool joinByAddress = true;

        [Tooltip("Offer Join by code. Hidden anyway when the NetBootstrap's Join Codes is None.")]
        public bool joinByCode = true;

        [Tooltip("Offer the Settings screen (name and colour, plus anything the game adds).")]
        public bool settingsScreen = true;

        [Tooltip("Offer Quit (never on WebGL, where a page cannot close itself).")]
        public bool quitButton = true;

        [Tooltip("Dim the scene behind the main menu.")]
        public bool dimBehindMenu = true;

        [Header("Hosting")]
        [Tooltip("After hosting, show the lobby (join code, players, Start). Off goes straight into the game.")]
        public bool showLobby = true;

        [Tooltip("A scene the server loads for everyone when the host presses Start (it must be in the build's scene list "
            + "and the NetworkManager must have scene management on). Empty leaves scenes to the game (GameStarted).")]
        public string gameScene = string.Empty;

        [Tooltip("What a kicked player is told.")]
        public string kickMessage = "The host removed you from the game.";

        [Header("In game")]
        [Tooltip("Esc (gamepad Start or B) opens the in-game menu: Resume, Invite, Settings, Leave and the game's own items.")]
        public bool pauseMenu = true;

        [Tooltip("The in-game menu's title.")]
        public string pauseTitle = "Menu";

        [Header("Reconnect")]
        [Tooltip("When a client loses its connection, try to get back into the same seat by itself.")]
        public bool autoReconnect = true;

        [Tooltip("Tries before giving up.")]
        [Range(1, 20)] public int reconnectAttempts = ReconnectPolicy.DefaultMaxAttempts;

        [Tooltip("Seconds before the first try; each later one waits twice as long.")]
        [Range(0f, 10f)] public float reconnectFirstDelay = (float)ReconnectPolicy.DefaultFirstDelaySeconds;

        [Tooltip("The longest wait between tries, in seconds.")]
        [Range(0f, 60f)] public float reconnectMaxDelay = (float)ReconnectPolicy.DefaultMaxDelaySeconds;

        [Header("Look")]
        [Tooltip("The MindAttic theme the menus use. Empty keeps the current one (dark by default).")]
        public ThemeTokens theme;

        [Tooltip("The menus' canvas sorting order (above the game's own UI).")]
        public int sortingOrder = 500;

        /// <summary>The reconnect rules these settings describe.</summary>
        public ReconnectPolicy ToReconnectPolicy()
        {
            return new ReconnectPolicy
            {
                MaxAttempts = autoReconnect ? Mathf.Max(0, reconnectAttempts) : 0,
                FirstDelaySeconds = Mathf.Max(0f, reconnectFirstDelay),
                MaxDelaySeconds = Mathf.Max(0f, reconnectMaxDelay),
            };
        }
    }
}
