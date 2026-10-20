namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>Which service turns a session into a join code (RFC 0001).</summary>
    public enum JoinCodeProvider
    {
        /// <summary>
        /// The topology's default (<see cref="JoinCodeDefaults.ForPublishing"/>): Relay for a listen server (it gets
        /// through NAT), the rendezvous service for a dedicated server. A client tries whichever can resolve the code.
        /// </summary>
        Auto = 0,

        /// <summary>No join codes: players join by address only.</summary>
        None = 1,

        /// <summary>Unity Relay (needs com.unity.services.multiplayer, which defines MINDATTIC_UGS).</summary>
        Relay = 2,

        /// <summary>The self-hosted rendezvous service (<see cref="NetConfig.RendezvousUrl"/>).</summary>
        Rendezvous = 3,
    }

    /// <summary>The default-provider rule, engine-free so <c>dotnet test</c> covers it.</summary>
    public static class JoinCodeDefaults
    {
        /// <summary>
        /// The provider a Host or Server publishes its code with. <see cref="JoinCodeProvider.Auto"/> becomes Relay for a
        /// Host and Rendezvous for a Server; an explicit choice is kept. A Client publishes nothing (<see cref="JoinCodeProvider.None"/>).
        /// </summary>
        public static JoinCodeProvider ForPublishing(Topology topology, JoinCodeProvider configured)
        {
            if (topology == Topology.Client || configured == JoinCodeProvider.None)
            {
                return JoinCodeProvider.None;
            }

            if (configured != JoinCodeProvider.Auto)
            {
                return configured;
            }

            return topology == Topology.Host ? JoinCodeProvider.Relay : JoinCodeProvider.Rendezvous;
        }

        /// <summary>
        /// Whether a dedicated server should register itself with the rendezvous service as soon as it is running: it is a
        /// Server, its provider is (or defaults to) Rendezvous, and a rendezvous URL is configured.
        /// </summary>
        public static bool ShouldSelfRegister(NetConfig config)
        {
            return config != null
                && config.Topology == Topology.Server
                && ForPublishing(Topology.Server, config.JoinCodes) == JoinCodeProvider.Rendezvous
                && !string.IsNullOrWhiteSpace(config.RendezvousUrl);
        }
    }
}
