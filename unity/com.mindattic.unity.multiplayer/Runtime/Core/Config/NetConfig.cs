using System;
using System.Collections.Generic;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// Everything a session needs to start: topology, endpoint, transport, tick rate, capacity and
    /// the game version clients must match. The topology is configuration, not code (BIBLE UM-§2):
    /// the same game runs as host, dedicated server or client by changing this object.
    /// </summary>
    /// <remarks>
    /// A plain mutable settings object so a bootstrap, a command line or a <c>server.json</c> can fill
    /// it field by field. Call <see cref="Validate"/> (or <see cref="EnsureValid"/>) before starting.
    /// </remarks>
    public sealed class NetConfig
    {
        /// <summary>Default simulation and network tick rate, in ticks per second.</summary>
        public const int DefaultTickRate = 30;

        /// <summary>Default seconds a dropped player's seat is held for them to reconnect.</summary>
        public const double DefaultReconnectGraceSeconds = 60.0;

        /// <summary>Longest accepted <see cref="ReconnectGraceSeconds"/> (one day).</summary>
        public const double MaxReconnectGraceSeconds = 86400.0;

        /// <summary>Default port (Unity Transport's conventional 7777).</summary>
        public const ushort DefaultPort = 7777;

        /// <summary>Default maximum number of players, the host's own player included.</summary>
        public const int DefaultMaxPlayers = 8;

        /// <summary>Highest tick rate accepted by <see cref="Validate"/>.</summary>
        public const int MaxTickRate = 128;

        /// <summary>Highest player count accepted by <see cref="Validate"/>.</summary>
        public const int MaxMaxPlayers = 1024;

        /// <summary>Address a Host or Server binds when <see cref="Address"/> is empty: every interface.</summary>
        public const string AnyAddress = "0.0.0.0";

        /// <summary>Address a Client connects to when <see cref="Address"/> is empty: this machine.</summary>
        public const string LoopbackAddress = "127.0.0.1";

        /// <summary>Whether this process hosts, serves or joins.</summary>
        public Topology Topology { get; set; } = Topology.Host;

        /// <summary>
        /// For a Client, the server address to connect to; for a Host or Server, the local address to bind.
        /// Empty or null means the default for the topology (see <see cref="EffectiveAddress"/>).
        /// </summary>
        public string Address { get; set; }

        /// <summary>For a Client, the server's port; for a Host or Server, the port to listen on.</summary>
        public ushort Port { get; set; } = DefaultPort;

        /// <summary>The transport to run over.</summary>
        public TransportKind Transport { get; set; } = TransportKind.Udp;

        /// <summary>Ticks per second for the network tick and the server simulation. Default 30.</summary>
        public int TickRate { get; set; } = DefaultTickRate;

        /// <summary>Maximum connected players, the host's own player included.</summary>
        public int MaxPlayers { get; set; } = DefaultMaxPlayers;

        /// <summary>The game's version string. Connection approval rejects a client whose version differs.</summary>
        public string GameVersion { get; set; } = "0";

        /// <summary>
        /// Server side: how long a dropped player's seat (and player object) waits for the same player token to
        /// reconnect before it is freed. Zero frees it at once.
        /// </summary>
        public double ReconnectGraceSeconds { get; set; } = DefaultReconnectGraceSeconds;

        /// <summary>
        /// Which service publishes and resolves join codes. <see cref="JoinCodeProvider.Auto"/> (the default) means Relay
        /// for a listen server and the rendezvous service for a dedicated server (<see cref="JoinCodeDefaults"/>).
        /// </summary>
        public JoinCodeProvider JoinCodes { get; set; } = JoinCodeProvider.Auto;

        /// <summary>
        /// Base URL of the self-hosted rendezvous service (for example <c>https://rendezvous.example.com</c>). A dedicated
        /// server with one registers itself as soon as it is running; a client resolves codes with it. Empty for none.
        /// </summary>
        public string RendezvousUrl { get; set; }

        /// <summary>
        /// The rendezvous service's registration secret, which a dedicated server needs to register. Prefer the
        /// <c>MINDATTIC_RENDEZVOUS_SECRET</c> environment variable (<see cref="NetEnvironment"/>) to keeping it in a scene.
        /// Never included in <see cref="ToString"/>.
        /// </summary>
        public string RendezvousSecret { get; set; }

        /// <summary>
        /// The address players reach this server at, when it differs from what it binds (behind NAT or a proxy). Empty
        /// lets the rendezvous service use the address the registration came from (<see cref="ServerAdvertisement"/>).
        /// </summary>
        public string PublicAddress { get; set; }

        /// <summary>
        /// The port players reach this server at, when a host maps it (0, the default, means <see cref="Port"/>, or the
        /// <c>RUNPOD_TCP_PORT_*</c> mapping on RunPod; see <see cref="ServerAdvertisement"/>).
        /// </summary>
        public ushort PublicPort { get; set; }

        /// <summary>
        /// Simulated network conditions (latency, jitter, packet loss) for this peer's link, for testing how the game plays
        /// on a bad connection. <see cref="NetConditionsProfile.None"/> (the default) simulates nothing;
        /// <see cref="NetConditionsProfile.Custom"/> uses <see cref="SimulatedLatencyMs"/>, <see cref="SimulatedJitterMs"/>
        /// and <see cref="SimulatedPacketLossPercent"/>. Editor and development builds only (see <see cref="NetConditions"/>).
        /// </summary>
        public NetConditionsProfile Conditions { get; set; } = NetConditionsProfile.None;

        /// <summary>With <see cref="NetConditionsProfile.Custom"/>: milliseconds added to each packet sent and received (<see cref="NetConditions.LatencyMs"/>).</summary>
        public int SimulatedLatencyMs { get; set; }

        /// <summary>With <see cref="NetConditionsProfile.Custom"/>: random variation of that delay, in milliseconds (<see cref="NetConditions.JitterMs"/>).</summary>
        public int SimulatedJitterMs { get; set; }

        /// <summary>With <see cref="NetConditionsProfile.Custom"/>: percentage of packets dropped each way (<see cref="NetConditions.PacketLossPercent"/>).</summary>
        public int SimulatedPacketLossPercent { get; set; }

        /// <summary>The conditions <see cref="Conditions"/> stands for: a named profile's numbers, or the custom ones.</summary>
        public NetConditions EffectiveConditions
        {
            get
            {
                return Conditions == NetConditionsProfile.Custom
                    ? new NetConditions(SimulatedLatencyMs, SimulatedJitterMs, SimulatedPacketLossPercent)
                    : NetConditions.For(Conditions);
            }
        }

        /// <summary>
        /// Switches <see cref="Conditions"/> to <see cref="NetConditionsProfile.Custom"/>, starting the custom numbers from
        /// the profile chosen so far, so one of them can be changed and the rest kept (<c>-netsim mobile4g -loss 10</c>).
        /// Does nothing when the profile is already Custom.
        /// </summary>
        public void UseCustomConditions()
        {
            if (Conditions == NetConditionsProfile.Custom)
            {
                return;
            }

            NetConditions start = EffectiveConditions;
            SimulatedLatencyMs = start.LatencyMs;
            SimulatedJitterMs = start.JitterMs;
            SimulatedPacketLossPercent = start.PacketLossPercent;
            Conditions = NetConditionsProfile.Custom;
        }

        /// <summary>The address actually used: <see cref="Address"/>, or the topology's default when it is empty.</summary>
        public string EffectiveAddress
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Address))
                {
                    return Address.Trim();
                }

                return Topology == Topology.Client ? LoopbackAddress : AnyAddress;
            }
        }

        /// <summary>Seconds per tick (1 / <see cref="TickRate"/>). Only meaningful once the config is valid.</summary>
        public double TickInterval
        {
            get { return TickRate > 0 ? 1.0 / TickRate : 0.0; }
        }

        /// <summary>Returns a field-by-field copy.</summary>
        public NetConfig Clone()
        {
            return (NetConfig)MemberwiseClone();
        }

        /// <summary>Lists every problem with this config; an empty list means it is valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (!Enum.IsDefined(typeof(Topology), Topology))
            {
                errors.Add("Topology " + (int)Topology + " is not Host, Server or Client.");
            }

            if (!Enum.IsDefined(typeof(TransportKind), Transport))
            {
                errors.Add("Transport " + (int)Transport + " is not Udp or WebSocket.");
            }

            if (Port == 0)
            {
                errors.Add("Port must be between 1 and 65535.");
            }

            if (TickRate < 1 || TickRate > MaxTickRate)
            {
                errors.Add("TickRate must be between 1 and " + MaxTickRate + "; got " + TickRate + ".");
            }

            if (MaxPlayers < 1 || MaxPlayers > MaxMaxPlayers)
            {
                errors.Add("MaxPlayers must be between 1 and " + MaxMaxPlayers + "; got " + MaxPlayers + ".");
            }

            if (string.IsNullOrWhiteSpace(GameVersion))
            {
                errors.Add("GameVersion must not be empty: connection approval compares it.");
            }

            if (double.IsNaN(ReconnectGraceSeconds) || ReconnectGraceSeconds < 0 || ReconnectGraceSeconds > MaxReconnectGraceSeconds)
            {
                errors.Add("ReconnectGraceSeconds must be between 0 and " + MaxReconnectGraceSeconds + "; got " + ReconnectGraceSeconds + ".");
            }

            if (!Enum.IsDefined(typeof(JoinCodeProvider), JoinCodes))
            {
                errors.Add("JoinCodes " + (int)JoinCodes + " is not Auto, None, Relay or Rendezvous.");
            }

            if (!string.IsNullOrWhiteSpace(RendezvousUrl) && !RendezvousClient.IsValidBaseUrl(RendezvousUrl))
            {
                errors.Add("RendezvousUrl must be an absolute http:// or https:// URL; got '" + RendezvousUrl + "'.");
            }

            if (!string.IsNullOrWhiteSpace(PublicAddress) && PublicAddress.Trim().IndexOf(' ') >= 0)
            {
                errors.Add("PublicAddress '" + PublicAddress + "' must not contain spaces.");
            }

            if (!Enum.IsDefined(typeof(NetConditionsProfile), Conditions))
            {
                errors.Add("Conditions " + (int)Conditions + " is not None, Broadband, Mobile4G, Mobile3G, Lossy or Custom.");
            }
            else if (Conditions == NetConditionsProfile.Custom)
            {
                errors.AddRange(EffectiveConditions.Validate());
            }

            string address = EffectiveAddress;
            if (address.IndexOf(' ') >= 0)
            {
                errors.Add("Address '" + address + "' must not contain spaces.");
            }

            if (Topology == Topology.Client && (address == AnyAddress || address == "::"))
            {
                errors.Add("A Client cannot connect to the wildcard address '" + address + "'; give the server's address.");
            }

            return errors;
        }

        /// <summary>True when <see cref="Validate"/> finds no problems.</summary>
        public bool IsValid
        {
            get { return Validate().Count == 0; }
        }

        /// <summary>Throws <see cref="ArgumentException"/> listing every problem if the config is invalid.</summary>
        public void EnsureValid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
            {
                throw new ArgumentException("Invalid NetConfig: " + string.Join(" ", errors));
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Topology + " " + EffectiveAddress + ":" + Port + " over " + Transport
                + " at " + TickRate + " Hz, max " + MaxPlayers + " players, version " + GameVersion
                + (Conditions == NetConditionsProfile.None ? string.Empty : ", simulating " + Conditions + " (" + EffectiveConditions + ")");
        }
    }
}
