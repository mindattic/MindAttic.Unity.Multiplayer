using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class ServerPackageTests
    {
        private static ServerPackageSpec Vps(NetConfig config = null, ServerSettings settings = null)
        {
            return ServerPackageSpec.For(HostingProfiles.Find(GenericVpsProfile.ProfileId), "Game.x86_64", config, settings);
        }

        private static ServerPackageSpec RunPod(NetConfig config = null, ServerSettings settings = null)
        {
            return ServerPackageSpec.For(HostingProfiles.Find(RunPodProfile.ProfileId), "Game.x86_64", config, settings);
        }

        private static string FileText(IReadOnlyList<ServerPackageFile> files, string path)
        {
            ServerPackageFile file = files.Single(f => f.RelativePath == path);
            return file.Contents;
        }

        // --- profiles ---------------------------------------------------------------------------------------------

        [Test]
        public void GenericVps_IsTheDefault_AndRunPodIsRegistered()
        {
            Assert.That(HostingProfiles.Default.Id, Is.EqualTo("GenericVps"));
            Assert.That(HostingProfiles.All.Select(p => p.Id), Is.EqualTo(new[] { "GenericVps", "RunPod" }));
            Assert.That(HostingProfiles.Find("runpod"), Is.InstanceOf<RunPodProfile>());
            Assert.That(HostingProfiles.Find("GPortal"), Is.Null);
        }

        [Test]
        public void ALaterProfile_SlotsInWithoutTouchingTheOthers()
        {
            var later = new StubProfile("Nitrado");
            HostingProfiles.Register(later);
            try
            {
                Assert.That(HostingProfiles.Find("Nitrado"), Is.SameAs(later));
                Assert.That(HostingProfiles.Default.Id, Is.EqualTo("GenericVps"));

                ServerPackageSpec spec = ServerPackageSpec.For(later, "Game.x86_64");
                IReadOnlyList<ServerPackageFile> files = spec.ComposeFiles();
                Assert.That(files.Select(f => f.RelativePath), Does.Contain("nitrado.txt"));
                Assert.That(FileText(files, "server-package.json"), Does.Contain("\"profile\": \"Nitrado\""));
            }
            finally
            {
                Assert.That(HostingProfiles.Unregister("Nitrado"), Is.True);
                Assert.That(HostingProfiles.Unregister("GenericVps"), Is.False);
            }
        }

        [Test]
        public void Register_RefusesAnUnsafeId_AndAProfileFileThatClashes()
        {
            Assert.Throws<ArgumentException>(() => HostingProfiles.Register(new StubProfile("bad id")));

            var clash = new StubProfile("Clash") { ExtraPath = "server.json" };
            ServerPackageSpec spec = ServerPackageSpec.For(clash, "Game.x86_64");
            Assert.Throws<InvalidOperationException>(() => spec.ComposeFiles());
        }

        [Test]
        public void For_MakesADedicatedServerCopy_WithoutTheRendezvousSecret()
        {
            var config = new NetConfig { Topology = Topology.Host, RendezvousSecret = "s3cret", MaxPlayers = 3 };

            ServerPackageSpec spec = Vps(config);

            Assert.That(spec.Config, Is.Not.SameAs(config));
            Assert.That(spec.Config.Topology, Is.EqualTo(Topology.Server));
            Assert.That(spec.Config.RendezvousSecret, Is.Null);
            Assert.That(spec.Config.MaxPlayers, Is.EqualTo(3));
            Assert.That(config.Topology, Is.EqualTo(Topology.Host));
            Assert.Throws<ArgumentException>(() => ServerPackageSpec.For(HostingProfiles.Default, "bad name"));
        }

        [Test]
        public void RunPod_SwitchesToWebSocket_AndDeclaresTcpPorts()
        {
            ServerPackageSpec spec = RunPod(new NetConfig { Transport = TransportKind.Udp, Port = 7777 });

            Assert.That(spec.Config.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(spec.Ports.Select(p => p.ToString()), Is.EqualTo(new[] { "game 7777/tcp", "health " + ServerSettings.DefaultHealthPort + "/tcp" }));
            Assert.That(spec.Validate(), Is.Empty);
        }

        [Test]
        public void RunPod_MovesAHealthPortThatWouldClashWithTheGamePort()
        {
            ServerPackageSpec spec = RunPod(new NetConfig { Port = ServerSettings.DefaultHealthPort });
            Assert.That(spec.Settings.HealthPort, Is.Not.EqualTo(spec.Config.Port));
            Assert.That(spec.Validate(), Is.Empty);
        }

        [Test]
        public void GenericVps_KeepsUdp_AndDeclaresTheGameAndHealthPorts()
        {
            ServerPackageSpec spec = Vps();

            Assert.That(spec.Config.Transport, Is.EqualTo(TransportKind.Udp));
            Assert.That(spec.Ports, Has.Count.EqualTo(2));
            Assert.That(spec.Ports[0], Is.EqualTo(new DeclaredPort("game", 7777, PortProtocol.Udp, "Players connect over UDP.")));
            Assert.That(spec.Ports[1].Name, Is.EqualTo("health"));
            Assert.That(spec.Ports[1].Protocol, Is.EqualTo(PortProtocol.Tcp));
        }

        [Test]
        public void HealthPortZero_DeclaresOnlyTheGamePort()
        {
            ServerPackageSpec spec = Vps(settings: new ServerSettings { HealthPort = 0 });
            Assert.That(spec.Ports.Select(p => p.Name), Is.EqualTo(new[] { "game" }));
            Assert.That(FileText(spec.ComposeFiles(), "Dockerfile"), Does.Not.Contain("HEALTHCHECK"));
        }

        // --- layout -----------------------------------------------------------------------------------------------

        [Test]
        public void Layout_IsTheCommonPackagePlusTheProfileFiles()
        {
            Assert.That(Vps().ExpectedLayout().Select(f => f.ToString()), Is.EqualTo(new[]
            {
                "Game.x86_64", "Game_Data/", "UnityPlayer.so",
                "server.json", "start.sh", "server-package.json", "Logs/", "Saves/",
                "Dockerfile", ".dockerignore", "docker-compose.yml", "HOSTING.txt",
            }));

            Assert.That(RunPod().ComposeFiles().Select(f => f.ToString()), Is.EqualTo(new[]
            {
                "server.json", "start.sh", "server-package.json", "Logs/", "Saves/", "Dockerfile", ".dockerignore", "HOSTING.txt",
            }));
        }

        [Test]
        public void Layout_FollowsRenamedFolders_AndSkipsOnesOutsideThePackage()
        {
            ServerPackageSpec spec = Vps(settings: new ServerSettings { LogsDirectory = "/var/log/game", SavesDirectory = "Data/World" });
            IReadOnlyList<string> paths = spec.ComposeFiles().Select(f => f.ToString()).ToList();

            Assert.That(paths, Does.Contain("Data/World/"));
            Assert.That(paths, Does.Not.Contain("Logs/"));
            Assert.That(paths.Any(p => p.Contains("var")), Is.False);
        }

        [Test]
        public void Layout_OnlyTheStartScriptIsExecutable_AndEveryTextUsesLf()
        {
            foreach (ServerPackageFile file in Vps().ComposeFiles())
            {
                Assert.That(file.IsExecutable, Is.EqualTo(file.RelativePath == "start.sh"), file.RelativePath);
                if (!file.IsDirectory)
                {
                    Assert.That(file.Contents, Does.Not.Contain("\r"), file.RelativePath);
                    Assert.That(file.Contents, Does.EndWith("\n"), file.RelativePath);
                }
            }
        }

        [Test]
        public void DataFolder_IsTheExecutableNameWithoutItsExtension()
        {
            Assert.That(ServerPackageSpec.For(HostingProfiles.Default, "My_Game.x86_64").DataFolderName, Is.EqualTo("My_Game_Data"));
            Assert.That(ServerPackageSpec.For(HostingProfiles.Default, "server").DataFolderName, Is.EqualTo("server_Data"));
        }

        [Test]
        public void PackageFile_RefusesPathsOutsideThePackage()
        {
            Assert.Throws<ArgumentException>(() => ServerPackageFile.Text("../x", "a"));
            Assert.Throws<ArgumentException>(() => ServerPackageFile.Text("/etc/x", "a"));
            Assert.Throws<ArgumentException>(() => ServerPackageFile.Text("a\\b", "a"));
            Assert.Throws<ArgumentException>(() => ServerPackageFile.Directory(""));
        }

        // --- the files --------------------------------------------------------------------------------------------

        [Test]
        public void ServerJson_HoldsTheSpecsSettings()
        {
            var config = new NetConfig { ServerName = "Friday", MaxPlayers = 4, Port = 7790 };
            ServerPackageSpec spec = Vps(config);
            var back = new NetConfig();
            var settings = new ServerSettings();

            ServerConfigResult result = ServerConfigFile.Apply(FileText(spec.ComposeFiles(), "server.json"), back, settings);

            Assert.That(result.Errors, Is.Empty);
            Assert.That(back.ServerName, Is.EqualTo("Friday"));
            Assert.That(back.MaxPlayers, Is.EqualTo(4));
            Assert.That(back.Port, Is.EqualTo(7790));
        }

        [Test]
        public void StartScript_ExecsTheServerWithTheConfigAndForwardsArguments()
        {
            ServerPackageSpec spec = Vps();
            spec.ExtraArgs.Add("-tickrate");
            spec.ExtraArgs.Add("60");
            string script = spec.ComposeStartScript();
            string[] lines = script.Split('\n');

            Assert.That(lines[0], Is.EqualTo("#!/bin/sh"));
            Assert.That(script, Does.Contain("cd \"$(dirname \"$0\")\"\n"));
            Assert.That(script, Does.Contain("mkdir -p Logs Saves\n"));
            Assert.That(lines.Single(l => l.StartsWith("exec ")),
                Is.EqualTo("exec ./Game.x86_64 -batchmode -nographics -logFile - -mode server -config server.json -tickrate 60 \"$@\""));
        }

        [Test]
        public void StartScript_QuotesFoldersWithSpaces()
        {
            ServerPackageSpec spec = Vps(settings: new ServerSettings { SavesDirectory = "My Saves" });
            Assert.That(spec.ComposeStartScript(), Does.Contain("mkdir -p Logs 'My Saves'\n"));
            Assert.That(ServerPackageSpec.ShellQuote("it's"), Is.EqualTo("'it'\\''s'"));
            Assert.That(ServerPackageSpec.ShellQuote(""), Is.EqualTo("''"));
        }

        [Test]
        public void Manifest_DeclaresProfileExecutablePortsAndHealthPath()
        {
            string manifest = Vps().ComposeManifest();
            Assert.That(RendezvousJson.TryParseObject(manifest, out Dictionary<string, object> fields), Is.True, manifest);
            Assert.That(fields["profile"], Is.EqualTo("GenericVps"));
            Assert.That(fields["executable"], Is.EqualTo("Game.x86_64"));
            Assert.That(fields["start"], Is.EqualTo("./start.sh"));
            Assert.That(fields["transport"], Is.EqualTo("udp"));
            Assert.That(fields["stopSignal"], Is.EqualTo("SIGTERM"));
            Assert.That(manifest, Does.Contain("{ \"name\": \"game\", \"port\": 7777, \"protocol\": \"udp\" }"));
            Assert.That(manifest, Does.Contain("{ \"name\": \"health\", \"port\": " + ServerSettings.DefaultHealthPort + ", \"protocol\": \"tcp\", \"path\": \"/healthz\" }"));
        }

        [Test]
        public void Dockerfile_RunsStartShAsNonRoot_WithVolumesStopSignalAndHealthcheck()
        {
            string dockerfile = FileText(Vps().ComposeFiles(), "Dockerfile");

            Assert.That(dockerfile, Does.Contain("FROM ubuntu:24.04\n"));
            Assert.That(dockerfile, Does.Contain("useradd --create-home --shell /usr/sbin/nologin server\n"));
            Assert.That(dockerfile, Does.Contain("RUN chmod +x start.sh Game.x86_64 && mkdir -p /srv/game/Logs /srv/game/Saves"));
            Assert.That(dockerfile, Does.Contain("USER server\n"));
            Assert.That(dockerfile, Does.Contain("EXPOSE 7777/udp\n"));
            Assert.That(dockerfile, Does.Contain("EXPOSE " + ServerSettings.DefaultHealthPort + "/tcp\n"));
            Assert.That(dockerfile, Does.Contain("VOLUME [\"/srv/game/Saves\", \"/srv/game/Logs\"]\n"));
            Assert.That(dockerfile, Does.Contain("STOPSIGNAL SIGTERM\n"));
            Assert.That(dockerfile, Does.Contain("curl -fsS http://127.0.0.1:" + ServerSettings.DefaultHealthPort + "/healthz"));
            Assert.That(dockerfile, Does.EndWith("ENTRYPOINT [\"./start.sh\"]\n"));
            Assert.That(dockerfile.IndexOf("USER server", StringComparison.Ordinal), Is.GreaterThan(dockerfile.IndexOf("COPY", StringComparison.Ordinal)));
        }

        [Test]
        public void Dockerignore_KeepsDebugFoldersLocalSavesAndLogsOutOfTheImage()
        {
            string ignore = FileText(Vps().ComposeFiles(), ".dockerignore");
            foreach (string line in new[] { "*_BurstDebugInformation_DoNotShip", "*_BackUpThisFolder_ButDontShipItWithYourGame", "Logs", "Saves", "Dockerfile", "docker-compose.yml" })
            {
                Assert.That(ignore.Split('\n'), Does.Contain(line));
            }
        }

        [Test]
        public void Compose_RunsTheGameBesideTheRendezvousService()
        {
            string compose = FileText(Vps().ComposeFiles(), "docker-compose.yml");

            Assert.That(compose, Does.Contain("  game:\n    build: .\n"));
            Assert.That(compose, Does.Contain("      - \"7777:7777/udp\"\n"));
            Assert.That(compose, Does.Contain("      - \"127.0.0.1:" + ServerSettings.DefaultHealthPort + ":" + ServerSettings.DefaultHealthPort + "/tcp\"\n"));
            Assert.That(compose, Does.Contain("MINDATTIC_RENDEZVOUS_URL: http://rendezvous:8080\n"));
            Assert.That(compose, Does.Contain("MINDATTIC_RENDEZVOUS_SECRET: ${RENDEZVOUS_SECRET:?"));
            Assert.That(compose, Does.Contain("MINDATTIC_PUBLIC_ADDRESS: ${PUBLIC_ADDRESS:?"));
            Assert.That(compose, Does.Contain("  rendezvous:\n"));
            Assert.That(compose, Does.Contain("Rendezvous__RegistrationSecret: ${RENDEZVOUS_SECRET:?"));
            Assert.That(compose, Does.Contain("      - saves:/srv/game/Saves\n"));
            Assert.That(compose, Does.Contain("stop_grace_period: 15s\n"));
        }

        [Test]
        public void RunPodNotes_ListTheTcpPortsAndTheRunPodVariables()
        {
            string notes = FileText(RunPod().ComposeFiles(), "HOSTING.txt");
            Assert.That(notes, Does.Contain("Expose TCP Ports\" list: 7777, " + ServerSettings.DefaultHealthPort));
            Assert.That(notes, Does.Contain("RUNPOD_TCP_PORT_7777"));
            Assert.That(FileText(RunPod().ComposeFiles(), "Dockerfile"), Does.Contain("EXPOSE 7777/tcp\n"));
        }

        private sealed class StubProfile : IHostingProfile
        {
            public StubProfile(string id)
            {
                Id = id;
            }

            public string Id { get; }

            public string DisplayName
            {
                get { return Id; }
            }

            public string Description
            {
                get { return "A test profile."; }
            }

            public string ExtraPath { get; set; }

            public void Configure(ServerPackageSpec spec)
            {
            }

            public IReadOnlyList<ServerPackageFile> CreateFiles(ServerPackageSpec spec)
            {
                return new[] { ServerPackageFile.Text(ExtraPath ?? Id.ToLowerInvariant() + ".txt", "hello\n") };
            }
        }
    }
}
