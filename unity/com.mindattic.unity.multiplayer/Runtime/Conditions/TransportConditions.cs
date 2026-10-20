// The same condition NGO's UnityTransport uses to build its simulator pipeline stage
// (UNITY_MP_TOOLS_NETSIM_IMPLEMENTATION_ENABLED there): where it is false the stage does not exist.
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && !UNITY_MP_TOOLS_NETSIM_DISABLED_IN_DEVELOP) || (!DEVELOPMENT_BUILD && UNITY_MP_TOOLS_NETSIM_ENABLED_IN_RELEASE)
#define MINDATTIC_NETSIM
#endif

using MindAttic.Unity.Multiplayer.Core;
using Unity.Netcode.Transports.UTP;
#if MINDATTIC_NETSIM
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
#endif

namespace MindAttic.Unity.Multiplayer
{
    /// <summary>
    /// Puts <see cref="NetConditions"/> on a running Unity Transport by retuning the simulator pipeline stage NGO adds to
    /// every pipeline in the editor and development builds. The stage sits under the reliable stage, so dropped reliable
    /// packets are resent; the parameters are read per packet, so a change applies to live connections at once.
    /// </summary>
    internal static class TransportConditions
    {
        /// <summary>True when this build has the simulator stage at all.</summary>
        public static bool IsSupported
        {
            get
            {
#if MINDATTIC_NETSIM
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Applies <paramref name="conditions"/> to <paramref name="transport"/>'s driver, which must already be created
        /// (the transport has started). Returns false, with why, when they could not be applied.
        /// </summary>
        public static bool TryApply(UnityTransport transport, NetConditions conditions, out string problem)
        {
#if MINDATTIC_NETSIM
            if (transport == null)
            {
                problem = "there is no Unity Transport";
                return false;
            }

            ref NetworkDriver driver = ref transport.GetNetworkDriver();
            if (!driver.IsCreated)
            {
                problem = "the transport has not started";
                return false;
            }

            NetworkSettings settings = driver.CurrentSettings;
            SimulatorUtility.Parameters parameters = settings.GetSimulatorStageParameters();
            if (parameters.MaxPacketCount <= 0)
            {
                problem = "the transport's driver has no simulator stage (a custom driver constructor?)";
                return false;
            }

            // Max packet count and size are fixed when the driver is made; everything else may change.
            parameters.Mode = ApplyMode.AllPackets;
            parameters.PacketDelayMs = conditions.LatencyMs;
            parameters.PacketJitterMs = conditions.JitterMs;
            parameters.PacketDropPercentage = conditions.PacketLossPercent;
            parameters.PacketDropInterval = 0;
            driver.ModifySimulatorStageParameters(parameters);
            problem = null;
            return true;
#else
            problem = "this build has no network simulator (only the editor and development builds do)";
            return false;
#endif
        }
    }
}
