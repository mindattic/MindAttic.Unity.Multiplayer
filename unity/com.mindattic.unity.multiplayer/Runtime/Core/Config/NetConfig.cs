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
                + " at " + TickRate + " Hz, max " + MaxPlayers + " players, version " + GameVersion;
        }
    }
}
