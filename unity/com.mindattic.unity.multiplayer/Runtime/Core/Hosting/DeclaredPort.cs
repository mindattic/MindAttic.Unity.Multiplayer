using System;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>The IP protocol a declared port is opened for.</summary>
    public enum PortProtocol
    {
        /// <summary>UDP (the game over Unity Transport's UDP).</summary>
        Udp = 0,

        /// <summary>TCP (the game over WebSocket, or HTTP such as the health endpoint).</summary>
        Tcp = 1,
    }

    /// <summary>
    /// One port a server package listens on, declared so a host knows what to open, map or probe: the game port, and
    /// the health endpoint's.
    /// </summary>
    public readonly struct DeclaredPort : IEquatable<DeclaredPort>
    {
        /// <summary>The game port's <see cref="Name"/>.</summary>
        public const string GameName = "game";

        /// <summary>The health endpoint port's <see cref="Name"/>.</summary>
        public const string HealthName = "health";

        /// <summary>Declares a port.</summary>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is 0.</exception>
        public DeclaredPort(string name, ushort port, PortProtocol protocol, string purpose)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A declared port needs a name.", nameof(name));
            }

            if (port == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "A declared port needs a fixed number.");
            }

            Name = name;
            Port = port;
            Protocol = protocol;
            Purpose = purpose ?? string.Empty;
        }

        /// <summary>A short name: <see cref="GameName"/>, <see cref="HealthName"/>, or a profile's own.</summary>
        public string Name { get; }

        /// <summary>The port number inside the server's machine or container.</summary>
        public ushort Port { get; }

        /// <summary>UDP or TCP.</summary>
        public PortProtocol Protocol { get; }

        /// <summary>What it is for, in a sentence.</summary>
        public string Purpose { get; }

        /// <summary><c>udp</c> or <c>tcp</c>, as Docker and firewalls write it.</summary>
        public string ProtocolName
        {
            get { return Protocol == PortProtocol.Udp ? "udp" : "tcp"; }
        }

        /// <inheritdoc />
        public bool Equals(DeclaredPort other)
        {
            return Name == other.Name && Port == other.Port && Protocol == other.Protocol && Purpose == other.Purpose;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is DeclaredPort other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ((Name ?? string.Empty).GetHashCode() * 397) ^ (Port << 1) ^ (int)Protocol;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name + " " + Port + "/" + ProtocolName;
        }
    }
}
