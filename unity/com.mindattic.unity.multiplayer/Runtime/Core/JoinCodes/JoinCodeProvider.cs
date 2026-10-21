namespace Unity.Multiplayer.Core
{
    /// <summary>Which service turns a session into a join code (RFC 0001).</summary>
    public enum JoinCodeProvider
    {
        /// <summary>
        /// The topology's default (<see cref="JoinCodeDefaults.ForPublishing"/>): Relay for a listen server (it gets
        /// through NAT) when it is installed, else the rendezvous service when a URL is set
        /// (<see cref="JoinCodeDefaults.HostingOrder"/>); the rendezvous service for a dedicated server. A client tries
        /// whichever can resolve the code.
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
        /// The providers a listen server (Host) tries, in order, to publish a join code (UM-T63). An explicit choice is kept
        /// when it can work (Relay installed, a rendezvous URL set). <see cref="JoinCodeProvider.Auto"/> is Relay first when
        /// it is installed (it gets through NAT), then the rendezvous service when a URL is configured, so a game with only a
        /// rendezvous URL still gets a code. Empty when nothing can publish one (codes off, or nothing set up).
        /// </summary>
        /// <param name="configured">The configured provider (<see cref="NetConfig.JoinCodes"/>).</param>
        /// <param name="relayAvailable">Whether Unity Relay is installed (com.unity.services.multiplayer).</param>
        /// <param name="hasRendezvousUrl">Whether <see cref="NetConfig.RendezvousUrl"/> is set.</param>
        public static System.Collections.Generic.IReadOnlyList<JoinCodeProvider> HostingOrder(JoinCodeProvider configured, bool relayAvailable, bool hasRendezvousUrl)
        {
            var order = new System.Collections.Generic.List<JoinCodeProvider>(2);
            bool relay = configured == JoinCodeProvider.Relay || configured == JoinCodeProvider.Auto;
            bool rendezvous = configured == JoinCodeProvider.Rendezvous || configured == JoinCodeProvider.Auto;
            if (relay && relayAvailable)
            {
                order.Add(JoinCodeProvider.Relay);
            }

            if (rendezvous && hasRendezvousUrl)
            {
                order.Add(JoinCodeProvider.Rendezvous);
            }

            return order;
        }

        /// <summary>
        /// The one provider a listen server publishes with when it can only use one (<see cref="HostingOrder"/>'s first).
        /// When nothing is set up it is the configured provider (Relay for <see cref="JoinCodeProvider.Auto"/>), so the
        /// caller reports what is missing.
        /// </summary>
        public static JoinCodeProvider ForHosting(JoinCodeProvider configured, bool relayAvailable, bool hasRendezvousUrl)
        {
            if (configured == JoinCodeProvider.None)
            {
                return JoinCodeProvider.None;
            }

            System.Collections.Generic.IReadOnlyList<JoinCodeProvider> order = HostingOrder(configured, relayAvailable, hasRendezvousUrl);
            if (order.Count > 0)
            {
                return order[0];
            }

            return configured == JoinCodeProvider.Auto ? JoinCodeProvider.Relay : configured;
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
