using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Reads session settings from a process command line onto a <see cref="NetConfig"/>, so a
    /// headless server build is configured by its launch line:
    /// <c>Server.x86_64 -batchmode -nographics -mode server -port 7777 -transport websocket</c>.
    /// </summary>
    /// <remarks>
    /// <para>Recognised arguments (names ignore case; each takes its value as the next argument or after
    /// <c>=</c>, as in <c>-port=7777</c>):</para>
    /// <list type="table">
    /// <item><term><c>-mode</c></term><description><c>host</c>, <c>server</c> or <c>client</c> (<see cref="NetConfig.Topology"/>).</description></item>
    /// <item><term><c>-address</c> / <c>-ip</c></term><description>Server address to join, or local address to bind (<see cref="NetConfig.Address"/>).</description></item>
    /// <item><term><c>-port</c></term><description>1-65535 (<see cref="NetConfig.Port"/>).</description></item>
    /// <item><term><c>-transport</c></term><description><c>udp</c>, or <c>websocket</c> / <c>ws</c> (<see cref="NetConfig.Transport"/>).</description></item>
    /// <item><term><c>-tickrate</c></term><description>Ticks per second (<see cref="NetConfig.TickRate"/>).</description></item>
    /// <item><term><c>-maxplayers</c></term><description>Player cap (<see cref="NetConfig.MaxPlayers"/>).</description></item>
    /// <item><term><c>-name</c></term><description>The server's name (<see cref="NetConfig.ServerName"/>).</description></item>
    /// <item><term><c>-password</c></term><description>The server password (<see cref="NetConfig.Password"/>); <c>MINDATTIC_SERVER_PASSWORD</c> is safer, since a command line is visible to other processes.</description></item>
    /// <item><term><c>-joincodes</c></term><description><c>auto</c>, <c>none</c>, <c>relay</c> or <c>rendezvous</c> (<see cref="NetConfig.JoinCodes"/>).</description></item>
    /// <item><term><c>-rendezvous</c></term><description>Rendezvous service base URL (<see cref="NetConfig.RendezvousUrl"/>).</description></item>
    /// <item><term><c>-rendezvoussecret</c></term><description>Its registration secret (<see cref="NetConfig.RendezvousSecret"/>); the environment variable is safer, since a command line is visible to other processes.</description></item>
    /// <item><term><c>-publicaddress</c></term><description>Address players reach this server at (<see cref="NetConfig.PublicAddress"/>).</description></item>
    /// <item><term><c>-publicport</c></term><description>Port players reach this server at (<see cref="NetConfig.PublicPort"/>).</description></item>
    /// <item><term><c>-netsim</c></term><description>Simulated network conditions: <c>none</c>, <c>broadband</c>, <c>mobile4g</c>, <c>mobile3g</c>, <c>lossy</c> or <c>custom</c> (<see cref="NetConfig.Conditions"/>).</description></item>
    /// <item><term><c>-latency</c> / <c>-jitter</c> / <c>-loss</c></term><description>Simulated milliseconds of delay, milliseconds of jitter and percent of packets lost. Each switches to <see cref="NetConditionsProfile.Custom"/>, keeping the other numbers of the profile chosen so far (<see cref="NetConfig.UseCustomConditions"/>).</description></item>
    /// </list>
    /// <para>Anything else is ignored, because Unity and the OS put their own arguments on the same line.
    /// A recognised argument with a missing or unreadable value is reported in
    /// <see cref="NetCommandLineResult.Errors"/> and leaves that setting unchanged.</para>
    /// </remarks>
    public static class NetCommandLine
    {
        /// <summary>Selects the topology: host, server or client.</summary>
        public const string ModeArg = "-mode";

        /// <summary>Selects the address to join or bind.</summary>
        public const string AddressArg = "-address";

        /// <summary>Alias of <see cref="AddressArg"/>, matching Unity Transport's own <c>-ip</c>.</summary>
        public const string IpArg = "-ip";

        /// <summary>Selects the port.</summary>
        public const string PortArg = "-port";

        /// <summary>Selects the transport: udp or websocket.</summary>
        public const string TransportArg = "-transport";

        /// <summary>Selects the tick rate.</summary>
        public const string TickRateArg = "-tickrate";

        /// <summary>Selects the player cap.</summary>
        public const string MaxPlayersArg = "-maxplayers";

        /// <summary>Sets the server's name.</summary>
        public const string NameArg = "-name";

        /// <summary>Sets the server password.</summary>
        public const string PasswordArg = "-password";

        /// <summary>Selects the join-code provider: auto, none, relay or rendezvous.</summary>
        public const string JoinCodesArg = "-joincodes";

        /// <summary>Sets the rendezvous service URL.</summary>
        public const string RendezvousArg = "-rendezvous";

        /// <summary>Sets the rendezvous registration secret.</summary>
        public const string RendezvousSecretArg = "-rendezvoussecret";

        /// <summary>Sets the address players reach this server at.</summary>
        public const string PublicAddressArg = "-publicaddress";

        /// <summary>Sets the port players reach this server at.</summary>
        public const string PublicPortArg = "-publicport";

        /// <summary>Selects the simulated network conditions profile.</summary>
        public const string NetSimArg = "-netsim";

        /// <summary>Sets the simulated latency in milliseconds (custom conditions).</summary>
        public const string LatencyArg = "-latency";

        /// <summary>Sets the simulated jitter in milliseconds (custom conditions).</summary>
        public const string JitterArg = "-jitter";

        /// <summary>Sets the simulated packet loss in percent (custom conditions).</summary>
        public const string LossArg = "-loss";

        /// <summary>Unity's batch-mode switch; its presence marks a headless run.</summary>
        public const string BatchModeArg = "-batchmode";

        /// <summary>Unity's no-graphics switch; its presence marks a headless run.</summary>
        public const string NoGraphicsArg = "-nographics";

        /// <summary>
        /// Applies every recognised argument in <paramref name="args"/> to <paramref name="config"/>, in order
        /// (a later repeat wins). The first element is not skipped, so pass the arguments without the program
        /// path or with it; a path is never mistaken for an argument.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> or <paramref name="args"/> is null.</exception>
        public static NetCommandLineResult Apply(NetConfig config, IReadOnlyList<string> args)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            var errors = new List<string>();
            bool topologySpecified = false;
            bool headless = false;

            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg) || arg[0] != '-')
                {
                    continue;
                }

                string name = arg;
                string value = null;
                bool inlineValue = false;
                int equals = arg.IndexOf('=');
                if (equals > 0)
                {
                    name = arg.Substring(0, equals);
                    value = arg.Substring(equals + 1);
                    inlineValue = true;
                }

                if (Is(name, BatchModeArg) || Is(name, NoGraphicsArg))
                {
                    headless = true;
                    continue;
                }

                if (!IsRecognised(name))
                {
                    continue;
                }

                if (!inlineValue)
                {
                    // The value is the next argument, unless that is itself a switch (or there is none).
                    if (i + 1 < args.Count && !string.IsNullOrEmpty(args[i + 1]) && args[i + 1][0] != '-')
                    {
                        value = args[i + 1];
                        i++;
                    }
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(name + " needs a value.");
                    continue;
                }

                value = value.Trim();
                if (Is(name, ModeArg))
                {
                    Topology topology;
                    if (TryParseTopology(value, out topology))
                    {
                        config.Topology = topology;
                        topologySpecified = true;
                    }
                    else
                    {
                        errors.Add(ModeArg + " must be host, server or client; got '" + value + "'.");
                    }
                }
                else if (Is(name, AddressArg) || Is(name, IpArg))
                {
                    config.Address = value;
                }
                else if (Is(name, PortArg))
                {
                    ushort port;
                    if (ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port != 0)
                    {
                        config.Port = port;
                    }
                    else
                    {
                        errors.Add(PortArg + " must be a number from 1 to 65535; got '" + value + "'.");
                    }
                }
                else if (Is(name, TransportArg))
                {
                    TransportKind transport;
                    if (TryParseTransport(value, out transport))
                    {
                        config.Transport = transport;
                    }
                    else
                    {
                        errors.Add(TransportArg + " must be udp or websocket; got '" + value + "'.");
                    }
                }
                else if (Is(name, TickRateArg))
                {
                    int tickRate;
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out tickRate)
                        && tickRate >= 1 && tickRate <= NetConfig.MaxTickRate)
                    {
                        config.TickRate = tickRate;
                    }
                    else
                    {
                        errors.Add(TickRateArg + " must be a number from 1 to " + NetConfig.MaxTickRate + "; got '" + value + "'.");
                    }
                }
                else if (Is(name, JoinCodesArg))
                {
                    JoinCodeProvider provider;
                    if (TryParseJoinCodes(value, out provider))
                    {
                        config.JoinCodes = provider;
                    }
                    else
                    {
                        errors.Add(JoinCodesArg + " must be auto, none, relay or rendezvous; got '" + value + "'.");
                    }
                }
                else if (Is(name, RendezvousArg))
                {
                    if (RendezvousClient.IsValidBaseUrl(value))
                    {
                        config.RendezvousUrl = value;
                    }
                    else
                    {
                        errors.Add(RendezvousArg + " must be an absolute http:// or https:// URL; got '" + value + "'.");
                    }
                }
                else if (Is(name, RendezvousSecretArg))
                {
                    config.RendezvousSecret = value;
                }
                else if (Is(name, NameArg))
                {
                    config.ServerName = value;
                }
                else if (Is(name, PasswordArg))
                {
                    config.Password = value;
                }
                else if (Is(name, PublicAddressArg))
                {
                    config.PublicAddress = value;
                }
                else if (Is(name, PublicPortArg))
                {
                    ushort publicPort;
                    if (ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out publicPort) && publicPort != 0)
                    {
                        config.PublicPort = publicPort;
                    }
                    else
                    {
                        errors.Add(PublicPortArg + " must be a number from 1 to 65535; got '" + value + "'.");
                    }
                }
                else if (Is(name, NetSimArg))
                {
                    NetConditionsProfile profile;
                    if (NetConditions.TryParseProfile(value, out profile))
                    {
                        config.Conditions = profile;
                    }
                    else
                    {
                        errors.Add(NetSimArg + " must be none, broadband, mobile4g, mobile3g, lossy or custom; got '" + value + "'.");
                    }
                }
                else if (Is(name, LatencyArg))
                {
                    int latency;
                    if (TryParseInRange(value, 0, NetConditions.MaxLatencyMs, out latency))
                    {
                        config.UseCustomConditions();
                        config.SimulatedLatencyMs = latency;
                    }
                    else
                    {
                        errors.Add(LatencyArg + " must be a number of milliseconds from 0 to " + NetConditions.MaxLatencyMs + "; got '" + value + "'.");
                    }
                }
                else if (Is(name, JitterArg))
                {
                    int jitter;
                    if (TryParseInRange(value, 0, NetConditions.MaxLatencyMs, out jitter))
                    {
                        config.UseCustomConditions();
                        config.SimulatedJitterMs = jitter;
                    }
                    else
                    {
                        errors.Add(JitterArg + " must be a number of milliseconds from 0 to " + NetConditions.MaxLatencyMs + "; got '" + value + "'.");
                    }
                }
                else if (Is(name, LossArg))
                {
                    int loss;
                    if (TryParseInRange(value.TrimEnd('%'), 0, 100, out loss))
                    {
                        config.UseCustomConditions();
                        config.SimulatedPacketLossPercent = loss;
                    }
                    else
                    {
                        errors.Add(LossArg + " must be a percentage from 0 to 100; got '" + value + "'.");
                    }
                }
                else if (Is(name, MaxPlayersArg))
                {
                    int maxPlayers;
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out maxPlayers)
                        && maxPlayers >= 1 && maxPlayers <= NetConfig.MaxMaxPlayers)
                    {
                        config.MaxPlayers = maxPlayers;
                    }
                    else
                    {
                        errors.Add(MaxPlayersArg + " must be a number from 1 to " + NetConfig.MaxMaxPlayers + "; got '" + value + "'.");
                    }
                }
            }

            return new NetCommandLineResult(topologySpecified, headless, errors);
        }

        /// <summary>True when the arguments contain Unity's <c>-batchmode</c> or <c>-nographics</c> switch.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
        public static bool IsHeadless(IReadOnlyList<string> args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            for (int i = 0; i < args.Count; i++)
            {
                if (Is(args[i], BatchModeArg) || Is(args[i], NoGraphicsArg))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Parses host, server or client (ignoring case).</summary>
        public static bool TryParseTopology(string text, out Topology topology)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "host":
                    topology = Topology.Host;
                    return true;
                case "server":
                    topology = Topology.Server;
                    return true;
                case "client":
                    topology = Topology.Client;
                    return true;
                default:
                    topology = Topology.Host;
                    return false;
            }
        }

        /// <summary>Parses udp, websocket or ws (ignoring case).</summary>
        public static bool TryParseTransport(string text, out TransportKind transport)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "udp":
                    transport = TransportKind.Udp;
                    return true;
                case "websocket":
                case "websockets":
                case "ws":
                    transport = TransportKind.WebSocket;
                    return true;
                default:
                    transport = TransportKind.Udp;
                    return false;
            }
        }

        /// <summary>Parses auto, none (or off), relay or rendezvous (ignoring case).</summary>
        public static bool TryParseJoinCodes(string text, out JoinCodeProvider provider)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "auto":
                    provider = JoinCodeProvider.Auto;
                    return true;
                case "none":
                case "off":
                    provider = JoinCodeProvider.None;
                    return true;
                case "relay":
                    provider = JoinCodeProvider.Relay;
                    return true;
                case "rendezvous":
                    provider = JoinCodeProvider.Rendezvous;
                    return true;
                default:
                    provider = JoinCodeProvider.Auto;
                    return false;
            }
        }

        private static bool IsRecognised(string name)
        {
            return Is(name, ModeArg) || Is(name, AddressArg) || Is(name, IpArg) || Is(name, PortArg)
                || Is(name, TransportArg) || Is(name, TickRateArg) || Is(name, MaxPlayersArg)
                || Is(name, JoinCodesArg) || Is(name, RendezvousArg) || Is(name, RendezvousSecretArg)
                || Is(name, PublicAddressArg) || Is(name, PublicPortArg) || Is(name, NameArg) || Is(name, PasswordArg)
                || Is(name, NetSimArg) || Is(name, LatencyArg) || Is(name, JitterArg) || Is(name, LossArg);
        }

        private static bool TryParseInRange(string text, int min, int max, out int value)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }

        private static bool Is(string name, string expected)
        {
            return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>What <see cref="NetCommandLine.Apply"/> found.</summary>
    public sealed class NetCommandLineResult
    {
        /// <summary>Creates a result.</summary>
        public NetCommandLineResult(bool topologySpecified, bool headless, IReadOnlyList<string> errors)
        {
            TopologySpecified = topologySpecified;
            Headless = headless;
            Errors = errors ?? new string[0];
        }

        /// <summary>True when a valid <c>-mode</c> was given, so the launch line chose host, server or client.</summary>
        public bool TopologySpecified { get; }

        /// <summary>True when <c>-batchmode</c> or <c>-nographics</c> was on the line.</summary>
        public bool Headless { get; }

        /// <summary>One message per recognised argument whose value was missing or unreadable. Never null.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>True when <see cref="Errors"/> is empty.</summary>
        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }
}
