using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>What a join code points at.</summary>
    public enum JoinTargetKind
    {
        /// <summary>A server reachable at an address and port (a dedicated server, or a host on the LAN).</summary>
        Endpoint = 0,

        /// <summary>A relay allocation, joined through the relay provider with its own join code (Unity Relay).</summary>
        Relay = 1,
    }

    /// <summary>
    /// Where a join code leads: an address and port, or a relay allocation. A host or server hands one
    /// to <see cref="IJoinCodeService.PublishAsync"/>; a client gets it back from
    /// <see cref="IJoinCodeService.ResolveAsync"/> and connects with it.
    /// </summary>
    /// <remarks>Immutable. Build one with <see cref="ForEndpoint"/> or <see cref="ForRelay"/>.</remarks>
    public sealed class JoinTarget : IEquatable<JoinTarget>
    {
        private JoinTarget(JoinTargetKind kind, string address, ushort port, string relayJoinCode, TransportKind transport)
        {
            Kind = kind;
            Address = address;
            Port = port;
            RelayJoinCode = relayJoinCode;
            Transport = transport;
        }

        /// <summary>Endpoint or relay.</summary>
        public JoinTargetKind Kind { get; }

        /// <summary>The server's address (host name or IP) for an <see cref="JoinTargetKind.Endpoint"/>; null for a relay.</summary>
        public string Address { get; }

        /// <summary>The server's port for an <see cref="JoinTargetKind.Endpoint"/>; 0 for a relay.</summary>
        public ushort Port { get; }

        /// <summary>The relay provider's own join code for a <see cref="JoinTargetKind.Relay"/>; null for an endpoint.</summary>
        public string RelayJoinCode { get; }

        /// <summary>The transport the client must connect with.</summary>
        public TransportKind Transport { get; }

        /// <summary>A server at <paramref name="address"/>:<paramref name="port"/>.</summary>
        /// <exception cref="ArgumentException">The address is empty, a wildcard or contains whitespace, or the port is 0.</exception>
        public static JoinTarget ForEndpoint(string address, ushort port, TransportKind transport = TransportKind.Udp)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("An endpoint needs an address.", nameof(address));
            }

            string trimmed = address.Trim();
            if (ContainsWhiteSpace(trimmed))
            {
                throw new ArgumentException("Address '" + trimmed + "' must not contain whitespace.", nameof(address));
            }

            if (trimmed == NetConfig.AnyAddress || trimmed == "::")
            {
                throw new ArgumentException(
                    "'" + trimmed + "' is a bind wildcard, not an address a client can reach; publish the server's public address.",
                    nameof(address));
            }

            if (port == 0)
            {
                throw new ArgumentException("An endpoint needs a port between 1 and 65535.", nameof(port));
            }

            CheckTransport(transport);
            return new JoinTarget(JoinTargetKind.Endpoint, trimmed, port, null, transport);
        }

        /// <summary>A relay allocation the client joins with the provider's <paramref name="relayJoinCode"/>.</summary>
        /// <exception cref="ArgumentException">The relay join code is empty or contains whitespace.</exception>
        public static JoinTarget ForRelay(string relayJoinCode, TransportKind transport = TransportKind.Udp)
        {
            if (string.IsNullOrWhiteSpace(relayJoinCode))
            {
                throw new ArgumentException("A relay target needs the relay's join code.", nameof(relayJoinCode));
            }

            string trimmed = relayJoinCode.Trim();
            if (ContainsWhiteSpace(trimmed))
            {
                throw new ArgumentException("Relay join code '" + trimmed + "' must not contain whitespace.", nameof(relayJoinCode));
            }

            CheckTransport(transport);
            return new JoinTarget(JoinTargetKind.Relay, null, 0, trimmed, transport);
        }

        /// <inheritdoc />
        public bool Equals(JoinTarget other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return Kind == other.Kind
                && Port == other.Port
                && Transport == other.Transport
                && string.Equals(Address, other.Address, StringComparison.OrdinalIgnoreCase)
                && string.Equals(RelayJoinCode, other.RelayJoinCode, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as JoinTarget);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = hash * 397 ^ Port;
                hash = hash * 397 ^ (int)Transport;
                hash = hash * 397 ^ (Address == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Address));
                hash = hash * 397 ^ (RelayJoinCode == null ? 0 : StringComparer.Ordinal.GetHashCode(RelayJoinCode));
                return hash;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Kind == JoinTargetKind.Endpoint
                ? Address + ":" + Port + " over " + Transport
                : "relay " + RelayJoinCode + " over " + Transport;
        }

        private static bool ContainsWhiteSpace(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CheckTransport(TransportKind transport)
        {
            if (!Enum.IsDefined(typeof(TransportKind), transport))
            {
                throw new ArgumentException("Transport " + (int)transport + " is not Udp or WebSocket.", nameof(transport));
            }
        }
    }
}
