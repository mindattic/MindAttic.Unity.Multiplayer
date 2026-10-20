using MindAttic.Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MindAttic.Unity.Multiplayer
{
    /// <summary>
    /// The one component a game adds to network a scene. It sits on the NetworkManager's GameObject, owns the
    /// <see cref="NetSession"/>, holds the default <see cref="NetConfig"/> in the Inspector, and at launch applies
    /// the headless settings and the command line and starts the session when it should (see <see cref="NetLaunch"/>).
    /// </summary>
    /// <remarks>
    /// <para>Launch behaviour: <c>-mode host|server|client</c> on the command line always starts that topology; a
    /// headless process (<c>-batchmode</c>, <c>-nographics</c>, a Dedicated Server build) starts as a dedicated server
    /// when <see cref="serveWhenHeadless"/> is on; otherwise the session starts only when <see cref="autoStart"/> is on,
    /// and UI calls <see cref="StartHost"/>, <see cref="StartServer"/>, <see cref="JoinByAddress"/> and <see cref="Leave"/>.</para>
    /// <para>A headless process is also paced to the tick rate (<see cref="HeadlessBootstrap.Apply"/>).</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(UnityTransport))]
    [AddComponentMenu("MindAttic/Multiplayer/Net Bootstrap")]
    public sealed class NetBootstrap : MonoBehaviour
    {
        [Header("Session")]
        [Tooltip("What this process does when it starts by itself (Auto Start) or from UI with Begin().")]
        [SerializeField] private Topology topology = Topology.Host;

        [Tooltip("Client: the server address to join. Host/Server: the local address to bind (empty = every interface).")]
        [SerializeField] private string address = string.Empty;

        [Tooltip("Client: the server's port. Host/Server: the port to listen on.")]
        [SerializeField, Range(1, 65535)] private int port = NetConfig.DefaultPort;

        [Tooltip("UDP, or WebSocket for hosts that only pass TCP/HTTP.")]
        [SerializeField] private TransportKind transport = TransportKind.Udp;

        [Tooltip("Network and server simulation ticks per second.")]
        [SerializeField, Range(1, NetConfig.MaxTickRate)] private int tickRate = NetConfig.DefaultTickRate;

        [Tooltip("Most players at once, the host's own player included.")]
        [SerializeField, Range(1, NetConfig.MaxMaxPlayers)] private int maxPlayers = NetConfig.DefaultMaxPlayers;

        [Tooltip("Version clients must match. Empty uses Application.version.")]
        [SerializeField] private string gameVersion = string.Empty;

        [Tooltip("Server: seconds a dropped player's seat and player object wait for it to reconnect. 0 frees them at once.")]
        [SerializeField, Range(0f, 600f)] private float reconnectGraceSeconds = (float)NetConfig.DefaultReconnectGraceSeconds;

        [Header("Join codes")]
        [Tooltip("Auto: Unity Relay for a listen server, the rendezvous service for a dedicated server. None turns join codes off.")]
        [SerializeField] private JoinCodeProvider joinCodes = JoinCodeProvider.Auto;

        [Tooltip("Base URL of the self-hosted rendezvous service. A dedicated server registers itself there; clients resolve codes with it. "
            + "-rendezvous or MINDATTIC_RENDEZVOUS_URL override it. The registration secret comes from MINDATTIC_RENDEZVOUS_SECRET, never the scene.")]
        [SerializeField] private string rendezvousUrl = string.Empty;

        [Tooltip("Server: the address players reach it at, when it differs from what it binds. Empty lets the rendezvous service use the address it sees.")]
        [SerializeField] private string publicAddress = string.Empty;

        [Tooltip("Server: the port players reach it at, when a host maps it. 0 = the listen port (or RunPod's RUNPOD_TCP_PORT_* mapping).")]
        [SerializeField, Range(0, 65535)] private int publicPort;

        [Header("Player")]
        [Tooltip("The name other players see. Empty uses \"Player\".")]
        [SerializeField] private string displayName = string.Empty;

        [Tooltip("The colour this player plays as.")]
        [SerializeField] private Color32 playerColor = new Color32(255, 255, 255, 255);

        [Tooltip("Keep this player's token in PlayerPrefs so a restarted game gets its seat back. -playertoken <token> on the command line overrides it.")]
        [SerializeField] private bool rememberPlayer = true;

        [Header("Prefabs")]
        [Tooltip("Prefabs this game spawns over the network, registered before any session starts.")]
        [SerializeField] private NetPrefabList[] prefabLists = new NetPrefabList[0];

        [Header("Launch")]
        [Tooltip("Start the session above as soon as the scene starts.")]
        [SerializeField] private bool autoStart;

        [Tooltip("A headless process (-batchmode, -nographics, a Dedicated Server build) starts as a dedicated server.")]
        [SerializeField] private bool serveWhenHeadless = true;

        [Tooltip("Read -mode -address -port -transport -tickrate -maxplayers -joincodes -rendezvous -publicaddress -publicport from the command line, and MINDATTIC_RENDEZVOUS_URL / _SECRET / MINDATTIC_PUBLIC_ADDRESS from the environment.")]
        [SerializeField] private bool readCommandLine = true;

        [Tooltip("In a headless process: vsync off, targetFrameRate = tick rate, run in background.")]
        [SerializeField] private bool applyHeadlessSettings = true;

        /// <summary>Command-line argument that sets the player token (for several clients on one machine, or bots).</summary>
        public const string PlayerTokenArg = "-playertoken";

        /// <summary>PlayerPrefs key prefix the remembered player token is stored under (suffixed per install folder).</summary>
        public const string PlayerTokenPrefsKey = "MindAttic.Multiplayer.PlayerToken";

        /// <summary>The first enabled NetBootstrap in the loaded scenes, for game code that has no reference to it.</summary>
        public static NetBootstrap Instance { get; private set; }

        /// <summary>The session this component owns. Set in Awake; null after the component is destroyed.</summary>
        public NetSession Session { get; private set; }

        /// <summary>
        /// The Inspector settings as a <see cref="NetConfig"/>, with the command line folded in when
        /// <see cref="readCommandLine"/> is on. A new object every call.
        /// </summary>
        public NetConfig BuildConfig()
        {
            var config = new NetConfig
            {
                Topology = topology,
                Address = address,
                Port = (ushort)Mathf.Clamp(port, 1, 65535),
                Transport = transport,
                TickRate = tickRate,
                MaxPlayers = maxPlayers,
                GameVersion = string.IsNullOrWhiteSpace(gameVersion) ? Application.version : gameVersion,
                ReconnectGraceSeconds = Mathf.Max(0f, reconnectGraceSeconds),
                JoinCodes = joinCodes,
                RendezvousUrl = string.IsNullOrWhiteSpace(rendezvousUrl) ? null : rendezvousUrl.Trim(),
                PublicAddress = string.IsNullOrWhiteSpace(publicAddress) ? null : publicAddress.Trim(),
                PublicPort = (ushort)Mathf.Clamp(publicPort, 0, 65535),
            };

            if (string.IsNullOrWhiteSpace(config.GameVersion))
            {
                config.GameVersion = "0";
            }

            if (readCommandLine)
            {
                HeadlessBootstrap.ReadCommandLine(config);
            }

            return config;
        }

        /// <summary>
        /// Replaces the Inspector session settings with <paramref name="config"/>'s (for code that sets the
        /// component up at runtime, before its Start runs).
        /// </summary>
        internal void SetDefaults(NetConfig config)
        {
            topology = config.Topology;
            address = config.Address ?? string.Empty;
            port = config.Port;
            transport = config.Transport;
            tickRate = config.TickRate;
            maxPlayers = config.MaxPlayers;
            gameVersion = config.GameVersion;
            reconnectGraceSeconds = (float)config.ReconnectGraceSeconds;
            joinCodes = config.JoinCodes;
            rendezvousUrl = config.RendezvousUrl ?? string.Empty;
            publicAddress = config.PublicAddress ?? string.Empty;
            publicPort = config.PublicPort;
        }

        /// <summary>
        /// The identity this component gives its session: the token from <c>-playertoken</c>, else the remembered one
        /// (created and stored on first use) when <see cref="rememberPlayer"/> is on, else a fresh one; with the
        /// Inspector's name and colour.
        /// </summary>
        public PlayerIdentity BuildIdentity()
        {
            string name = string.IsNullOrWhiteSpace(displayName) ? PlayerIdentity.DefaultDisplayName : displayName.Trim();
            var color = new PlayerColor(playerColor.r, playerColor.g, playerColor.b, playerColor.a);
            string token = CommandLineToken();
            if (token == null && rememberPlayer)
            {
                token = RememberedToken();
            }

            try
            {
                return new PlayerIdentity(token ?? PlayerIdentity.NewToken(), name, color);
            }
            catch (System.ArgumentException ex)
            {
                Debug.LogWarning("[MindAttic.Multiplayer] " + ex.Message + " Using a fresh identity.");
                return PlayerIdentity.CreateNew(null, color);
            }
        }

        private static string CommandLineToken()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], PlayerTokenArg, System.StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        /// <summary>
        /// The stored token, keyed by install folder so Multiplayer Play Mode's virtual players (each runs from its
        /// own folder) do not share one and take over each other's seats.
        /// </summary>
        private static string RememberedToken()
        {
            string key = PlayerTokenPrefsKey + "." + StableHash(Application.dataPath);
            string token = PlayerPrefs.GetString(key, string.Empty);
            if (token.Length == 0)
            {
                token = PlayerIdentity.NewToken();
                PlayerPrefs.SetString(key, token);
                PlayerPrefs.Save();
            }

            return token;
        }

        private static string StableHash(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text ?? string.Empty)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }

            return hash.ToString("x16");
        }

        /// <summary>Starts the configured topology (<see cref="BuildConfig"/>).</summary>
        public bool Begin()
        {
            return Session.Start(BuildConfig());
        }

        /// <summary>Hosts a listen server with the configured settings.</summary>
        public bool StartHost()
        {
            return Session.StartHost(BuildConfig());
        }

        /// <summary>Runs a dedicated server with the configured settings.</summary>
        public bool StartServer()
        {
            return Session.StartServer(BuildConfig());
        }

        /// <summary>Joins <paramref name="ip"/>:<paramref name="joinPort"/> with the configured transport and version.</summary>
        public bool JoinByAddress(string ip, ushort joinPort)
        {
            return Session.JoinByAddress(ip, joinPort, BuildConfig());
        }

        /// <summary>
        /// Hosts a listen server with the configured settings and publishes a join code (Unity Relay unless Join Codes
        /// says otherwise). The code is also in <see cref="NetSession.PublishedCode"/>; Empty when it failed
        /// (<see cref="NetSession.JoinCodeFailed"/> says why).
        /// </summary>
        public System.Threading.Tasks.Task<JoinCode> HostWithCode()
        {
            return Session.HostWithCode(BuildConfig());
        }

        /// <summary>Joins the game behind <paramref name="code"/> (as a player typed it) with the configured settings.</summary>
        public System.Threading.Tasks.Task<bool> JoinByCode(string code)
        {
            return Session.JoinByCode(code, BuildConfig());
        }

        /// <summary>Leaves the current session, if any.</summary>
        public bool Leave()
        {
            return Session != null && Session.Leave();
        }

        private void Awake()
        {
            Session = new NetSession(GetComponent<NetworkManager>());
            Session.Identity = BuildIdentity();
            if (prefabLists != null)
            {
                foreach (NetPrefabList list in prefabLists)
                {
                    if (list != null)
                    {
                        Session.Prefabs.Register(list);
                    }
                }
            }

            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void Start()
        {
            NetConfig config = BuildConfig();
            bool headless = HeadlessBootstrap.IsHeadless;
            if (headless && applyHeadlessSettings)
            {
                HeadlessBootstrap.Apply(config.TickRate);
            }

            NetCommandLineResult commandLine = readCommandLine
                ? NetCommandLine.Apply(new NetConfig(), System.Environment.GetCommandLineArgs())
                : new NetCommandLineResult(false, false, null);

            if (NetLaunch.ShouldStart(config, commandLine, headless, autoStart, serveWhenHeadless))
            {
                Debug.Log("[MindAttic.Multiplayer] Starting " + config);
                if (config.Topology == Topology.Host && config.JoinCodes == JoinCodeProvider.Relay)
                {
                    // An explicit Relay host needs its allocation before it starts.
                    _ = Session.HostWithCode(config);
                }
                else
                {
                    // A dedicated server with a rendezvous URL registers itself once it is running.
                    Session.Start(config);
                }
            }
        }

        private void OnDestroy()
        {
            if (Session != null)
            {
                Session.Dispose();
                Session = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
