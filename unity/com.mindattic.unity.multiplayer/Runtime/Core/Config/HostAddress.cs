using System;
using System.Globalization;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A server address as a player types it into a "Join by IP" box: <c>192.168.1.20</c>, <c>192.168.1.20:7777</c>,
    /// <c>game.example.com:7777</c>, <c>::1</c> or <c>[::1]:7777</c>. The port is optional and defaults to the game's.
    /// </summary>
    public readonly struct HostAddress
    {
        /// <summary>Creates an address.</summary>
        public HostAddress(string host, ushort port)
        {
            Host = host ?? string.Empty;
            Port = port;
        }

        /// <summary>The host name or IP, without brackets.</summary>
        public string Host { get; }

        /// <summary>The port.</summary>
        public ushort Port { get; }

        /// <summary>
        /// Reads <paramref name="text"/>. Whitespace around it is ignored; a missing port is <paramref name="defaultPort"/>.
        /// An IPv6 address takes a port only in brackets (<c>[::1]:7777</c>), since its colons are ambiguous otherwise.
        /// </summary>
        /// <param name="text">What the player typed.</param>
        /// <param name="defaultPort">The port to use when none is typed.</param>
        /// <param name="address">The address read, when the result is true.</param>
        /// <param name="error">A sentence to show the player, when the result is false.</param>
        public static bool TryParse(string text, ushort defaultPort, out HostAddress address, out string error)
        {
            address = default(HostAddress);
            error = null;
            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                error = "Type the host's address, for example 192.168.1.20 or 192.168.1.20:" + defaultPort + ".";
                return false;
            }

            string host = trimmed;
            string portText = null;
            if (trimmed[0] == '[')
            {
                int close = trimmed.IndexOf(']');
                if (close < 0)
                {
                    error = "The address has a '[' with no ']'.";
                    return false;
                }

                host = trimmed.Substring(1, close - 1);
                string rest = trimmed.Substring(close + 1);
                if (rest.Length > 0)
                {
                    if (rest[0] != ':')
                    {
                        error = "Put the port after the ']' as :port, for example [::1]:" + defaultPort + ".";
                        return false;
                    }

                    portText = rest.Substring(1);
                }
            }
            else
            {
                int first = trimmed.IndexOf(':');
                if (first >= 0 && first == trimmed.LastIndexOf(':'))
                {
                    host = trimmed.Substring(0, first);
                    portText = trimmed.Substring(first + 1);
                }
            }

            host = host.Trim();
            if (host.Length == 0)
            {
                error = "The address has a port but no host before it.";
                return false;
            }

            for (int i = 0; i < host.Length; i++)
            {
                if (char.IsWhiteSpace(host[i]))
                {
                    error = "An address cannot contain spaces.";
                    return false;
                }
            }

            ushort port = defaultPort;
            if (portText != null)
            {
                int parsed;
                if (!int.TryParse(portText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < 1 || parsed > 65535)
                {
                    error = "The port must be a number from 1 to 65535.";
                    return false;
                }

                port = (ushort)parsed;
            }

            if (port == 0)
            {
                error = "The port must be a number from 1 to 65535.";
                return false;
            }

            address = new HostAddress(host, port);
            return true;
        }

        /// <summary><c>host:port</c>, with an IPv6 host in brackets.</summary>
        public override string ToString()
        {
            return (Host.IndexOf(':') >= 0 ? "[" + Host + "]" : Host) + ":" + Port.ToString(CultureInfo.InvariantCulture);
        }
    }
}
