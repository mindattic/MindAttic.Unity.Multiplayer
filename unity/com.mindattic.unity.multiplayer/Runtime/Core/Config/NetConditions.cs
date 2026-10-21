using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A named set of simulated network conditions (<see cref="NetConditions"/>) a session can run under, chosen in
    /// <see cref="NetConfig.Conditions"/> or with <c>-netsim</c>. For development: simulation only runs in the editor and
    /// in development builds (see <see cref="NetConditions"/>).
    /// </summary>
    public enum NetConditionsProfile
    {
        /// <summary>No simulation: the real network as it is (the default).</summary>
        None = 0,

        /// <summary>A good home connection: 20 ms each way, 5 ms jitter, no loss.</summary>
        Broadband = 1,

        /// <summary>A decent mobile connection: 60 ms each way, 15 ms jitter, 1% loss.</summary>
        Mobile4G = 2,

        /// <summary>A poor mobile connection: 200 ms each way, 50 ms jitter, 3% loss.</summary>
        Mobile3G = 3,

        /// <summary>The package's own test profile: 150 ms each way, 10 ms jitter, 5% loss (the lossy PlayMode suite runs under it).</summary>
        Lossy = 4,

        /// <summary>
        /// The numbers in <see cref="NetConfig.SimulatedLatencyMs"/>, <see cref="NetConfig.SimulatedJitterMs"/> and
        /// <see cref="NetConfig.SimulatedPacketLossPercent"/>.
        /// </summary>
        Custom = 5,
    }

    /// <summary>
    /// Simulated latency, jitter and packet loss for the link between this peer and the others, applied by Unity
    /// Transport's simulator pipeline stage.
    /// </summary>
    /// <remarks>
    /// <para>Every value describes this peer's own link: each packet it sends is delayed by
    /// <see cref="LatencyMs"/> (plus or minus up to <see cref="JitterMs"/>) and dropped with a chance of
    /// <see cref="PacketLossPercent"/>, and so is each packet it receives. A client on the
    /// <see cref="NetConditionsProfile.Lossy"/> profile therefore sees about 300 ms more round trip and loses about 5%
    /// of the packets each way. Set it on the clients to test what a player on a bad link sees, or on the server to put
    /// every player there; set on both ends, the effects add up.</para>
    /// <para>Loss happens below Unity Transport's reliability, so reliable traffic (RPCs by default, synced fields,
    /// spawns, connection approval) still arrives, later; unreliable RPCs are really lost.</para>
    /// <para>The simulator stage exists only where Netcode for GameObjects builds it: the editor and development
    /// builds (unless <c>UNITY_MP_TOOLS_NETSIM_DISABLED_IN_DEVELOP</c> is defined) and release builds that define
    /// <c>UNITY_MP_TOOLS_NETSIM_ENABLED_IN_RELEASE</c>. Elsewhere conditions are ignored, with a warning. Multiplayer
    /// Tools' Network Simulator component drives the same stage, so use one or the other on a given peer.</para>
    /// </remarks>
    public struct NetConditions : IEquatable<NetConditions>
    {
        /// <summary>Longest accepted <see cref="LatencyMs"/>.</summary>
        public const int MaxLatencyMs = 5000;

        /// <summary>Creates a set of conditions. Check it with <see cref="Validate"/>.</summary>
        public NetConditions(int latencyMs, int jitterMs, int packetLossPercent)
        {
            LatencyMs = latencyMs;
            JitterMs = jitterMs;
            PacketLossPercent = packetLossPercent;
        }

        /// <summary>No simulation.</summary>
        public static NetConditions None
        {
            get { return default(NetConditions); }
        }

        /// <summary>Milliseconds added to every packet this peer sends and every one it receives (0 to <see cref="MaxLatencyMs"/>).</summary>
        public int LatencyMs { get; }

        /// <summary>
        /// Up to this many milliseconds more or less than <see cref="LatencyMs"/>, at random per packet (0 to
        /// <see cref="LatencyMs"/>). Packets can arrive out of order; Unity Transport's sequenced pipelines put them back.
        /// </summary>
        public int JitterMs { get; }

        /// <summary>Percentage of packets dropped, in each direction (0 to 100).</summary>
        public int PacketLossPercent { get; }

        /// <summary>True when nothing is simulated.</summary>
        public bool IsNone
        {
            get { return LatencyMs == 0 && JitterMs == 0 && PacketLossPercent == 0; }
        }

        /// <summary>The conditions a named profile stands for. <see cref="NetConditionsProfile.Custom"/> (which has no numbers of its own) and unknown values give <see cref="None"/>.</summary>
        public static NetConditions For(NetConditionsProfile profile)
        {
            switch (profile)
            {
                case NetConditionsProfile.Broadband:
                    return new NetConditions(20, 5, 0);
                case NetConditionsProfile.Mobile4G:
                    return new NetConditions(60, 15, 1);
                case NetConditionsProfile.Mobile3G:
                    return new NetConditions(200, 50, 3);
                case NetConditionsProfile.Lossy:
                    return new NetConditions(150, 10, 5);
                default:
                    return None;
            }
        }

        /// <summary>Lists every problem with these numbers; an empty list means they are usable.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (LatencyMs < 0 || LatencyMs > MaxLatencyMs)
            {
                errors.Add("Simulated latency must be between 0 and " + MaxLatencyMs + " ms; got " + LatencyMs + ".");
            }

            if (JitterMs < 0 || JitterMs > Math.Max(0, LatencyMs))
            {
                errors.Add("Simulated jitter must be between 0 ms and the latency (" + LatencyMs + " ms); got " + JitterMs + ".");
            }

            if (PacketLossPercent < 0 || PacketLossPercent > 100)
            {
                errors.Add("Simulated packet loss must be between 0 and 100 percent; got " + PacketLossPercent + ".");
            }

            return errors;
        }

        /// <summary>True when <see cref="Validate"/> finds no problems.</summary>
        public bool IsValid
        {
            get { return Validate().Count == 0; }
        }

        /// <inheritdoc />
        public bool Equals(NetConditions other)
        {
            return LatencyMs == other.LatencyMs && JitterMs == other.JitterMs && PacketLossPercent == other.PacketLossPercent;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is NetConditions && Equals((NetConditions)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (LatencyMs * 397 ^ JitterMs) * 397 ^ PacketLossPercent;
            }
        }

        /// <summary>Equality by value.</summary>
        public static bool operator ==(NetConditions left, NetConditions right)
        {
            return left.Equals(right);
        }

        /// <summary>Inequality by value.</summary>
        public static bool operator !=(NetConditions left, NetConditions right)
        {
            return !left.Equals(right);
        }

        /// <summary>For example <c>150 ms ±10 ms, 5% loss</c>, or <c>no simulation</c>.</summary>
        public override string ToString()
        {
            if (IsNone)
            {
                return "no simulation";
            }

            return LatencyMs.ToString(CultureInfo.InvariantCulture) + " ms ±" + JitterMs.ToString(CultureInfo.InvariantCulture)
                + " ms, " + PacketLossPercent.ToString(CultureInfo.InvariantCulture) + "% loss";
        }

        /// <summary>
        /// Parses a profile name, ignoring case: <c>none</c> (or <c>off</c>), <c>broadband</c>, <c>mobile4g</c> (or
        /// <c>4g</c>), <c>mobile3g</c> (or <c>3g</c>), <c>lossy</c> or <c>custom</c>.
        /// </summary>
        public static bool TryParseProfile(string text, out NetConditionsProfile profile)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "none":
                case "off":
                    profile = NetConditionsProfile.None;
                    return true;
                case "broadband":
                    profile = NetConditionsProfile.Broadband;
                    return true;
                case "mobile4g":
                case "4g":
                    profile = NetConditionsProfile.Mobile4G;
                    return true;
                case "mobile3g":
                case "3g":
                    profile = NetConditionsProfile.Mobile3G;
                    return true;
                case "lossy":
                    profile = NetConditionsProfile.Lossy;
                    return true;
                case "custom":
                    profile = NetConditionsProfile.Custom;
                    return true;
                default:
                    profile = NetConditionsProfile.None;
                    return false;
            }
        }
    }
}
