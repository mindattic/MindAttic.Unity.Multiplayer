using System.Collections;
using MindAttic.Components.Unity;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
#if MINDATTIC_SHARED
using Unity.Shared.Core;
#endif
using static Unity.Multiplayer.UI.Tests.FrontEndTestSupport;

namespace Unity.Multiplayer.UI.Tests
{
    /// <summary>
    /// UM-T48: the front end on a real NetBootstrap, the way a game's scene has it, in a headless process (the suite runs
    /// with -batchmode -nographics): it finds its bootstrap, builds no UI at all, and still follows the session, so a
    /// dedicated server or bot with the same scene never shows a menu. Plus the profile-backed identity.
    /// </summary>
    public class NetFrontEndHeadlessTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
                _go = null;
            }

            NetFrontEnd.MenusOverride = null;
            CleanUpUi();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Headless_TheSceneServesByItself_AndTheFrontEndCreatesNoUi()
        {
            if (!HeadlessBootstrap.IsHeadless)
            {
                Assert.Ignore("Needs a headless editor (-batchmode / -nographics): run tests/run-playmode.ps1.");
            }

            NetFrontEnd.MenusOverride = null;
            _go = new GameObject("Headless front end");
            _go.SetActive(false);
            NetworkManager manager = _go.AddComponent<NetworkManager>();
            _go.AddComponent<UnityTransport>();
            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }

            manager.NetworkConfig.EnableSceneManagement = false;
            NetBootstrap bootstrap = _go.AddComponent<NetBootstrap>();
            bootstrap.Configure(new NetConfig { Topology = Topology.Host, Port = FreeUdpPort(), GameVersion = "test", JoinCodes = JoinCodeProvider.None });
            NetFrontEnd frontEnd = _go.AddComponent<NetFrontEnd>();
            int gameStarts = 0;
            frontEnd.GameStarted += () => gameStarts++;
            int stacksBefore = Object.FindObjectsByType<MenuStack>(FindObjectsInactive.Include).Length;
            _go.SetActive(true);

            yield return Wait(() => bootstrap.Session.State == SessionState.Running, 5f, () => "the headless scene never served (" + bootstrap.Session.State + ")");
            yield return null;

            Assert.That(frontEnd.Session, Is.SameAs(bootstrap.Session), "found the NetBootstrap beside it");
            Assert.That(bootstrap.Session.Topology, Is.EqualTo(Topology.Server), "a headless process is a dedicated server");
            Assert.That(frontEnd.HasMenus, Is.False);
            Assert.That(frontEnd.MainMenu, Is.Null);
            Assert.That(frontEnd.PauseMenu, Is.Null);
            Assert.That(_go.GetComponentsInChildren<Canvas>(true), Is.Empty, "no canvas");
            Assert.That(Object.FindObjectsByType<MenuStack>(FindObjectsInactive.Include).Length, Is.EqualTo(stacksBefore), "no menu anywhere");
            Assert.That(MaModal.OpenCount, Is.Zero, "no dialog");
            Assert.That(frontEnd.State, Is.EqualTo(FrontEndState.InGame), "it still follows the session");
            Assert.That(gameStarts, Is.EqualTo(1), "and tells the game it is running");

            Assert.That(bootstrap.Leave(), Is.True);
            yield return Wait(() => frontEnd.State == FrontEndState.Menu, 5f, () => "the front end never saw the session end");
            Assert.That(MaModal.OpenCount, Is.Zero, "still no dialog");
        }

        [Test]
        public void IdentityHelpers_CleanNamesTheWayTheServerWantsThem()
        {
            Assert.That(PlayerIdentitySources.CleanName("  Ann  "), Is.EqualTo("Ann"));
            Assert.That(PlayerIdentitySources.CleanName("A\u0007nn\n"), Is.EqualTo("Ann"));
            Assert.That(PlayerIdentitySources.CleanName("   "), Is.EqualTo(PlayerIdentity.DefaultDisplayName));
            Assert.That(PlayerIdentitySources.CleanName(new string('x', 40)).Length, Is.EqualTo(ConnectionPayload.MaxDisplayNameLength));
            Assert.That(PlayerIdentitySources.NearestPaletteIndex(new Color(0.2f, 0.6f, 0.86f)), Is.EqualTo(1), "near blue is blue");
            Assert.That(PlayerIdentitySources.ToPlayerColor(new Color32(1, 2, 3, 4)), Is.EqualTo(new PlayerColor(1, 2, 3, 255)), "always opaque");
        }

#if MINDATTIC_SHARED
        [Test]
        public void ProfileIdentity_ReadsAndSavesTheMindAtticProfile()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "um-t48-profiles-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Profiles.UseRoot(root);
                Assert.That(PlayerIdentitySources.ProfilesAvailable, Is.True);
                IPlayerIdentitySource source = PlayerIdentitySources.Default;
                Assert.That(source, Is.InstanceOf<ProfileIdentitySource>());
                Assert.That(source.StoredIn, Does.Contain("MindAttic profile"));

                source.Save("Cursor Queen", PlayerIdentitySources.Palette[2]);
                Assert.That(Profiles.Current.ReloadSettings().Common.DisplayName, Is.EqualTo("Cursor Queen"), "written to settings.json");
                Assert.That(Profiles.Current.Settings.Common.Color.ToHex(), Is.EqualTo("#2ECC71"));
                Assert.That(new ProfileIdentitySource().DisplayName, Is.EqualTo("Cursor Queen"));
                Assert.That((Color32)new ProfileIdentitySource().Color, Is.EqualTo(PlayerIdentitySources.Palette[2]));
            }
            finally
            {
                Profiles.Reset();
                Unity.Shared.ProfilesBootstrap.Initialize();
                if (System.IO.Directory.Exists(root))
                {
                    System.IO.Directory.Delete(root, true);
                }
            }
        }
#endif
    }
}
