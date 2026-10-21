using System;
using System.Collections.Generic;
using System.Text;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The RunPod hosting profile. RunPod pods appear to expose TCP/HTTP only, so the package runs WebSocket transport on
    /// a TCP port the pod maps to a public one; the server reads RunPod's <c>RUNPOD_PUBLIC_IP</c> and
    /// <c>RUNPOD_TCP_PORT_*</c> and registers the mapped address with a rendezvous service hosted elsewhere
    /// (<see cref="ServerAdvertisement"/>). UM-T18 checks RunPod's current UDP support.
    /// </summary>
    public sealed class RunPodProfile : IHostingProfile
    {
        /// <summary>This profile's <see cref="Id"/>.</summary>
        public const string ProfileId = "RunPod";

        /// <summary>The notes file's name.</summary>
        public const string NotesFileName = "HOSTING.txt";

        /// <inheritdoc />
        public string Id
        {
            get { return ProfileId; }
        }

        /// <inheritdoc />
        public string DisplayName
        {
            get { return "RunPod (WebSocket over a mapped TCP port)"; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "A RunPod pod: WebSocket transport on a mapped TCP port, registered with a rendezvous service hosted elsewhere."; }
        }

        /// <summary>The Docker base image; null for <see cref="ServerPackageDockerfile.DefaultBaseImage"/>.</summary>
        public string BaseImage { get; set; }

        /// <inheritdoc />
        public void Configure(ServerPackageSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            spec.Config.Transport = TransportKind.WebSocket;
            if (spec.Config.JoinCodes == JoinCodeProvider.Relay)
            {
                spec.Config.JoinCodes = JoinCodeProvider.Auto;
            }

            if (spec.Settings.HealthPort == spec.Config.Port)
            {
                spec.Settings.HealthPort = spec.Config.Port == ServerSettings.DefaultHealthPort ? (ushort)(ServerSettings.DefaultHealthPort + 1) : ServerSettings.DefaultHealthPort;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<ServerPackageFile> CreateFiles(ServerPackageSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            return new[]
            {
                ServerPackageFile.Text(ServerPackageDockerfile.FileName, ServerPackageDockerfile.Compose(spec, BaseImage)),
                ServerPackageFile.Text(ServerPackageDockerfile.IgnoreFileName, ServerPackageDockerfile.ComposeIgnore(spec, new[] { NotesFileName })),
                ServerPackageFile.Text(NotesFileName, ComposeNotes(spec)),
            };
        }

        /// <summary>The <c>HOSTING.txt</c> text.</summary>
        public static string ComposeNotes(ServerPackageSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var text = new StringBuilder();
            text.Append("Hosting profile: RunPod\n");
            text.Append("=======================\n\n");
            text.Append("RunPod pods expose TCP/HTTP ports, so this package plays over WebSocket on TCP ").Append(spec.Config.Port).Append(".\n\n");
            text.Append("1. Build and push the image: docker build -t <registry>/game-server . && docker push <registry>/game-server\n");
            text.Append("2. Create a CPU pod template from that image. Under \"Expose TCP Ports\" list: ");
            var tcp = new List<string>();
            foreach (DeclaredPort port in spec.Ports)
            {
                tcp.Add(port.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            text.Append(string.Join(", ", tcp)).Append(".\n");
            text.Append("   The server finds its public address in RUNPOD_PUBLIC_IP and its mapped port in RUNPOD_TCP_PORT_")
                .Append(spec.Config.Port).Append(" by itself.\n");
            text.Append("3. Set the pod's environment: MINDATTIC_RENDEZVOUS_URL (a rendezvous service reachable from the internet, for\n");
            text.Append("   example the one a GenericVps host runs), MINDATTIC_RENDEZVOUS_SECRET, and MINDATTIC_SERVER_PASSWORD for a\n");
            text.Append("   private server. Never bake secrets into the image.\n");
            text.Append("4. For saves that outlive the pod, attach a network volume mounted at ")
                .Append(ServerPackageDockerfile.ContainerPath(spec.Settings.SavesDirectory)).Append(".\n");
            text.Append("5. Stopping the pod sends SIGTERM: the server tells players, saves and exits cleanly.\n");
            return text.ToString();
        }
    }
}
