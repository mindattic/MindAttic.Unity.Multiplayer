using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MindAttic.Components.Unity;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Multiplayer.UI.Testing;
using Unity.Multiplayer.Tests;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using static Unity.Multiplayer.UI.Tests.FrontEndTestSupport;

namespace Unity.Multiplayer.UI.Tests
{
    /// <summary>
    /// UM-T48: the drop-in front end, driven through its menus the way a player would. A host and a client, each with
    /// its own NetFrontEnd over a harness NetworkManager, talk over real Unity Transport on loopback (a free port each
    /// test): host from the menu, join by IP and by code from the menu (code through an in-process rendezvous server),
    /// the version-mismatch and kicked dialogs, and an automatic reconnect that gets the same seat back.
    /// </summary>
    public class NetFrontEndTests : NetcodeIntegrationTest
    {
        private static readonly Color Red = new Color32(0xE6, 0x4B, 0x3C, 0xFF);
        private static readonly Color Blue = new Color32(0x34, 0x98, 0xDB, 0xFF);

        private readonly List<GameObject> _objects = new List<GameObject>();
        private NetSession _hostSession;
        private NetSession _clientSession;
        private NetFrontEnd _host;
        private NetFrontEnd _client;
        private FixedIdentitySource _hostIdentity;
        private FixedIdentitySource _clientIdentity;
        private InProcessRendezvousServer _rendezvous;
        private UdpLinkProxy _proxy;
        private ushort _port;
        private string _hostVersion;
        private string _clientVersion;
        private bool _codes;
        private bool _defaultJoinCodes;
        private readonly List<Disconnection> _clientLeft = new List<Disconnection>();
        private int _clientReconnects;
        private int _hostGameStarts;

        public NetFrontEndTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.EnableSceneManagement = false; // the sessions start NGO, so no scene sync to connect
            }

