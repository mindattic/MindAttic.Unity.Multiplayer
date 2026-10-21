using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// What a dedicated server tells the rendezvous service about itself: the address and port players connect to,
    /// which can differ from what it binds. Behind NAT or a port-mapping host the public port is not the local one;
    /// on RunPod a pod's exposed TCP port <c>P</c> is reachable at <c>RUNPOD_PUBLIC_IP:RUNPOD_TCP_PORT_P</c>.
    /// </summary>
    public sealed class ServerAdvertisement
    {
        /// <summary>RunPod's environment variable holding the pod's public IP.</summary>
        public const string RunPodPublicIpVariable = "RUNPOD_PUBLIC_IP";

        /// <summary>Prefix of RunPod's per-port variables: <c>RUNPOD_TCP_PORT_7777=40123</c> maps internal 7777 to public 40123.</summary>
        public const string RunPodTcpPortPrefix = "RUNPOD_TCP_PORT_";

        private ServerAdvertisement(string address, ushort port, TransportKind transport, string source, IReadOnlyList<string> warnings)
        {
            Address = address;
            Port = port;
            Transport = transport;
            Source = source;
            Warnings = warnings;
        }

        /// <summary>The public address, or null to let the rendezvous service use the address the registration came from.</summary>
        public string Address { get; }

        /// <summary>The public port players connect to.</summary>
        public ushort Port { get; }

        /// <summary>The transport players must connect with.</summary>
        public TransportKind Transport { get; }

        /// <summary>Where the address and port came from, for the log ("config", "RunPod", "local port").</summary>
        public string Source { get; }

        /// <summary>Problems worth logging (for example a UDP server on RunPod, which maps TCP only). Never null.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>An explicit advertisement.</summary>
        /// <exception cref="ArgumentException">The port is 0.</exception>
        public static ServerAdvertisement Explicit(string address, ushort port, TransportKind transport)
        {
            if (port == 0)
            {
                throw new ArgumentException("An advertised port must be between 1 and 65535.", nameof(port));
            }

            return new ServerAdvertisement(string.IsNullOrWhiteSpace(address) ? null : address.Trim(), port, transport, "explicit", new string[0]);
        }

        /// <summary>
        /// Works out what <paramref name="config"/>'s server should advertise. In order:
        /// <list type="number">
        /// <item>The port: <see cref="NetConfig.PublicPort"/> when set; else, on RunPod with a WebSocket transport,
        /// <c>RUNPOD_TCP_PORT_{Port}</c>; else the port the server listens on.</item>
        /// <item>The address: <see cref="NetConfig.PublicAddress"/> when set; else, when the RunPod port mapping was used,
        /// <c>RUNPOD_PUBLIC_IP</c>; else null (the service records the address the registration came from).</item>
        /// </list>
        /// </summary>
        /// <param name="config">The server's config.</param>
        /// <param name="environment">Reads an environment variable (null when unset); null reads the process environment.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public static ServerAdvertisement For(NetConfig config, Func<string, string> environment = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Func<string, string> env = environment ?? Environment.GetEnvironmentVariable;
            var warnings = new List<string>();
            string source = "local port";
            ushort port = config.Port;
            bool runPod = false;

            string mappedText = env(RunPodTcpPortPrefix + config.Port.ToString(CultureInfo.InvariantCulture));
            ushort mapped = 0;
            bool hasMapping = !string.IsNullOrWhiteSpace(mappedText);
            if (hasMapping && (!ushort.TryParse(mappedText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out mapped) || mapped == 0))
            {
                warnings.Add(RunPodTcpPortPrefix + config.Port + "='" + mappedText + "' is not a port; ignoring it.");
                hasMapping = false;
            }

            if (config.PublicPort != 0)
            {
                port = config.PublicPort;
                source = "config";
            }
            else if (hasMapping)
            {
                if (config.Transport == TransportKind.WebSocket)
                {
                    port = mapped;
                    runPod = true;
                    source = "RunPod";
                }
                else
                {
                    warnings.Add("RunPod maps port " + config.Port + " over TCP only, but this server runs UDP; players cannot reach it. "
                        + "Run with -transport websocket on RunPod.");
                }
            }

            string address = null;
            if (!string.IsNullOrWhiteSpace(config.PublicAddress))
            {
                address = config.PublicAddress.Trim();
                source = source == "local port" ? "config" : source;
            }
            else if (runPod)
            {
                string publicIp = env(RunPodPublicIpVariable);
                if (!string.IsNullOrWhiteSpace(publicIp))
                {
                    address = publicIp.Trim();
                }
                else
                {
                    warnings.Add(RunPodPublicIpVariable + " is not set; the rendezvous service will record the address the registration came from.");
                }
            }

            return new ServerAdvertisement(address, port, config.Transport, source, warnings);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return (Address ?? "<caller's address>") + ":" + Port + " over " + Transport + " (" + Source + ")";
        }
    }
}
