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

        [Header("Launch")]
        [Tooltip("Start the session above as soon as the scene starts.")]
        [SerializeField] private bool autoStart;

        [Tooltip("A headless process (-batchmode, -nographics, a Dedicated Server build) starts as a dedicated server.")]
        [SerializeField] private bool serveWhenHeadless = true;

        [Tooltip("Read -mode -address -port -transport -tickrate -maxplayers from the command line.")]
        [SerializeField] private bool readCommandLine = true;

        [Tooltip("In a headless process: vsync off, targetFrameRate = tick rate, run in background.")]
        [SerializeField] private bool applyHeadlessSettings = true;

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

        /// <summary>Leaves the current session, if any.</summary>
        public bool Leave()
        {
            return Session != null && Session.Leave();
        }

        private void Awake()
        {
            Session = new NetSession(GetComponent<NetworkManager>());
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
                Session.Start(config);
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
