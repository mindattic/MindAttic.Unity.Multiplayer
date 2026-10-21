using System;
using System.Linq;
using MindAttic.Components.Unity;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Unity.Multiplayer.UI.Testing
{
    /// <summary>
    /// Drives a game's <see cref="NetFrontEnd"/> from its own PlayMode tests: build its menus in a batch-mode run (where a
    /// headless process would otherwise get none), bind it to a test session, read its lobby and dialogs, and click its
    /// buttons the way a player would.
    /// </summary>
    /// <remarks>
    /// Compiled only in the editor and in test builds, and only when MindAttic Components is installed (the front end's
    /// own condition), so it never ships in a player. Reference <c>Unity.Multiplayer.UI.Testing</c> from the game's test
    /// assembly.
    /// </remarks>
    /// <example>
    /// <code>
    /// [SetUp] public void SetUp() => FrontEndTesting.ForceMenus = true;
    /// [TearDown] public void TearDown() { FrontEndTesting.ForceMenus = null; FrontEndTesting.CleanUpUi(); }
    ///
    /// FrontEndTesting.Click(frontEnd.MainMenu.Current, "Host");
    /// </code>
    /// </example>
    public static class FrontEndTesting
    {
        /// <summary>
        /// True builds every front end's menus even in a headless (batch-mode) run, false never builds them, null (the
        /// default) follows <see cref="NetLaunch.ShowsMenus"/>. Set it before the front end's Start; reset it to null in
        /// the test's teardown.
        /// </summary>
        public static bool? ForceMenus
        {
            get { return NetFrontEnd.MenusOverride; }
            set { NetFrontEnd.MenusOverride = value; }
        }

        /// <summary>
        /// Makes <paramref name="frontEnd"/> drive <paramref name="session"/> (for example one from
        /// <c>TestSessions.Create</c>) with <paramref name="config"/>'s settings instead of a NetBootstrap's. Call it before
        /// the front end's Start (add the component to an inactive GameObject, bind, then activate it).
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void Bind(NetFrontEnd frontEnd, NetSession session, Func<NetConfig> config)
        {
            if (frontEnd == null)
            {
                throw new ArgumentNullException(nameof(frontEnd));
            }

            frontEnd.Bind(session, config);
        }

        /// <summary>The main menu's root screen (Host, Join, Settings, Quit); null without menus.</summary>
        public static MenuScreen MainScreen(NetFrontEnd frontEnd)
        {
            return frontEnd.MainScreen;
        }

        /// <summary>The host's lobby screen while it shows; null otherwise.</summary>
        public static MenuScreen LobbyScreen(NetFrontEnd frontEnd)
        {
            return frontEnd.LobbyScreen;
        }

        /// <summary>The lobby's join-code display; null when no code was published or there is no lobby.</summary>
        public static MaCodeDisplay LobbyCode(NetFrontEnd frontEnd)
        {
            return frontEnd.LobbyCode;
        }

        /// <summary>The lobby's live roster (one row per seat); null when there is no lobby.</summary>
        public static MaList LobbyRoster(NetFrontEnd frontEnd)
        {
            return frontEnd.LobbyRoster;
        }

        /// <summary>The busy dialog while connecting or reconnecting; null when none is up.</summary>
        public static MaModal BusyDialog(NetFrontEnd frontEnd)
        {
            return frontEnd.BusyDialog;
        }

        /// <summary>The last message dialog (why a game ended or a join failed); null before any.</summary>
        public static MaModal MessageDialog(NetFrontEnd frontEnd)
        {
            return frontEnd.MessageDialog;
        }

        /// <summary>
        /// Clicks the visible button labelled <paramref name="label"/> under <paramref name="root"/>, as a player would.
        /// Fails, listing the buttons there are, when there is none or it is disabled.
        /// </summary>
        public static void Click(Component root, string label)
        {
            Assert.That(root, Is.Not.Null, "nothing to click '" + label + "' on");
            MaButton button = root.GetComponentsInChildren<MaButton>(false).FirstOrDefault(b => b.Label == label);
            Assert.That(button, Is.Not.Null, "no '" + label + "' button; there are: "
                + string.Join(", ", root.GetComponentsInChildren<MaButton>(false).Select(b => b.Label)));
            Assert.That(button.Interactable, Is.True, "'" + label + "' is disabled");
            button.Click();
        }

        /// <summary>True when <paramref name="root"/> shows a button labelled <paramref name="label"/>.</summary>
        public static bool HasButton(Component root, string label)
        {
            return root != null && root.GetComponentsInChildren<MaButton>(false).Any(b => b.Label == label);
        }

        /// <summary>The labels of the visible labelled buttons under <paramref name="root"/>, in order (icon buttons left out).</summary>
        public static string[] Buttons(Component root)
        {
            return root.GetComponentsInChildren<MaButton>(false).Where(b => !(b is MaIconButton)).Select(b => b.Label).ToArray();
        }

        /// <summary>Closes every dialog and toast and removes the shared overlay and event systems the menus made (a test's teardown).</summary>
        public static void CleanUpUi()
        {
            MaModal.CloseAll();
            MaToast.DismissAll();
            MaCanvas.DestroyOverlay();
            foreach (EventSystem es in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
            {
                UnityEngine.Object.Destroy(es.gameObject);
            }
        }
    }

    /// <summary>
    /// A player name and colour kept in memory, so a test never reads or writes PlayerPrefs or the real MindAttic profile:
    /// <c>frontEnd.Identity = new FixedIdentitySource("Ann", Color.red);</c>
    /// </summary>
    public sealed class FixedIdentitySource : IPlayerIdentitySource
    {
        /// <summary>A source that starts as <paramref name="displayName"/> in <paramref name="color"/>.</summary>
        public FixedIdentitySource(string displayName, Color color)
        {
            DisplayName = displayName;
            Color = color;
        }

        /// <inheritdoc />
        public string DisplayName { get; private set; }

        /// <inheritdoc />
        public Color Color { get; private set; }

        /// <inheritdoc />
        public string StoredIn
        {
            get { return "the test"; }
        }

        /// <summary>How many times the Settings screen saved to it.</summary>
        public int Saves { get; private set; }

        /// <inheritdoc />
        public void Save(string displayName, Color color)
        {
            DisplayName = displayName;
            Color = color;
            Saves++;
        }
    }
}