            _port = FreePort.Udp();
            _hostVersion = "test";
            _clientVersion = "test";
            _codes = false;
            _defaultJoinCodes = false;
            _clientLeft.Clear();
            _clientReconnects = 0;
            _hostGameStarts = 0;
            _hostSession = TestSessions.Create(m_ServerNetworkManager);
            _hostSession.EnvironmentReader = _ => null;
            _clientSession = TestSessions.Create(m_ClientNetworkManagers[0]);
            FrontEndTesting.ForceMenus = true;
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            foreach (GameObject go in _objects)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _objects.Clear();
            _clientSession?.Dispose();
            _hostSession?.Dispose();
            _rendezvous?.Dispose();
            _proxy?.Dispose();
            _proxy = null;
            _clientSession = null;
            _hostSession = null;
            _rendezvous = null;
            FrontEndTesting.ForceMenus = null;
            CleanUpUi();
            yield return null;
        }

        private NetConfig HostConfig()
        {
            var config = new NetConfig { Port = _port, GameVersion = _hostVersion, JoinCodes = JoinCodeProvider.None, ReconnectGraceSeconds = 30 };
            if (_codes)
            {
                config.JoinCodes = _defaultJoinCodes ? JoinCodeProvider.Auto : JoinCodeProvider.Rendezvous;
                config.RendezvousUrl = _rendezvous.Url;
                config.RendezvousSecret = _rendezvous.Secret;
            }

            return config;
        }

        private NetConfig ClientConfig()
        {
            var config = new NetConfig { Port = _port, GameVersion = _clientVersion, JoinCodes = JoinCodeProvider.None };
            if (_codes)
            {
                config.JoinCodes = JoinCodeProvider.Auto;
                config.RendezvousUrl = _rendezvous.Url;
            }

            return config;
        }

        /// <summary>Both front ends, built (their Start has run) with their menus up.</summary>
        private IEnumerator CreateFrontEnds()
        {
            _hostIdentity = new FixedIdentitySource("Ann", Red);
            _clientIdentity = new FixedIdentitySource("Bob", Blue);
            _host = NewFrontEnd("Host front end", _hostSession, HostConfig, _hostIdentity);
            _client = NewFrontEnd("Client front end", _clientSession, ClientConfig, _clientIdentity);
            _host.GameStarted += () => _hostGameStarts++;
            _client.Left += _clientLeft.Add;
            _client.Reconnected += () => _clientReconnects++;
            yield return null;
            Assert.That(_host.HasMenus && _client.HasMenus, Is.True, "FrontEndTesting.ForceMenus builds the menus in batch mode");
            Assert.That(_host.State, Is.EqualTo(FrontEndState.Menu));
        }

        private NetFrontEnd NewFrontEnd(string name, NetSession session, System.Func<NetConfig> config, IPlayerIdentitySource identity)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            NetFrontEnd frontEnd = go.AddComponent<NetFrontEnd>();
            FrontEndTesting.Bind(frontEnd, session, config);
            frontEnd.Identity = identity;
            frontEnd.Reconnect = new ReconnectPolicy { MaxAttempts = 5, FirstDelaySeconds = 0.2, MaxDelaySeconds = 1 };
            _objects.Add(go);
            go.SetActive(true);
            return frontEnd;
        }

        private IEnumerator HostFromMenu()
        {
            Click(_host.MainScreen, "Host");
            yield return Wait(() => _host.State == FrontEndState.Lobby, 10f, () => "the host never reached the lobby (" + _host.State + ", session " + _hostSession.State + ")");
        }

        private IEnumerator JoinByAddressFromMenu(string typed)
        {
            Click(_client.MainScreen, "Join by IP");
            MenuScreen join = _client.MainMenu.Current;
            Assert.That(join.Title, Is.EqualTo("Join by IP"));
            MaTextbox address = join.GetComponentInChildren<MaTextbox>();
            address.Text = typed;
            Click(join, "Join");
            yield return null;
        }

        private IEnumerator ClientInGame()
        {
            yield return Wait(() => _client.State == FrontEndState.InGame, 10f,
                () => "the client never got in (" + _client.State + ", session " + _clientSession.State + ", " + _clientSession.LastDisconnect + ")");
        }

        private Seat ClientSeat()
        {
            return _hostSession.Seats.Single(s => s.PlayerToken == _clientSession.Identity.PlayerToken);
        }

        [UnityTest]
        public IEnumerator HostFromMenu_ShowsLobby_JoinByIpFromMenu_RosterShowsNameAndColour_StartEntersTheGame()
        {
            yield return CreateFrontEnds();
            Assert.That(Buttons(_host.MainScreen), Is.EqualTo(new[] { "Host", "Join by IP", "Settings", "Quit" }),
                "no join codes configured: no Join by code");
            Assert.That(_host.MainScreen.Subtitle, Is.EqualTo("Playing as Ann"));

            yield return HostFromMenu();
            Assert.That(_hostSession.IsHost, Is.True);
            Assert.That(_host.LobbyCode, Is.Null, "no code without a join-code service");
            Assert.That(_host.LobbyScreen.Subtitle, Does.Contain("join by IP").And.Contain(":" + _port));
            Assert.That(_host.LobbyRoster.Rows.Select(r => r.Text), Is.EqualTo(new[] { "Ann" }));
            Assert.That(_host.BusyDialog, Is.Null, "the busy dialog closed");

            yield return JoinByAddressFromMenu("127.0.0.1:" + _port);
            Assert.That(_client.BusyDialog, Is.Not.Null.And.Property("IsOpen").True, "connecting shows a busy dialog");
            yield return ClientInGame();
            Assert.That(_client.BusyDialog, Is.Null);
            Assert.That(_client.MainMenu.IsOpen, Is.False, "the main menu hands over to the game");
            Assert.That(_client.PauseMenu.gameObject.activeSelf, Is.True, "Esc opens the in-game menu now");

            // The client's name and colour went into the approval payload: the host's seat and roster carry them.
            yield return Wait(() => _host.LobbyRoster.Count == 2, 5f, () => "the roster never showed the second player");
            Seat bob = ClientSeat();
            Assert.That(bob.DisplayName, Is.EqualTo("Bob"));
            Assert.That(bob.Color, Is.EqualTo(PlayerIdentitySources.ToPlayerColor(Blue)));
            MaListRow row = _host.LobbyRoster.Rows.Single(r => r.Text == "Bob");
            Assert.That(row.Swatch.HasValue && ((Color32)row.Swatch.Value).Equals((Color32)Blue), Is.True, "Bob's swatch is his colour");
            Assert.That(row.Actions.Count, Is.EqualTo(1), "the host can remove Bob");

            Click(_host.LobbyScreen, "Start");
            Assert.That(_host.State, Is.EqualTo(FrontEndState.InGame));
            Assert.That(_hostGameStarts, Is.EqualTo(1), "GameStarted");
            Assert.That(_host.MainMenu.IsOpen, Is.False);

            // The in-game menu: the host's has Invite and End game.
            _host.PauseMenu.Open();
            Assert.That(Buttons(_host.PauseMenu.Current), Is.EqualTo(new[] { "Resume", "Invite players", "Settings", "End game" }));
            Click(_host.PauseMenu.Current, "Resume");
            Assert.That(_host.PauseMenu.IsOpen, Is.False);

            // The client leaves from its own in-game menu, confirming.
            _client.PauseMenu.Open();
            Click(_client.PauseMenu.Current, "Leave game");
            Click(MaModal.Top, "Leave");
            yield return Wait(() => _client.State == FrontEndState.Menu, 5f, () => "the client never got back to the menu");
            Assert.That(_clientLeft.Select(d => d.Reason), Is.EqualTo(new[] { DisconnectReason.LocalLeave }));
            Assert.That(_client.MainMenu.IsOpen && _client.MainMenu.Current == _client.MainScreen, Is.True);
            Assert.That(_client.LastMessage, Is.Null, "leaving on purpose shows no dialog");
        }

        [UnityTest]
        public IEnumerator JoinByCodeFromMenu_UsesTheCodeTheLobbyShows()
        {
            _rendezvous = new InProcessRendezvousServer();
            _codes = true;
            yield return CreateFrontEnds();
            Assert.That(HasButton(_client.MainScreen, "Join by code"), Is.True);

            yield return HostFromMenu();
            yield return Wait(() => _host.LobbyCode != null, 10f, () => "the lobby never showed a join code");
            string shown = _host.LobbyCode.Code;
            Assert.That(shown, Is.EqualTo(_hostSession.PublishedCodeText), "the lobby shows the grouped code");
            Assert.That(shown, Does.Match("^[A-Z0-9]{3}-[A-Z0-9]{3}-[A-Z0-9]{3}$"));
            Assert.That(_host.LobbyCode.CopyButton, Is.Not.Null, "with a Copy button");

            Click(_client.MainScreen, "Join by code");
            MenuScreen join = _client.MainMenu.Current;
            Assert.That(join.Title, Is.EqualTo("Join by code"));
            join.GetComponentInChildren<MaTextbox>().Text = shown.Replace("-", string.Empty).ToLowerInvariant();
            Click(join, "Join");
            yield return ClientInGame();
            Assert.That(_clientSession.Config.Port, Is.EqualTo(_port), "joined the host the code leads to");
            yield return Wait(() => _host.LobbyRoster.Count == 2, 5f, () => "the roster never showed Bob");
        }

        [UnityTest]
        public IEnumerator HostFromMenu_DefaultJoinCodes_WithARendezvousUrl_ShowsARendezvousCode()
        {
            if (RelayJoinCodeService.IsAvailable)
            {
                Assert.Ignore("com.unity.services.multiplayer is installed; the default tries Relay first.");
            }

            // UM-T63: Join Codes left at Auto (the NetBootstrap default) with a rendezvous URL: Relay is not installed, so
            // the host publishes through the rendezvous service instead of hosting with no code.
            _rendezvous = new InProcessRendezvousServer();
            _codes = true;
            _defaultJoinCodes = true;
            yield return CreateFrontEnds();
            yield return HostFromMenu();
            yield return Wait(() => _host.LobbyCode != null, 10f, () => "the lobby never showed a join code");
            Assert.That(_host.LobbyCode.Code, Is.EqualTo(_hostSession.PublishedCodeText));
            Assert.That(_rendezvous.Find(_hostSession.PublishedCode), Is.Not.Null, "published through the rendezvous service");

            Click(_client.MainScreen, "Join by code");
            _client.MainMenu.Current.GetComponentInChildren<MaTextbox>().Text = _host.LobbyCode.Code;
            Click(_client.MainMenu.Current, "Join");
            yield return ClientInGame();
        }

        [UnityTest]
        public IEnumerator VersionMismatch_IsExplainedInADialog_AndLeavesThePlayerOnTheMenu()
        {
            _hostVersion = "2.0";
            _clientVersion = "1.0";
            yield return CreateFrontEnds();
            yield return HostFromMenu();

            yield return JoinByAddressFromMenu("127.0.0.1:" + _port);
            yield return Wait(() => _client.State == FrontEndState.Menu && _client.LastMessage.HasValue, 10f,
                () => "the client was never told (" + _client.State + ", " + _clientSession.LastDisconnect + ")");

            DisconnectMessage message = _client.LastMessage.Value;
            Assert.That(message.Title, Is.EqualTo("Different version"));
            Assert.That(message.Detail, Does.Contain("2.0").And.Contain("1.0"), "the server's own sentence names both versions");
            Assert.That(_client.MessageDialog, Is.Not.Null.And.Property("IsOpen").True);
            Assert.That(_client.MessageDialog.Title, Is.EqualTo("Different version"));
            Assert.That(_client.BusyDialog, Is.Null);
            Assert.That(_clientLeft.Single().Rejection, Is.EqualTo(ApprovalRejection.VersionMismatch));
            Assert.That(_client.MainMenu.Current, Is.SameAs(_client.MainScreen), "back on the main menu");
            Assert.That(_clientReconnects, Is.Zero, "a refusal is never retried");
        }

        [UnityTest]
        public IEnumerator Kicked_FromTheLobbyRoster_ShowsTheReason_AndIsNotRetried()
        {
            yield return CreateFrontEnds();
            yield return HostFromMenu();
            yield return JoinByAddressFromMenu("127.0.0.1:" + _port);
            yield return ClientInGame();
            yield return Wait(() => _host.LobbyRoster.Count == 2, 5f, () => "the roster never showed Bob");

            // The host removes Bob from the roster, confirming.
            _host.LobbyRoster.Rows.Single(r => r.Text == "Bob").Actions[0].Button.onClick.Invoke();
            Assert.That(MaModal.Top.Title, Is.EqualTo("Remove Bob?"));
            Click(MaModal.Top, "Remove");

            yield return Wait(() => _client.State == FrontEndState.Menu && _client.LastMessage.HasValue, 10f,
                () => "the client was never told (" + _client.State + ", " + _clientSession.LastDisconnect + ")");
            Assert.That(_client.LastMessage.Value.Title, Is.EqualTo("Removed from the game"));
            Assert.That(_client.LastMessage.Value.Body, Is.EqualTo("The host removed you from the game."));
            Assert.That(_client.MessageDialog.Title, Is.EqualTo("Removed from the game"));
            Assert.That(_clientLeft.Single().Reason, Is.EqualTo(DisconnectReason.Kicked));
            Assert.That(_clientReconnects, Is.Zero);
            yield return Wait(() => _host.LobbyRoster.Count == 1, 5f, () => "Bob never left the host's roster");
        }

        [UnityTest]
        public IEnumerator LostConnection_ReconnectsByItself_ToTheSameSeat()
        {
            // Short transport timeouts so a dead link is noticed in about a second on both ends.
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                var transport = (UnityTransport)manager.NetworkConfig.NetworkTransport;
                transport.HeartbeatTimeoutMS = 200;
                transport.DisconnectTimeoutMS = 1200;
            }

            // The client reaches the host through a relay that can cut the link, as a dropped Wi-Fi connection would.
            _proxy = new UdpLinkProxy(_port);
            yield return CreateFrontEnds();
            yield return HostFromMenu();
            Click(_host.LobbyScreen, "Start");
            yield return JoinByAddressFromMenu("127.0.0.1:" + _proxy.Port);
            yield return ClientInGame();
            Seat before = ClientSeat();
            var states = new List<FrontEndState>();
            _client.StateChanged += states.Add;

            _proxy.Blocked = true;
            yield return Wait(() => _client.State == FrontEndState.Reconnecting, 15f,
                () => "the client never noticed the dead link (" + _clientSession.State + ")");
            Assert.That(_clientSession.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.TimedOut), _clientSession.LastDisconnect.ToString());
            Assert.That(_client.BusyDialog, Is.Not.Null.And.Property("IsOpen").True, "reconnecting shows progress");
            Assert.That(_client.PauseMenu.gameObject.activeSelf, Is.False, "no in-game menu while reconnecting");
            Assert.That(_client.MainMenu.IsOpen, Is.False, "and no main menu either: the player is not back at the start");

            _proxy.Blocked = false; // the network comes back
            yield return Wait(() => _client.State == FrontEndState.InGame && _clientReconnects == 1, 30f,
                () => "the client never got back in (" + _client.State + ", session " + _clientSession.State + ", " + _clientSession.LastDisconnect + ")");
            Assert.That(states, Is.EqualTo(new[] { FrontEndState.Reconnecting, FrontEndState.InGame }));
            Assert.That(_clientLeft, Is.Empty, "a restored connection never went back to the menu");
            Assert.That(_client.BusyDialog, Is.Null);
            Assert.That(_client.LastMessage, Is.Null, "no dialog for a connection that came back");

            yield return Wait(() => _hostSession.TryGetSeat(_clientSession.LocalClientId, out _), 5f, () => "the host never seated the returning client");
            Assert.That(_hostSession.TryGetSeat(_clientSession.LocalClientId, out Seat after), Is.True);
            Assert.That(after.Index, Is.EqualTo(before.Index), "the same seat");
            Assert.That(after.PlayerToken, Is.EqualTo(before.PlayerToken));
            Assert.That(after.Reconnects, Is.EqualTo(1), "a reconnect, not a new player");
            Assert.That(_hostSession.Seats.Count, Is.EqualTo(2), "no second seat was taken");
        }

        [UnityTest]
        public IEnumerator LostConnection_ThatNeverComesBack_GivesUpWithAMessage()
        {
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                var transport = (UnityTransport)manager.NetworkConfig.NetworkTransport;
                transport.HeartbeatTimeoutMS = 200;
                transport.DisconnectTimeoutMS = 1200;
                transport.ConnectTimeoutMS = 200;
                transport.MaxConnectAttempts = 3;
            }

            _proxy = new UdpLinkProxy(_port);
            yield return CreateFrontEnds();
            _client.Reconnect = new ReconnectPolicy { MaxAttempts = 2, FirstDelaySeconds = 0.1, MaxDelaySeconds = 0.2 };
            yield return HostFromMenu();
            yield return JoinByAddressFromMenu("127.0.0.1:" + _proxy.Port);
            yield return ClientInGame();

            // Each failed try makes Unity Transport log "Failed to connect to server." as an error, which is the point here.
            LogAssert.ignoreFailingMessages = true;
            _proxy.Blocked = true;
            yield return Wait(() => _client.State == FrontEndState.Menu, 30f,
                () => "the client never gave up (" + _client.State + ", session " + _clientSession.State + ")");
            Assert.That(_client.LastMessage.HasValue, Is.True);
            Assert.That(_client.LastMessage.Value.Title, Is.EqualTo("Connection lost"));
            Assert.That(_client.LastMessage.Value.Body, Does.Contain("after 2 tries"));
            Assert.That(_clientReconnects, Is.Zero);
            Assert.That(_clientLeft.Single().Reason, Is.EqualTo(DisconnectReason.TimedOut), "Left carries the reason the connection was lost");
            Assert.That(_client.MainMenu.IsOpen && _client.MainMenu.Current == _client.MainScreen, Is.True, "back on the main menu");
        }

        [UnityTest]
        public IEnumerator Settings_SaveNameAndColour_AndGameItemsSitBeforeQuit()
        {
            yield return CreateFrontEnds();
            _host.AddScreen("Options", s => s.AddToggle("Subtitles", true));
            _host.AddSettingsItems(s => s.AddToggle("Show ping", false));
            Assert.That(Buttons(_host.MainScreen), Is.EqualTo(new[] { "Host", "Join by IP", "Settings", "Options", "Quit" }));

            Click(_host.MainScreen, "Options");
            Assert.That(_host.MainMenu.Current.Title, Is.EqualTo("Options"));
            _host.MainMenu.Back();

            Click(_host.MainScreen, "Settings");
            MenuScreen settings = _host.MainMenu.Current;
            Assert.That(settings.Title, Is.EqualTo("Settings"));
            Assert.That(settings.Subtitle, Is.EqualTo("Saved to the test."));
            Assert.That(settings.GetComponentsInChildren<MaToggle>().Select(t => t.name).Count(), Is.EqualTo(1), "the game's setting is on the screen");
            settings.GetComponentInChildren<MaTextbox>().Text = "  Annie  ";
            settings.GetComponentInChildren<MaSelect>().Value = 6; // Teal
            _host.MainMenu.Back();
            yield return null;

            Assert.That(_hostIdentity.Saves, Is.EqualTo(1));
            Assert.That(_hostIdentity.DisplayName, Is.EqualTo("  Annie  "), "the source gets what was typed and cleans it itself");
            Assert.That((Color32)_hostIdentity.Color, Is.EqualTo(PlayerIdentitySources.Palette[6]));
            Assert.That(_host.MainScreen.Subtitle, Is.EqualTo("Playing as Annie"));

            // The new name goes to the server on the next host.
            yield return HostFromMenu();
            Assert.That(_hostSession.Seats.Single().DisplayName, Is.EqualTo("Annie"));
            Assert.That(_hostSession.Seats.Single().Color, Is.EqualTo(PlayerIdentitySources.ToPlayerColor(PlayerIdentitySources.Palette[6])));
        }
    }
}
