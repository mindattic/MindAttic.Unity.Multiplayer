using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MindAttic.Components.Unity;
using MindAttic.Components.Unity.Core;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Multiplayer.UI
{
    /// <summary>Where the player is in the front end's flow.</summary>
    public enum FrontEndState
    {
        /// <summary>On the main menu, no session.</summary>
        Menu = 0,

        /// <summary>Starting a game or joining one (the busy dialog is up).</summary>
        Connecting = 1,

        /// <summary>Hosting, in the lobby: the join code and the players, waiting for Start.</summary>
        Lobby = 2,

        /// <summary>Playing: the menus are hidden and Esc opens the in-game menu.</summary>
        InGame = 3,

        /// <summary>The connection was lost and the front end is getting the player's seat back.</summary>
        Reconnecting = 4,
    }

    /// <summary>Where <see cref="NetFrontEnd.AddScreen"/> puts the button that opens a game's own screen.</summary>
    [Flags]
    public enum MenuPlacement
    {
        /// <summary>The main menu, before Quit.</summary>
        MainMenu = 1,

        /// <summary>The in-game menu, before Leave.</summary>
        PauseMenu = 2,

        /// <summary>Both menus.</summary>
        Both = MainMenu | PauseMenu,
    }

    /// <summary>
    /// The drop-in multiplayer front end: add this one component beside a <see cref="NetBootstrap"/> (MindAttic ▸
    /// Multiplayer ▸ Add Front End does it) and the game has a main menu (Host, Join by IP, Join by code, Settings, Quit),
    /// a host lobby with the join code and the players, a connecting dialog that can be cancelled, a plain-words dialog
    /// for every reason a game can end, an automatic reconnect that gets a dropped player's seat back, and an in-game
    /// Esc menu (Resume, Invite, Settings, Leave). It is built entirely from MindAttic Components.
    /// </summary>
    /// <remarks>
    /// <para>The player's name and colour come from <see cref="Identity"/> (the MindAttic profile when Unity.Shared is
    /// installed, else PlayerPrefs) and are sent in the connection approval payload, so the server's seat carries them.</para>
    /// <para>A headless process (dedicated server, CI) and one launched into a role by <c>-mode</c> (a bot) never create
    /// any UI (<see cref="NetLaunch.ShowsMenus"/>); the session still runs and the events below still fire.</para>
    /// <para>Game hooks: <see cref="SessionStarted"/>, <see cref="GameStarted"/>, <see cref="Left"/> and
    /// <see cref="Reconnected"/>; <see cref="AddScreen"/>, <see cref="AddMainMenuItems"/>, <see cref="AddPauseMenuItems"/> and
    /// <see cref="AddSettingsItems"/> for the game's own options; <see cref="MenusVisible"/> to keep the menus out of the
    /// way. Every button also has a method (<see cref="Host"/>, <see cref="JoinByAddress"/>, <see cref="JoinByCode"/>,
    /// <see cref="StartGame"/>, <see cref="Leave"/>), so a game can drive the same flow from its own UI.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("MindAttic/Multiplayer/Net Front End")]
    public sealed class NetFrontEnd : MonoBehaviour
    {
        [Tooltip("The NetBootstrap whose session this front end drives. Empty: the one on this GameObject, else NetBootstrap.Instance.")]
        [SerializeField] private NetBootstrap bootstrap;

        [Tooltip("Optional: what the menus say and offer, and how a lost connection is retried. Empty uses the defaults.")]
        [SerializeField] private NetFrontEndSettings settings;

        /// <summary>PlayerPrefs key of the last address typed into Join by IP.</summary>
        public const string LastAddressKey = "MindAttic.Multiplayer.LastAddress";

        private const string LogPrefix = "[MindAttic.Multiplayer] ";

        private readonly List<Action<MenuScreen>> _mainItems = new List<Action<MenuScreen>>();
        private readonly List<Action<MenuScreen>> _pauseItems = new List<Action<MenuScreen>>();
        private readonly List<Action<MenuScreen>> _settingsItems = new List<Action<MenuScreen>>();
        private readonly Dictionary<MenuScreen, MaList> _rosters = new Dictionary<MenuScreen, MaList>();

        private NetSession _session;
        private Func<NetConfig> _config;
        private IPlayerIdentitySource _identity;
        private NetFrontEndSettings _settings;
        private ReconnectPolicy _policy;
        private Canvas _canvas;
        private MenuStack _main;
        private MenuStack _pause;
        private MenuScreen _mainScreen;
        private MenuScreen _pauseScreen;
        private MenuScreen _lobbyScreen;
        private MaButton _inviteButton;
        private MaButton _leaveButton;
        private MaCodeDisplay _lobbyCode;
        private MaModal _busy;
        private MaModal _alert;
        private FrontEndState _state;
        private bool _flow;
        private bool _cancelled;
        private bool _wasLive;
        private bool _menusVisible = true;
        private bool _subscribed;
        private Func<Task<bool>> _rejoin;
        private string _codeFailure;

        /// <summary>
        /// Test hook: true builds the menus even in a headless (batch mode) process, false never builds them; null follows
        /// <see cref="NetLaunch.ShowsMenus"/>.
        /// </summary>
        internal static bool? MenusOverride;

        /// <summary>The first enabled front end, for game code that has no reference to it.</summary>
        public static NetFrontEnd Instance { get; private set; }

        /// <summary>Raised when a session goes live from this front end (or from game code): a host is running, a client is in.</summary>
        public event Action<NetSession> SessionStarted;

        /// <summary>
        /// Raised when the menus hand over to the game: the host pressed Start (or there is no lobby), a client got in, or a
        /// dedicated server is running. Load the level or show the HUD here.
        /// </summary>
        public event Action GameStarted;

        /// <summary>
        /// Raised when the player is back at the main menu: they left, the game ended, they were removed, or reconnecting
        /// gave up. Carries why (<see cref="DisconnectReason.LocalLeave"/> for their own leave).
        /// </summary>
        public event Action<Disconnection> Left;

        /// <summary>Raised when a lost connection was restored and the player is back in their seat.</summary>
        public event Action Reconnected;

        /// <summary>Raised after <see cref="State"/> changes.</summary>
        public event Action<FrontEndState> StateChanged;

        /// <summary>The session this front end drives; null before Start (or when there is no NetBootstrap).</summary>
        public NetSession Session
        {
            get { return _session; }
        }

        /// <summary>Where the player is in the flow.</summary>
        public FrontEndState State
        {
            get { return _state; }
        }

        /// <summary>True when the menus were built: false in a headless process or a <c>-mode</c> launch.</summary>
        public bool HasMenus
        {
            get { return _main != null; }
        }

        /// <summary>The main menu stack (Host, Join, Settings, Quit; the lobby is pushed on it). Null when <see cref="HasMenus"/> is false.</summary>
        public MenuStack MainMenu
        {
            get { return _main; }
        }

        /// <summary>The in-game Esc menu. Null when <see cref="HasMenus"/> is false; inactive outside a game.</summary>
        public MenuStack PauseMenu
        {
            get { return _pause; }
        }

        /// <summary>The settings in use (the asset, or the defaults when none is assigned).</summary>
        public NetFrontEndSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = settings != null ? settings : CreateDefaultSettings();
                }

                return _settings;
            }
        }

        /// <summary>
        /// Where the player's name and colour come from and are saved to. Defaults to
        /// <see cref="PlayerIdentitySources.Default"/>; set your own before the player hosts or joins.
        /// </summary>
        public IPlayerIdentitySource Identity
        {
            get { return _identity ?? (_identity = PlayerIdentitySources.Default); }
            set
            {
                _identity = value;
                RefreshMainScreen();
            }
        }

        /// <summary>How a lost connection is retried (from <see cref="Settings"/>); change it before a session starts.</summary>
        public ReconnectPolicy Reconnect
        {
            get { return _policy ?? (_policy = Settings.ToReconnectPolicy()); }
            set { _policy = value; }
        }

        /// <summary>
        /// Whether the front end may show its menus. False closes the main menu and switches the in-game menu off (for a
        /// cutscene, or a game that shows its own title screen first); true brings back whichever fits the
        /// <see cref="State"/>. Dialogs still appear.
        /// </summary>
        public bool MenusVisible
        {
            get { return _menusVisible; }
            set
            {
                _menusVisible = value;
                if (_main == null)
                {
                    return;
                }

                if (!value)
                {
                    _main.Close();
                    ArmPauseMenu(false);
                }
                else if (_state == FrontEndState.InGame)
                {
                    ArmPauseMenu(true);
                }
                else
                {
                    _main.Open();
                }
            }
        }

        /// <summary>The last message shown (or logged, without menus) about why a session ended; null before any.</summary>
        public DisconnectMessage? LastMessage { get; private set; }

        // ---- Game hooks: the game's own menu items ---------------------------------------------------------------------

        /// <summary>
        /// Adds a button that opens a screen of the game's own (Options, Controls, Credits...) to the main menu, the in-game
        /// menu or both. <paramref name="build"/> fills a fresh screen each time it opens.
        /// </summary>
        /// <example><code>frontEnd.AddScreen("Options", s => s.AddRange("Volume", 0, 100, 80, SetVolume, true));</code></example>
        /// <returns>This component, so calls chain.</returns>
        public NetFrontEnd AddScreen(string label, Action<MenuScreen> build, MenuPlacement placement = MenuPlacement.Both, MaIcon icon = MaIcon.None)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("A screen needs a button label.", nameof(label));
            }

            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            if ((placement & MenuPlacement.MainMenu) != 0)
            {
                AddMainMenuItems(s => s.AddButton(label, () => PushOn(_main, label, build), ButtonVariant.Secondary, icon));
            }

            if ((placement & MenuPlacement.PauseMenu) != 0)
            {
                AddPauseMenuItems(s => s.AddButton(label, () => PushOn(_pause, label, build), ButtonVariant.Secondary, icon));
            }

            return this;
        }

        /// <summary>Adds the game's own items to the main menu, after Settings and before Quit. Rebuilds the menu if it is up.</summary>
        /// <returns>This component, so calls chain.</returns>
        public NetFrontEnd AddMainMenuItems(Action<MenuScreen> add)
        {
            _mainItems.Add(add ?? throw new ArgumentNullException(nameof(add)));
            RefreshMainScreen();
            return this;
        }

        /// <summary>
        /// Adds the game's own items to the in-game menu, before Leave. <paramref name="add"/> runs once, on the in-game
        /// menu's one screen (when the menus are built, or at once if they already are), and the items it adds stay for
        /// the front end's life, across games: refresh what they show from the screen's <c>Shown</c> event, which fires
        /// once each time the menu opens.
        /// </summary>
        /// <returns>This component, so calls chain.</returns>
        public NetFrontEnd AddPauseMenuItems(Action<MenuScreen> add)
        {
            _pauseItems.Add(add ?? throw new ArgumentNullException(nameof(add)));
            if (_pauseScreen != null)
            {
                RunPauseItem(add);
            }

            return this;
        }

        /// <summary>Adds the game's own settings below the name and colour on the Settings screen.</summary>
        /// <returns>This component, so calls chain.</returns>
        public NetFrontEnd AddSettingsItems(Action<MenuScreen> add)
        {
            _settingsItems.Add(add ?? throw new ArgumentNullException(nameof(add)));
            return this;
        }

        // ---- The flow, callable from game code too ------------------------------------------------------------------------

        /// <summary>Hosts a game (the Host button): with a join code when one can be published, else by address only.</summary>
        public void Host()
        {
            _ = HostAsync();
        }

        /// <summary>
        /// Hosts a listen server with the NetBootstrap's settings and the player's name and colour. A join code is published
        /// when the configured provider can (Relay installed, or a rendezvous URL); if it cannot, the game is hosted anyway
        /// and players join by address. Shows the lobby (or goes into the game) once the host is running.
        /// </summary>
        /// <returns>True when the host is running.</returns>
        public async Task<bool> HostAsync()
        {
            if (!ReadyToStart("host"))
            {
                return false;
            }

            BeginFlow(null);
            ShowBusy("Hosting", "Starting your game...");
            NetConfig config = BaseConfig();
            bool started = false;
            try
            {
                await WhenCanStart();
                IReadOnlyList<JoinCodeProvider> providers = JoinCodeProvidersFor(config);
                if (providers.Count > 0)
                {
                    // Each provider in turn (Relay, then the rendezvous URL, for the default Auto). An empty code means the
                    // host did not start, or started and left again when the code failed.
                    foreach (JoinCodeProvider provider in providers)
                    {
                        if (_cancelled || this == null)
                        {
                            break;
                        }

                        NetConfig attempt = config.Clone();
                        if (_session.JoinCodes == null)
                        {
                            attempt.JoinCodes = provider;
                        }

                        await WhenCanStart();
                        started = !(await _session.HostWithCode(attempt)).IsEmpty;
                        if (started)
                        {
                            break;
                        }
                    }

                    if (!started && !_cancelled && this != null)
                    {
                        Toast("No join code: " + (_codeFailure ?? "the join-code service did not answer")
                            + " Players can still join by IP.", ToastKind.Warning);
                        await WhenCanStart();
                        started = _session.StartHost(config);
                    }
                }
                else
                {
                    started = _session.StartHost(config);
                }

                if (started)
                {
                    await WhenSettled();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _codeFailure = ex.Message;
            }

            return FinishFlow(started, new DisconnectMessage("Couldn't host",
                "Your game could not start. Another program may be using port " + config.Port + ".", _codeFailure));
        }

        /// <summary>Joins the game at an address the player typed (the Join by IP screen): <c>host</c> or <c>host:port</c>.</summary>
        public void JoinByAddress(string address)
        {
            _ = JoinByAddressAsync(address);
        }

        /// <summary>
        /// Joins the game at <paramref name="address"/> (<c>192.168.1.20</c>, <c>192.168.1.20:7777</c>, <c>[::1]:7777</c>; the
        /// port defaults to the NetBootstrap's) with the player's name and colour, showing a connecting dialog that can be
        /// cancelled. A refusal is explained in a dialog.
        /// </summary>
        /// <returns>True when the player got in.</returns>
        public Task<bool> JoinByAddressAsync(string address)
        {
            if (!ReadyToStart("join"))
            {
                return Task.FromResult(false);
            }

            HostAddress parsed;
            string error;
            if (!HostAddress.TryParse(address, BaseConfig().Port, out parsed, out error))
            {
                ShowMessage(new DisconnectMessage("Check the address", error, null));
                return Task.FromResult(false);
            }

            PlayerPrefs.SetString(LastAddressKey, (address ?? string.Empty).Trim());
            PlayerPrefs.Save();
            return JoinAsync("Joining " + parsed + "...",
                () => Task.FromResult(_session.JoinByAddress(parsed.Host, parsed.Port, BaseConfig())));
        }

        /// <summary>Joins the game behind a join code the player typed (the Join by code screen).</summary>
        public void JoinByCode(string code)
        {
            _ = JoinByCodeAsync(code);
        }

        /// <summary>
        /// Joins the game behind <paramref name="code"/> (any case, with or without dashes) with the player's name and
        /// colour, showing a connecting dialog that can be cancelled.
        /// </summary>
        /// <returns>True when the player got in.</returns>
        public Task<bool> JoinByCodeAsync(string code)
        {
            if (!ReadyToStart("join"))
            {
                return Task.FromResult(false);
            }

            JoinCodeParseResult parsed;
            if (!JoinCode.TryParse(code, out parsed))
            {
                ShowMessage(new DisconnectMessage("Check the code", parsed.Message, null));
                return Task.FromResult(false);
            }

            string shown = JoinCodeFormat.Format(parsed.Code.Value);
            return JoinAsync("Looking up " + shown + "...", () => _session.JoinByCode(shown, BaseConfig()));
        }

        /// <summary>
        /// The host's Start (in the lobby): hands over to the game. When <see cref="NetFrontEndSettings.gameScene"/> is set,
        /// the server loads it for everyone first.
        /// </summary>
        public void StartGame()
        {
            if (_session == null || !_session.IsServer)
            {
                return;
            }

            string scene = Settings.gameScene;
            if (!string.IsNullOrWhiteSpace(scene))
            {
                SceneRequestResult result = _session.Scenes.Load(scene.Trim());
                if (result != SceneRequestResult.Started)
                {
                    Debug.LogWarning(LogPrefix + "The front end could not load '" + scene + "' for everyone: " + result + ".");
                    Toast("Could not load " + scene + " (" + result + ").", ToastKind.Error);
                }
            }

            EnterGame();
        }

        /// <summary>Leaves the session (a host ends it for everyone) and returns to the main menu.</summary>
        public void Leave()
        {
            if (_session == null)
            {
                return;
            }

            _cancelled = true;
            if (!_session.Leave() && _state != FrontEndState.Menu && !_flow)
            {
                ReturnToMenu(new Disconnection(DisconnectReason.LocalLeave, null), null);
            }
        }

        /// <summary>Asks before leaving (the Leave and End game buttons).</summary>
        public void ConfirmLeave()
        {
            bool host = _session != null && _session.IsServer;
            if (_main == null)
            {
                Leave();
                return;
            }

            MaModal.Confirm(host ? "End the game?" : "Leave the game?",
                host ? "Everyone in it will be disconnected." : "You can join again later.",
                ok =>
                {
                    if (ok)
                    {
                        Leave();
                    }
                },
                host ? "End game" : "Leave", "Stay", ButtonVariant.Danger);
        }

        /// <summary>Opens the Settings screen (name, colour and the game's settings) on whichever menu is up.</summary>
        public void OpenSettings()
        {
            MenuStack stack = _state == FrontEndState.InGame ? _pause : _main;
            PushOn(stack, "Settings", BuildSettings);
        }

        // ---- Unity lifecycle ---------------------------------------------------------------------------------------------

        /// <summary>Test hook: drive <paramref name="session"/> with <paramref name="config"/> instead of a NetBootstrap's (call before Start).</summary>
        internal void Bind(NetSession session, Func<NetConfig> config)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void Start()
        {
            if (_session == null)
            {
                NetBootstrap found = bootstrap != null ? bootstrap : GetComponent<NetBootstrap>();
                if (found == null)
                {
                    found = NetBootstrap.Instance;
                }

                if (found == null || found.Session == null)
                {
                    Debug.LogError(LogPrefix + "NetFrontEnd needs a NetBootstrap: run MindAttic > Multiplayer > Set Up Scene, or assign one.", this);
                    enabled = false;
                    return;
                }

                bootstrap = found;
                _session = found.Session;
                _config = found.BuildConfig;
            }

            Subscribe();
            bool menus = MenusOverride ?? NetLaunch.ShowsMenus(
                NetCommandLine.Apply(new NetConfig(), Environment.GetCommandLineArgs()), HeadlessBootstrap.IsHeadless);
            if (menus)
            {
                BuildMenus();
            }

            if (_session.IsLive)
            {
                // Auto Start, or game code, started the session before this ran.
                OnLive();
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            CloseBusy();
            if (_alert != null)
            {
                _alert.Close();
                _alert = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            _session.Started += OnSessionStarted;
            _session.Ended += OnSessionEnded;
            _session.JoinCodePublished += OnJoinCodePublished;
            _session.JoinCodeFailed += OnJoinCodeFailed;
            _session.Roster.Joined += OnPlayersChanged;
            _session.Roster.Left += OnPlayersChanged;
            _session.Roster.Changed += OnPlayerChanged;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _session == null)
            {
                return;
            }

            _subscribed = false;
            _session.Started -= OnSessionStarted;
            _session.Ended -= OnSessionEnded;
            _session.JoinCodePublished -= OnJoinCodePublished;
            _session.JoinCodeFailed -= OnJoinCodeFailed;
            _session.Roster.Joined -= OnPlayersChanged;
            _session.Roster.Left -= OnPlayersChanged;
            _session.Roster.Changed -= OnPlayerChanged;
        }

        // ---- Session events ------------------------------------------------------------------------------------------------

        private void OnSessionStarted()
        {
            if (!_flow)
            {
                OnLive();
            }
        }

        private void OnSessionEnded(Disconnection why)
        {
            bool wasLive = _wasLive;
            _wasLive = false;
            if (_flow || this == null)
            {
                return; // the host, join or reconnect flow in progress decides what happens next
            }

            if (_session.Topology == Topology.Client && _rejoin != null && !_cancelled && Reconnect.ShouldReconnect(why, wasLive))
            {
                _ = ReconnectAsync(why);
                return;
            }

            ReturnToMenu(why, _cancelled ? (DisconnectMessage?)null : MessageFor(why));
        }

        private void OnJoinCodePublished(JoinCode code)
        {
            if (_lobbyScreen != null)
            {
                FillLobby(_lobbyScreen, true);
            }

            if (_state == FrontEndState.InGame)
            {
                RefreshPauseScreen();
            }
        }

        private void OnJoinCodeFailed(string message)
        {
            _codeFailure = message;
        }

        /// <summary>
        /// A player's record changed. The rosters on screen follow (not for a round trip alone), and when it is this
        /// player's own name or colour that changed (an accepted <see cref="NetRoster.RequestChange"/>, from Settings or
        /// from game code), it is saved back to <see cref="Identity"/> (the MindAttic profile by default).
        /// </summary>
        private void OnPlayerChanged(NetPlayerChange change)
        {
            if (change.OnlyPingChanged)
            {
                return;
            }

            OnPlayersChanged(change.Player);
            if (change.Player.IsLocal && (change.NameChanged || change.ColorChanged))
            {
                SaveIdentity(change);
            }
        }

        private void SaveIdentity(NetPlayerChange change)
        {
            try
            {
                IPlayerIdentitySource source = Identity;
                string name = change.NameChanged ? change.Player.DisplayName : source.DisplayName;
                Color color = change.ColorChanged ? (Color)change.Player.Color.ToColor32() : source.Color;
                source.Save(name, color);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "Could not save the player's new name and colour: " + ex.Message);
            }

            RefreshMainScreen();
            if (_state == FrontEndState.InGame)
            {
                RefreshPauseScreen();
            }
        }

        private void OnPlayersChanged(NetPlayer player)
        {
            var stale = new List<MenuScreen>();
            foreach (KeyValuePair<MenuScreen, MaList> roster in _rosters)
            {
                if (roster.Key == null || roster.Value == null)
                {
                    stale.Add(roster.Key);
                }
                else
                {
                    FillRoster(roster.Value);
                }
            }

            foreach (MenuScreen screen in stale)
            {
                _rosters.Remove(screen);
            }
        }

        // ---- Flows ---------------------------------------------------------------------------------------------------------

        private bool ReadyToStart(string what)
        {
            if (_session == null)
            {
                Debug.LogWarning(LogPrefix + "NetFrontEnd cannot " + what + " before it has started.");
                return false;
            }

            if (_flow || _session.IsActive)
            {
                Debug.LogWarning(LogPrefix + "NetFrontEnd cannot " + what + " while a session is " + _session.State + "; leave first.");
                return false;
            }

            return true;
        }

        private void BeginFlow(Func<Task<bool>> rejoin)
        {
            _flow = true;
            _cancelled = false;
            _codeFailure = null;
            _rejoin = rejoin;
            SetState(FrontEndState.Connecting);
            ApplyIdentity();
        }

        private async Task<bool> JoinAsync(string message, Func<Task<bool>> join)
        {
            BeginFlow(join);
            ShowBusy("Joining", message);
            bool started = false;
            try
            {
                await WhenCanStart();
                started = await join();
                if (started)
                {
                    await WhenSettled();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _codeFailure = ex.Message;
            }

            return FinishFlow(started, new DisconnectMessage("Couldn't join", _codeFailure ?? "The game could not be reached.", null));
        }

        /// <summary>
        /// Ends a host or join flow: into the lobby or game when live, else back to the menu with why: the session's own
        /// reason when it <paramref name="started"/> and then ended, else <paramref name="notStarted"/>.
        /// </summary>
        private bool FinishFlow(bool started, DisconnectMessage notStarted)
        {
            _flow = false;
            if (this == null)
            {
                return false;
            }

            if (_cancelled && _session.IsActive)
            {
                _session.Leave(); // cancelled while the session was coming up
            }

            if (_session.IsLive)
            {
                CloseBusy();
                OnLive();
                return true;
            }

            if (_cancelled)
            {
                ReturnToMenu(new Disconnection(DisconnectReason.LocalLeave, null), null);
                return false;
            }

            Disconnection why = started ? _session.LastDisconnect : new Disconnection(DisconnectReason.ConnectFailed, notStarted.Body);
            ReturnToMenu(why, started && DisconnectText.ShouldShow(why) ? DisconnectText.For(why) : notStarted);
            return false;
        }

        private async Task ReconnectAsync(Disconnection lost)
        {
            _flow = true;
            _cancelled = false;
            SetState(FrontEndState.Reconnecting);
            ArmPauseMenu(false);
            ReconnectPolicy policy = Reconnect;
            int attempts = Math.Max(1, policy.MaxAttempts);
            ShowBusy("Reconnecting", "Connection lost. Getting your seat back...");
            MaProgressBar progress = _busy != null ? MaProgressBar.Create(_busy.Body, 0f) : null;
            Disconnection last = lost;
            Debug.Log(LogPrefix + "Connection lost (" + lost + "); reconnecting.");

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (_busy != null)
                {
                    _busy.SetMessage("Connection lost. Getting your seat back (try " + attempt + " of " + attempts + ")...");
                }

                if (progress != null)
                {
                    progress.Value = (attempt - 1f) / attempts;
                }

                await WhenElapsed((float)policy.DelayBefore(attempt));
                if (this == null)
                {
                    return;
                }

                if (_cancelled)
                {
                    break;
                }

                await WhenCanStart();
                bool started = false;
                try
                {
                    ApplyIdentity();
                    started = await _rejoin();
                    if (started)
                    {
                        await WhenSettled();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(LogPrefix + "Reconnect try " + attempt + " failed: " + ex.Message);
                }

                if (this == null)
                {
                    return;
                }

                if (_cancelled)
                {
                    if (_session.IsActive)
                    {
                        _session.Leave();
                    }

                    break;
                }

                if (_session.IsLive)
                {
                    _flow = false;
                    CloseBusy();
                    _wasLive = true;
                    Debug.Log(LogPrefix + "Reconnected on try " + attempt + ".");
                    Toast("Reconnected", ToastKind.Success);
                    EnterGame(false);
                    Raise(Reconnected);
                    return;
                }

                if (started && !ReconnectPolicy.IsRetryable(_session.LastDisconnect))
                {
                    last = _session.LastDisconnect;
                    break; // refused (full, banned...) or removed: final
                }
            }

            _flow = false;
            if (_cancelled)
            {
                ReturnToMenu(lost, null);
                return;
            }

            ReturnToMenu(last, last.Reason == lost.Reason && ReconnectPolicy.IsRetryable(last)
                ? new DisconnectMessage("Connection lost", "The connection to the game was lost and could not be restored after "
                    + attempts + (attempts == 1 ? " try." : " tries."), null)
                : MessageFor(last));
        }

        private void CancelFlow()
        {
            _cancelled = true;
            _busy = null; // MaModal closes itself after this
            if (_session != null && _session.IsActive)
            {
                _session.Leave();
            }
        }

        private void OnLive()
        {
            _wasLive = true;
            _cancelled = false;
            Raise(SessionStarted, _session);
            if (_session.IsServer)
            {
                if (Settings.showLobby && _main != null)
                {
                    ShowLobby();
                }
                else
                {
                    StartGame();
                }
            }
            else
            {
                if (_main != null)
                {
                    Toast("Joined as " + _session.Identity.DisplayName, ToastKind.Success);
                }

                EnterGame();
            }
        }

        private void EnterGame(bool raise = true)
        {
            SetState(FrontEndState.InGame);
            DestroyLobby();
            if (_main != null)
            {
                _main.PopToRoot();
                _main.Close();
            }

            ArmPauseMenu(true);
            if (raise)
            {
                Raise(GameStarted);
            }
        }

        private void ReturnToMenu(Disconnection why, DisconnectMessage? message)
        {
            CloseBusy();
            ArmPauseMenu(false);
            DestroyLobby();
            SetState(FrontEndState.Menu);
            if (_main != null)
            {
                RefreshMainScreen();
                _main.PopToRoot();
                if (_menusVisible)
                {
                    _main.Open();
                }
            }

            Raise(Left, why);
            if (message.HasValue)
            {
                ShowMessage(message.Value);
            }
        }

        private static DisconnectMessage? MessageFor(Disconnection why)
        {
            return DisconnectText.ShouldShow(why) ? DisconnectText.For(why) : (DisconnectMessage?)null;
        }

        /// <summary>
        /// The join-code providers Host tries, in order (UM-T63): the session's own service when one is set, else
        /// <see cref="JoinCodeDefaults.HostingOrder"/>, the rule <see cref="NetSession.HostWithCode"/> follows. Empty when
        /// none can publish a code, and the game is hosted for joining by address.
        /// </summary>
        private IReadOnlyList<JoinCodeProvider> JoinCodeProvidersFor(NetConfig config)
        {
            if (_session.JoinCodes != null)
            {
                return new[] { JoinCodeProvider.Rendezvous };
            }

            return JoinCodeDefaults.HostingOrder(config.JoinCodes, RelayJoinCodeService.IsInstalled, !string.IsNullOrWhiteSpace(config.RendezvousUrl));
        }

        private bool JoinByCodeOffered()
        {
            return Settings.joinByCode && (_session == null || _session.JoinCodes != null || BaseConfig().JoinCodes != JoinCodeProvider.None);
        }

        private NetConfig BaseConfig()
        {
            return _config != null ? _config() : new NetConfig();
        }

        private void ApplyIdentity()
        {
            try
            {
                IPlayerIdentitySource source = Identity;
                _session.Identity = _session.Identity.With(
                    PlayerIdentitySources.CleanName(source.DisplayName), PlayerIdentitySources.ToPlayerColor(source.Color));
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "Could not read the player's name and colour (" + ex.Message + "); playing as "
                    + _session.Identity.DisplayName + ".");
            }
        }

        private async Task WhenCanStart()
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (this != null && !_session.CanStart && Time.realtimeSinceStartup < until)
            {
                await Task.Yield();
            }
        }

        private async Task WhenSettled()
        {
            while (this != null && _session.IsActive && !_session.IsLive)
            {
                await Task.Yield();
            }
        }

        private async Task WhenElapsed(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (this != null && !_cancelled && Time.realtimeSinceStartup < until)
            {
                await Task.Yield();
            }
        }

        private void SetState(FrontEndState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            Raise(StateChanged, state);
        }

        // ---- Menus -----------------------------------------------------------------------------------------------------------

        private void BuildMenus()
        {
            NetFrontEndSettings s = Settings;
            if (s.theme != null)
            {
                MaTheme.Apply(s.theme);
            }

            _canvas = MaCanvas.Create("Multiplayer Menus", s.sortingOrder, transform);
            _main = MenuStack.Create(_canvas, s.dimBehindMenu);
            _main.name = "Main Menu";
            _mainScreen = _main.CreateScreen(Title());
            FillMainScreen();
            _main.Push(_mainScreen);

            _pause = MenuStack.Create(_canvas, true);
            _pause.name = "In-Game Menu";
            _pauseScreen = _pause.CreateScreen(s.pauseTitle);
            BuildPauseScreen(_pauseScreen);
            _pause.ConfigureAsPauseMenu(_pauseScreen);
            _pause.gameObject.SetActive(false);
            if (!_menusVisible)
            {
                _main.Close();
            }
        }

        private string Title()
        {
            return string.IsNullOrWhiteSpace(Settings.title) ? Application.productName : Settings.title;
        }

        private void RefreshMainScreen()
        {
            if (_mainScreen != null)
            {
                FillMainScreen();
            }
        }

        private void FillMainScreen()
        {
            MenuScreen s = _mainScreen;
            s.Clear();
            s.Title = Title();
            s.Subtitle = "Playing as " + SafeName();
            s.AddButton("Host", Host, ButtonVariant.Primary, MaIcon.Play);
            if (Settings.joinByAddress)
            {
                s.AddButton("Join by IP", OpenJoinByAddress);
            }

            if (JoinByCodeOffered())
            {
                s.AddButton("Join by code", OpenJoinByCode);
            }

            if (Settings.settingsScreen)
            {
                s.AddButton("Settings", OpenSettings, ButtonVariant.Secondary, MaIcon.Settings);
            }

            RunItems(_mainItems, s);
            if (Settings.quitButton && Application.platform != RuntimePlatform.WebGLPlayer)
            {
                s.AddButton("Quit", Quit, ButtonVariant.Danger, MaIcon.Exit);
            }
        }

        private void OpenJoinByAddress()
        {
            ushort port = BaseConfig().Port;
            PushOn(_main, "Join by IP", s =>
            {
                s.Subtitle = "The host's address. Add :port if it is not " + port + ".";
                MaTextbox box = s.AddTextbox("Address", "192.168.1.20:" + port, JoinByAddress);
                box.Text = PlayerPrefs.GetString(LastAddressKey, string.Empty);
                s.AddButton("Join", () => JoinByAddress(box.Text), ButtonVariant.Primary, MaIcon.Play);
            });
        }

        private void OpenJoinByCode()
        {
            PushOn(_main, "Join by code", s =>
            {
                s.Subtitle = "The code the host shared, like ABC-DEF-GHJ.";
                MaTextbox box = s.AddTextbox("Join code", "ABCDEFGHJ", JoinByCode).AsCode(JoinCodeFormat.MaxDataLength + 1);
                s.AddButton("Join", () => JoinByCode(box.Text), ButtonVariant.Primary, MaIcon.Play);
            });
        }

        private void BuildSettings(MenuScreen s)
        {
            IPlayerIdentitySource source = Identity;
            string storedIn;
            string name;
            Color color;
            try
            {
                storedIn = source.StoredIn;
                name = source.DisplayName;
                color = source.Color;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "Could not read the player's identity: " + ex.Message);
                storedIn = "this session";
                name = _session != null ? _session.Identity.DisplayName : PlayerIdentity.DefaultDisplayName;
                color = Color.white;
            }

            s.Subtitle = "Saved to " + storedIn + ".";
            MaTextbox nameBox = s.AddTextbox("Name", PlayerIdentity.DefaultDisplayName).WithCharacterLimit(ConnectionPayload.MaxDisplayNameLength);
            nameBox.Text = name;
            int initialColor = PlayerIdentitySources.NearestPaletteIndex(color);
            MaSelect colorSelect = s.AddSelect("Colour", PlayerIdentitySources.PaletteNames, initialColor);
            RunItems(_settingsItems, s);
            bool playing = _session != null && _session.IsLive && _session.IsClient;
            if (playing)
            {
                s.AddText("A new name or colour shows to everyone in the game at once.", TextStyle.Muted);
            }

            s.Hidden += () =>
            {
                Color picked = PlayerIdentitySources.Palette[Mathf.Clamp(colorSelect.Value, 0, PlayerIdentitySources.Palette.Length - 1)];
                if (playing && _session != null && _session.IsLive)
                {
                    // In a game: the server checks the change first; an accepted one is saved when it comes back (OnPlayerChanged).
                    _ = RequestIdentityChange(nameBox.Text, picked, name, colorSelect.Value != initialColor);
                    return;
                }

                try
                {
                    source.Save(nameBox.Text, picked);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(LogPrefix + "Could not save the player's name and colour: " + ex.Message);
                }

                RefreshMainScreen();
            };
        }

        /// <summary>
        /// Asks the server to change the player's name and colour mid-game (the Settings screen). Only what changed is
        /// asked for; a refusal is shown as a toast with the server's reason, and nothing is saved.
        /// </summary>
        private async Task RequestIdentityChange(string typed, Color picked, string oldName, bool colorChanged)
        {
            string name = PlayerIdentitySources.CleanName(typed);
            PlayerColor color = PlayerIdentitySources.ToPlayerColor(picked);
            bool nameChanged = name != PlayerIdentitySources.CleanName(oldName);
            if (!nameChanged && !colorChanged)
            {
                return;
            }

            PlayerChangeResult result;
            try
            {
                result = await _session.Roster.RequestChange(nameChanged ? name : null, colorChanged ? color : (PlayerColor?)null);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                return;
            }

            if (this == null)
            {
                return;
            }

            if (!result.Accepted)
            {
                Toast("Your name and colour were not changed: " + result.Reason, ToastKind.Warning);
            }
        }

        private void ShowLobby()
        {
            SetState(FrontEndState.Lobby);
            DestroyLobby();
            _lobbyScreen = _main.CreateScreen("Lobby");
            _lobbyScreen.BackHandler = () =>
            {
                ConfirmLeave();
                return true;
            };
            FillLobby(_lobbyScreen, true);
            _main.Push(_lobbyScreen);
            if (_menusVisible)
            {
                _main.Open();
            }
        }

        private void DestroyLobby()
        {
            if (_lobbyScreen == null)
            {
                return;
            }

            MenuScreen lobby = _lobbyScreen;
            _lobbyScreen = null;
            _lobbyCode = null;
            _rosters.Remove(lobby);
            if (_main != null && _main.Current == lobby)
            {
                _main.Pop();
            }

            Destroy(lobby.gameObject);
        }

        /// <summary>The join code (or the address to join at) and the live roster; the lobby adds Leave and Start.</summary>
        private void FillLobby(MenuScreen s, bool lobby)
        {
            s.Clear();
            string code = _session.PublishedCodeText;
            MaCodeDisplay codeDisplay = null;
            if (code.Length > 0)
            {
                s.Subtitle = lobby ? "Share the code. Press Start when everyone is in." : "Share the code to invite players.";
                codeDisplay = s.AddCode("Join code", code);
            }
            else
            {
                NetConfig config = _session.Config;
                s.Subtitle = "Players join by IP at " + LocalAddress() + ":" + (config != null ? config.Port : BaseConfig().Port) + ".";
            }

            s.AddSection("Players");
            MaList roster = s.AddList(6, "Waiting for players...");
            _rosters[s] = roster;
            FillRoster(roster);
            if (lobby)
            {
                _lobbyCode = codeDisplay;
                MaRow row = s.AddRow();
                row.AddButton("Leave", ConfirmLeave, ButtonVariant.Danger, MaIcon.Exit);
                row.AddButton("Start", StartGame, ButtonVariant.Primary, MaIcon.Play);
            }
        }

        /// <summary>The players, from the session's <see cref="NetRoster"/> (UM-T55): name, colour, Host tag, "you", "reconnecting...".</summary>
        private void FillRoster(MaList list)
        {
            list.Clear();
            foreach (NetPlayer player in _session.Roster.All)
            {
                string detail = !player.IsConnected ? "reconnecting..." : player.IsLocal ? "you" : null;
                MaListRow row = list.AddRow(player.DisplayName, player.Color.ToColor32(), detail);
                if (player.IsHost)
                {
                    row.AddTag("Host", TagTone.Accent);
                }
                else if (player.IsConnected && _session.IsServer)
                {
                    ulong clientId = player.ClientId;
                    string name = player.DisplayName;
                    row.AddAction(MaIcon.Close, () => ConfirmKick(clientId, name), "Remove " + name);
                }
            }
        }

        private void ConfirmKick(ulong clientId, string name)
        {
            MaModal.Confirm("Remove " + name + "?", name + " will be disconnected and told why.", ok =>
            {
                if (ok && _session != null)
                {
                    _session.Kick(clientId, Settings.kickMessage);
                }
            }, "Remove", "Cancel", ButtonVariant.Danger);
        }

        private void ArmPauseMenu(bool armed)
        {
            if (_pause == null)
            {
                return;
            }

            if (armed && Settings.pauseMenu && _menusVisible)
            {
                RefreshPauseScreen();
                _pause.gameObject.SetActive(true);
                _pause.Close();
            }
            else
            {
                _pause.PopToRoot();
                _pause.Close();
                _pause.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Builds the in-game menu's one screen (UM-T64): Resume, Invite players, Settings, the game's items, Leave. It is
        /// never cleared and refilled, so each game item callback runs once and whatever it hooks onto the screen (a
        /// <c>Shown</c> handler) is hooked once; <see cref="RefreshPauseScreen"/> only updates the built-in parts.
        /// </summary>
        private void BuildPauseScreen(MenuScreen s)
        {
            s.AddButton("Resume", () => _pause.Close(), ButtonVariant.Primary, MaIcon.Play);
            _inviteButton = s.AddButton("Invite players", () => PushOn(_pause, "Players", screen => FillLobby(screen, false)), ButtonVariant.Secondary, MaIcon.User);
            if (Settings.settingsScreen)
            {
                s.AddButton("Settings", OpenSettings, ButtonVariant.Secondary, MaIcon.Settings);
            }

            _leaveButton = s.AddButton("Leave game", ConfirmLeave, ButtonVariant.Danger, MaIcon.Exit);
            foreach (Action<MenuScreen> add in _pauseItems)
            {
                RunPauseItem(add);
            }
        }

        /// <summary>Runs one game item callback on the in-game menu's screen and keeps Leave last.</summary>
        private void RunPauseItem(Action<MenuScreen> add)
        {
            try
            {
                add(_pauseScreen);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            if (_leaveButton != null)
            {
                _leaveButton.transform.SetAsLastSibling();
            }

            _pauseScreen.MarkNavigationDirty();
        }

        /// <summary>Points the in-game menu at the current session: the subtitle, Invite for a host, End game or Leave game.</summary>
        private void RefreshPauseScreen()
        {
            if (_pauseScreen == null || _session == null)
            {
                return;
            }

            MenuScreen s = _pauseScreen;
            bool host = _session.IsServer;
            s.Title = Settings.pauseTitle;
            s.Subtitle = host && _session.PublishedCodeText.Length > 0
                ? "Hosting - join code " + _session.PublishedCodeText
                : host ? "Hosting" : "Playing as " + _session.Identity.DisplayName;
            if (_inviteButton != null)
            {
                _inviteButton.gameObject.SetActive(host);
            }

            if (_leaveButton != null)
            {
                _leaveButton.Label = host ? "End game" : "Leave game";
            }

            s.MarkNavigationDirty();
        }

        private void PushOn(MenuStack stack, string title, Action<MenuScreen> build)
        {
            if (stack == null)
            {
                return;
            }

            stack.Push(title, s =>
            {
                try
                {
                    build(s);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            });
        }

        private void RunItems(List<Action<MenuScreen>> items, MenuScreen s)
        {
            foreach (Action<MenuScreen> add in items)
            {
                try
                {
                    add(s);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }
        }

        private void ShowBusy(string title, string message)
        {
            CloseBusy();
            if (_main != null)
            {
                _busy = MaModal.Busy(title, message, CancelFlow);
            }
        }

        private void CloseBusy()
        {
            MaModal busy = _busy;
            _busy = null;
            if (busy != null && busy.IsOpen)
            {
                busy.OnCancel = null;
                busy.Close();
            }
        }

        private void ShowMessage(DisconnectMessage message)
        {
            LastMessage = message;
            Debug.Log(LogPrefix + message);
            if (_main == null)
            {
                return;
            }

            if (_alert != null && _alert.IsOpen)
            {
                _alert.Close();
            }

            MaModal modal = MaModal.Create(message.Title);
            modal.AddMessage(message.Body);
            if (message.Detail.Length > 0 && message.Detail != message.Body)
            {
                modal.AddMessage(message.Detail).Style = TextStyle.Muted;
            }

            modal.AddButton("OK", null, ButtonVariant.Primary);
            modal.Open();
            _alert = modal;
        }

        private void Toast(string message, ToastKind kind = ToastKind.Info)
        {
            if (_main != null)
            {
                MaToast.Show(message, kind);
            }
            else
            {
                Debug.Log(LogPrefix + message);
            }
        }

        private string SafeName()
        {
            try
            {
                return PlayerIdentitySources.CleanName(Identity.DisplayName);
            }
            catch (Exception)
            {
                return _session != null ? _session.Identity.DisplayName : PlayerIdentity.DefaultDisplayName;
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        /// <summary>This computer's first IPv4 LAN address, for players joining by IP.</summary>
        private static string LocalAddress()
        {
            try
            {
                foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // No name resolution: fall through.
            }

            return NetConfig.LoopbackAddress;
        }

        private static NetFrontEndSettings CreateDefaultSettings()
        {
            NetFrontEndSettings defaults = ScriptableObject.CreateInstance<NetFrontEndSettings>();
            defaults.name = "Default Front End Settings";
            defaults.hideFlags = HideFlags.DontSave;
            return defaults;
        }

        private void Raise(Action handler)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        private void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(value);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        // ---- Test access ---------------------------------------------------------------------------------------------------

        /// <summary>The main menu's root screen.</summary>
        internal MenuScreen MainScreen
        {
            get { return _mainScreen; }
        }

        /// <summary>The lobby screen while hosting (null otherwise).</summary>
        internal MenuScreen LobbyScreen
        {
            get { return _lobbyScreen; }
        }

        /// <summary>The lobby's join-code display (null when no code was published).</summary>
        internal MaCodeDisplay LobbyCode
        {
            get { return _lobbyCode; }
        }

        /// <summary>The lobby's roster.</summary>
        internal MaList LobbyRoster
        {
            get
            {
                MaList roster;
                return _lobbyScreen != null && _rosters.TryGetValue(_lobbyScreen, out roster) ? roster : null;
            }
        }

        /// <summary>The busy dialog while connecting or reconnecting.</summary>
        internal MaModal BusyDialog
        {
            get { return _busy; }
        }

        /// <summary>The last message dialog.</summary>
        internal MaModal MessageDialog
        {
            get { return _alert; }
        }
    }
}
