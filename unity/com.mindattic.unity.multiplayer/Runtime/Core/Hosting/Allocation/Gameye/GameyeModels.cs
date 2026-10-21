using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>Tells Gameye about an image it may be asked to run (<see cref="IGameyeApiClient.RegisterImageAsync"/>).</summary>
    public sealed class GameyeImageRegistration
    {
        /// <summary>Describes an image registration.</summary>
        public GameyeImageRegistration(string imageName, string imageReference, string registryUsername, string registryPassword)
        {
            ImageName = imageName;
            ImageReference = imageReference;
            RegistryUsername = registryUsername;
            RegistryPassword = registryPassword;
        }

        /// <summary>The name Gameye tracks this image under (used by <see cref="GameyeStartSessionRequest.ImageName"/>).</summary>
        public string ImageName { get; }

        /// <summary>The GHCR image reference (<c>ghcr.io/org/game:tag</c>) Gameye pulls from.</summary>
        public string ImageReference { get; }

        /// <summary>A GHCR username with pull access, or null if the image is public.</summary>
        public string RegistryUsername { get; }

        /// <summary>A GHCR token with pull access, or null if the image is public.</summary>
        public string RegistryPassword { get; }
    }

    /// <summary>One declared port for a Gameye session, matched back by <see cref="Name"/> in a session's port mapping.</summary>
    public sealed class GameyePortSpec
    {
        /// <summary>Declares a port.</summary>
        public GameyePortSpec(string name, int port, TransportKind transport)
        {
            Name = name;
            Port = port;
            Transport = transport;
        }

        /// <summary>The port's name.</summary>
        public string Name { get; }

        /// <summary>The container's internal port.</summary>
        public int Port { get; }

        /// <summary>Udp or WebSocket (carried over TCP on Gameye's side).</summary>
        public TransportKind Transport { get; }
    }

    /// <summary>What to ask Gameye to start for one match (<see cref="IGameyeApiClient.StartSessionAsync"/>).</summary>
    public sealed class GameyeStartSessionRequest
    {
        /// <summary>Describes a session start request.</summary>
        public GameyeStartSessionRequest(
            string imageName,
            string location,
            string matchId,
            IReadOnlyList<GameyePortSpec> ports,
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            ImageName = imageName;
            Location = location;
            MatchId = matchId;
            Ports = ports;
            EnvironmentVariables = environmentVariables;
        }

        /// <summary>The image name from <see cref="GameyeImageRegistration.ImageName"/>.</summary>
        public string ImageName { get; }

        /// <summary>The Gameye location/fleet to start the session in.</summary>
        public string Location { get; }

        /// <summary>The match this session is for, tagged onto the session for Gameye's own logs.</summary>
        public string MatchId { get; }

        /// <summary>The ports the container exposes.</summary>
        public IReadOnlyList<GameyePortSpec> Ports { get; }

        /// <summary>Container environment variables (rendezvous URL/secret and the like).</summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }
    }

    /// <summary>One port Gameye mapped on a live session.</summary>
    public sealed class GameyePortMapping
    {
        /// <summary>Describes a mapped port.</summary>
        public GameyePortMapping(string name, int port, TransportKind transport)
        {
            Name = name;
            Port = port;
            Transport = transport;
        }

        /// <summary>The port's name, matching <see cref="GameyePortSpec.Name"/>.</summary>
        public string Name { get; }

        /// <summary>The public port players connect to.</summary>
        public int Port { get; }

        /// <summary>The transport this port speaks.</summary>
        public TransportKind Transport { get; }
    }

    /// <summary>A live (or still-starting) Gameye session.</summary>
    public sealed class GameyeSession
    {
        /// <summary>Describes a session.</summary>
        public GameyeSession(string sessionId, string ip, bool ready, IReadOnlyList<GameyePortMapping> ports)
        {
            SessionId = sessionId;
            Ip = ip;
            Ready = ready;
            Ports = ports;
        }

        /// <summary>Gameye's session id; pass it back to <see cref="IGameyeApiClient.StopSessionAsync"/>.</summary>
        public string SessionId { get; }

        /// <summary>The public IP players connect to; null while still starting.</summary>
        public string Ip { get; }

        /// <summary>True once Gameye reports the container running and its ports mapped.</summary>
        public bool Ready { get; }

        /// <summary>The mapped ports; empty while still starting.</summary>
        public IReadOnlyList<GameyePortMapping> Ports { get; }
    }
}
