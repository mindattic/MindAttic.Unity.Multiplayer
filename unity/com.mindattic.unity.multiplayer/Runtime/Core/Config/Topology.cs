namespace Unity.Multiplayer.Core
{
    /// <summary>How this process takes part in a session.</summary>
    public enum Topology
    {
        /// <summary>Listen server: this process is the server and also a local player.</summary>
        Host = 0,

        /// <summary>Dedicated server: headless, no local player; everyone joins it.</summary>
        Server = 1,

        /// <summary>A player joining a host or a dedicated server.</summary>
        Client = 2,
    }

    /// <summary>The wire the session runs over.</summary>
    public enum TransportKind
    {
        /// <summary>Unity Transport over UDP. The default; needs a UDP-reachable port.</summary>
        Udp = 0,

        /// <summary>Unity Transport over WebSockets, for TCP/HTTP-only hosts (for example RunPod).</summary>
        WebSocket = 1,
    }

    /// <summary>Extension helpers for <see cref="Topology"/>.</summary>
    public static class TopologyExtensions
    {
        /// <summary>True when this process runs the authoritative server (Host or Server).</summary>
        public static bool RunsServer(this Topology topology)
        {
            return topology == Topology.Host || topology == Topology.Server;
        }

        /// <summary>True when this process has a local player (Host or Client).</summary>
        public static bool HasLocalPlayer(this Topology topology)
        {
            return topology == Topology.Host || topology == Topology.Client;
        }
    }
}
