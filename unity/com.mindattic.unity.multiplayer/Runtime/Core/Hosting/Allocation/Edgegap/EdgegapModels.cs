using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>One declared port on an Edgegap app version, matched back by <see cref="Name"/> in a deployment's port mapping.</summary>
    public sealed class EdgegapPortSpec
    {
        /// <summary>Declares a port.</summary>
        public EdgegapPortSpec(string name, int port, TransportKind transport)
        {
            Name = name;
            Port = port;
            Transport = transport;
        }

        /// <summary>The port's name (Edgegap's app-version <c>ports[].name</c>).</summary>
        public string Name { get; }

        /// <summary>The container's internal port.</summary>
        public int Port { get; }

        /// <summary>Udp or WebSocket (carried over TCP on Edgegap's side).</summary>
        public TransportKind Transport { get; }
    }

    /// <summary>What to tell Edgegap about the app version to deploy (<see cref="IEdgegapApiClient.EnsureAppVersionAsync"/>).</summary>
    public sealed class EdgegapAppVersionRequest
    {
        /// <summary>Describes an app version.</summary>
        public EdgegapAppVersionRequest(
            string appName,
            string versionName,
            string imageReference,
            IReadOnlyList<EdgegapPortSpec> ports,
            IReadOnlyDictionary<string, string> environmentVariables)
        {
            AppName = appName;
            VersionName = versionName;
            ImageReference = imageReference;
            Ports = ports;
            EnvironmentVariables = environmentVariables;
        }

        /// <summary>The Edgegap application name (created once per game, out of band, in the Edgegap dashboard).</summary>
        public string AppName { get; }

        /// <summary>The version name Edgegap tracks this build under; the mp-host image tag is a natural choice.</summary>
        public string VersionName { get; }

        /// <summary>The GHCR image reference this version runs (<c>ghcr.io/org/game:tag</c>), the same one mp-host just pushed.</summary>
        public string ImageReference { get; }

        /// <summary>The ports the container exposes.</summary>
        public IReadOnlyList<EdgegapPortSpec> Ports { get; }

        /// <summary>Container environment variables (rendezvous URL/secret and the like), the same shape every other hosting profile uses.</summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }
    }

    /// <summary>What to ask Edgegap to deploy for one match (<see cref="IEdgegapApiClient.DeployAsync"/>).</summary>
    public sealed class EdgegapDeployRequest
    {
        /// <summary>Describes a deployment request.</summary>
        public EdgegapDeployRequest(string appName, string versionName, string region)
        {
            AppName = appName;
            VersionName = versionName;
            Region = region;
        }

        /// <summary>The Edgegap application name.</summary>
        public string AppName { get; }

        /// <summary>The app version to deploy (from a prior <see cref="IEdgegapApiClient.EnsureAppVersionAsync"/>).</summary>
        public string VersionName { get; }

        /// <summary>A preferred region/city code, or null to let Edgegap's own distance routing pick the closest.</summary>
        public string Region { get; }
    }

    /// <summary>One port Edgegap mapped on a live deployment.</summary>
    public sealed class EdgegapPortMapping
    {
        /// <summary>Describes a mapped port.</summary>
        public EdgegapPortMapping(string name, int externalPort, TransportKind transport)
        {
            Name = name;
            ExternalPort = externalPort;
            Transport = transport;
        }

        /// <summary>The port's name, matching <see cref="EdgegapPortSpec.Name"/>.</summary>
        public string Name { get; }

        /// <summary>The public port players connect to.</summary>
        public int ExternalPort { get; }

        /// <summary>The transport this port speaks.</summary>
        public TransportKind Transport { get; }
    }

    /// <summary>A live (or still-starting) Edgegap deployment, as reported by <c>POST /deploy</c> or <c>GET /status/{id}</c>.</summary>
    public sealed class EdgegapDeployment
    {
        /// <summary>Describes a deployment.</summary>
        public EdgegapDeployment(string requestId, string publicIp, bool running, IReadOnlyList<EdgegapPortMapping> ports)
        {
            RequestId = requestId;
            PublicIp = publicIp;
            Running = running;
            Ports = ports;
        }

        /// <summary>Edgegap's deployment id; pass it back to <see cref="IEdgegapApiClient.StopDeploymentAsync"/>.</summary>
        public string RequestId { get; }

        /// <summary>The public IP players connect to; null while still provisioning.</summary>
        public string PublicIp { get; }

        /// <summary>True once Edgegap reports the container running and its ports mapped.</summary>
        public bool Running { get; }

        /// <summary>The mapped ports; empty while still provisioning.</summary>
        public IReadOnlyList<EdgegapPortMapping> Ports { get; }
    }
}
