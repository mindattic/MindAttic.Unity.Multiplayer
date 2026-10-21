using System;
using System.Collections.Generic;
using System.IO;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class ServerConfigTests
    {
        private static readonly string PackageDir = Path.Combine(Path.GetTempPath(), "pkg-not-on-disk");

        private static Func<string, string> Files(Dictionary<string, string> files)
        {
            return path =>
            {
                foreach (KeyValuePair<string, string> file in files)
                {
                    if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(PackageDir, file.Key)), StringComparison.OrdinalIgnoreCase))
                    {
                        return file.Value;
                    }
                }

                return null;
            };
        }

        private static Func<string, string> Env(Dictionary<string, string> vars = null)
        {
            return name => vars != null && vars.TryGetValue(name, out string value) ? value : null;
        }

        // --- server.json ------------------------------------------------------------------------------------------

        [Test]
        public void Apply_ReadsEveryKey()
        {
            var config = new NetConfig();
            var settings = new ServerSettings();
            string json = @"{
              ""name"": ""Friday game"", ""password"": ""hunter2"", ""port"": 7800, ""maxPlayers"": 6,
              ""address"": ""10.0.0.5"", ""transport"": ""websocket"", ""tickRate"": 20, ""reconnectGraceSeconds"": 12.5,
              ""joinCodes"": ""rendezvous"", ""rendezvousUrl"": ""https://codes.example.com"", ""publicAddress"": ""203.0.113.7"",
              ""publicPort"": 40123, ""healthPort"": 9100, ""adminCommands"": true, ""logsDirectory"": ""/var/log/game"",
              ""savesDirectory"": ""Data/Saves"", ""keepLogFiles"": 3, ""shutdownGraceSeconds"": 20
            }";

            ServerConfigResult result = ServerConfigFile.Apply(json, config, settings);

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(config.ServerName, Is.EqualTo("Friday game"));
            Assert.That(config.Password, Is.EqualTo("hunter2"));
            Assert.That(config.Port, Is.EqualTo(7800));
            Assert.That(config.MaxPlayers, Is.EqualTo(6));
            Assert.That(config.Address, Is.EqualTo("10.0.0.5"));
            Assert.That(config.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(config.TickRate, Is.EqualTo(20));
            Assert.That(config.ReconnectGraceSeconds, Is.EqualTo(12.5));
            Assert.That(config.JoinCodes, Is.EqualTo(JoinCodeProvider.Rendezvous));
            Assert.That(config.RendezvousUrl, Is.EqualTo("https://codes.example.com"));
            Assert.That(config.PublicAddress, Is.EqualTo("203.0.113.7"));
            Assert.That(config.PublicPort, Is.EqualTo(40123));
            Assert.That(settings.HealthPort, Is.EqualTo(9100));
            Assert.That(settings.AdminCommands, Is.True);
            Assert.That(settings.LogsDirectory, Is.EqualTo("/var/log/game"));
            Assert.That(settings.SavesDirectory, Is.EqualTo("Data/Saves"));
            Assert.That(settings.KeepLogFiles, Is.EqualTo(3));
            Assert.That(settings.ShutdownGraceSeconds, Is.EqualTo(20));
        }

        [Test]
        public void Apply_KeysIgnoreCase_AndUnknownKeysWarn()
        {
            var config = new NetConfig();
            ServerConfigResult result = ServerConfigFile.Apply("{\"MaxPlayers\": 3, \"motd\": \"hi\"}", config, new ServerSettings());

            Assert.That(result.IsValid, Is.True);
            Assert.That(config.MaxPlayers, Is.EqualTo(3));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("motd"));
        }

        [Test]
        public void Apply_RefusesTheRendezvousSecret()
        {
            var config = new NetConfig();
            ServerConfigResult result = ServerConfigFile.Apply("{\"rendezvousSecret\": \"s3cret\"}", config, new ServerSettings());

            Assert.That(config.RendezvousSecret, Is.Null);
            Assert.That(result.Warnings[0], Does.Contain(NetEnvironment.RendezvousSecretVariable));
        }

        [TestCase("{\"port\": 0}", "port")]
        [TestCase("{\"port\": 70000}", "port")]
        [TestCase("{\"port\": \"7777\"}", "port")]
        [TestCase("{\"maxPlayers\": 1.5}", "maxPlayers")]
        [TestCase("{\"transport\": \"tcp\"}", "transport")]
        [TestCase("{\"joinCodes\": \"magic\"}", "joinCodes")]
        [TestCase("{\"rendezvousUrl\": \"ftp://x\"}", "rendezvousUrl")]
        [TestCase("{\"adminCommands\": \"yes\"}", "adminCommands")]
        [TestCase("{\"name\": 5}", "name")]
        [TestCase("{\"savesDirectory\": \"\"}", "savesDirectory")]
        [TestCase("{\"shutdownGraceSeconds\": -1}", "shutdownGraceSeconds")]
        public void Apply_NamesABadValue_AndLeavesTheSettingAlone(string json, string key)
        {
            var config = new NetConfig();
            var settings = new ServerSettings();

            ServerConfigResult result = ServerConfigFile.Apply(json, config, settings);

            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Does.Contain("\"" + key + "\""));
            Assert.That(config.Port, Is.EqualTo(NetConfig.DefaultPort));
            Assert.That(settings.SavesDirectory, Is.EqualTo(ServerSettings.DefaultSavesDirectory));
        }

        [TestCase("")]
        [TestCase("[1,2]")]
        [TestCase("{\"port\": 7777")]
        [TestCase("port = 7777")]
        public void Apply_NotAnObject_IsOneError(string json)
        {
            ServerConfigResult result = ServerConfigFile.Apply(json, new NetConfig(), new ServerSettings());
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Does.Contain("not a JSON object"));
        }

        [Test]
        public void Compose_RoundTripsThroughApply_AndLeavesOutTheSecret()
        {
            var config = new NetConfig
            {
                ServerName = "Quote \" and \\ slash", Password = "pw", Port = 7801, MaxPlayers = 12, Transport = TransportKind.WebSocket,
                TickRate = 60, JoinCodes = JoinCodeProvider.None, RendezvousUrl = "http://localhost:8080", RendezvousSecret = "never",
                PublicPort = 40000, ReconnectGraceSeconds = 30,
            };
            var settings = new ServerSettings { HealthPort = 9200, AdminCommands = true, SavesDirectory = "World", KeepLogFiles = 5, ShutdownGraceSeconds = 7.5 };

            string json = ServerConfigFile.Compose(config, settings);
            var back = new NetConfig();
            var backSettings = new ServerSettings();
            ServerConfigResult result = ServerConfigFile.Apply(json, back, backSettings);

            Assert.That(json, Does.Not.Contain("never"));
            Assert.That(json, Does.StartWith("{\n  \"name\": "));
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(back.ServerName, Is.EqualTo(config.ServerName));
            Assert.That(back.Password, Is.EqualTo("pw"));
            Assert.That(back.Port, Is.EqualTo(7801));
            Assert.That(back.MaxPlayers, Is.EqualTo(12));
            Assert.That(back.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(back.TickRate, Is.EqualTo(60));
            Assert.That(back.JoinCodes, Is.EqualTo(JoinCodeProvider.None));
            Assert.That(back.RendezvousUrl, Is.EqualTo("http://localhost:8080"));
            Assert.That(back.PublicPort, Is.EqualTo(40000));
            Assert.That(back.ReconnectGraceSeconds, Is.EqualTo(30));
            Assert.That(backSettings.HealthPort, Is.EqualTo(9200));
            Assert.That(backSettings.AdminCommands, Is.True);
            Assert.That(backSettings.SavesDirectory, Is.EqualTo("World"));
            Assert.That(backSettings.KeepLogFiles, Is.EqualTo(5));
            Assert.That(backSettings.ShutdownGraceSeconds, Is.EqualTo(7.5));
        }

        [Test]
        public void Compose_OfDefaults_HasEmptyStringsNotNulls()
        {
            string json = ServerConfigFile.Compose(new NetConfig(), new ServerSettings());
            Assert.That(json, Does.Contain("\"password\": \"\""));
            Assert.That(json, Does.Contain("\"port\": 7777"));
            Assert.That(json, Does.Contain("\"healthPort\": " + ServerSettings.DefaultHealthPort));
            Assert.That(json, Does.Not.Contain("null"));
        }

        // --- layering ---------------------------------------------------------------------------------------------

        [Test]
        public void Load_LayersFileThenEnvironmentThenCommandLine()
        {
            var files = new Dictionary<string, string>
            {
                { "server.json", "{\"name\": \"From file\", \"port\": 7001, \"maxPlayers\": 2, \"password\": \"file\", \"healthPort\": 9001}" },
            };
            var env = new Dictionary<string, string> { { NetEnvironment.ServerPasswordVariable, "env" } };
            var config = new NetConfig { Topology = Topology.Host, MaxPlayers = 8 };

            ServerStartupResult result = ServerStartup.Load(config, new[] { "Game.x86_64", "-batchmode", "-port", "7002", "-name", "From CLI", "-healthport=9002" },
                PackageDir, Env(env), Files(files));

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.ConfigPath, Is.EqualTo(Path.Combine(PackageDir, "server.json")));
            Assert.That(config.Topology, Is.EqualTo(Topology.Server));
            Assert.That(config.ServerName, Is.EqualTo("From CLI"));
            Assert.That(config.Port, Is.EqualTo(7002));
            Assert.That(config.MaxPlayers, Is.EqualTo(2));
            Assert.That(config.Password, Is.EqualTo("env"));
            Assert.That(result.Settings.HealthPort, Is.EqualTo(9002));
            Assert.That(result.CommandLine.Headless, Is.True);
        }

        [Test]
        public void Load_CommandLinePasswordAndMaxPlayersBeatTheEnvironment()
        {
            var env = new Dictionary<string, string> { { NetEnvironment.ServerPasswordVariable, "env" } };
            var config = new NetConfig();

            ServerStartupResult result = ServerStartup.Load(config, new[] { "-password", "cli", "-maxPlayers", "5" }, PackageDir, Env(env), Files(new Dictionary<string, string>()));

            Assert.That(result.IsValid, Is.True, string.Join(" ", result.Errors));
            Assert.That(config.Password, Is.EqualTo("cli"));
            Assert.That(config.MaxPlayers, Is.EqualTo(5));
        }

        [Test]
        public void Load_WithoutAFile_UsesTheDefaults()
        {
            var config = new NetConfig();
            ServerStartupResult result = ServerStartup.Load(config, new string[0], PackageDir, Env(), Files(new Dictionary<string, string>()));

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ConfigPath, Is.Null);
            Assert.That(result.Settings.HealthPort, Is.EqualTo(ServerSettings.DefaultHealthPort));
            Assert.That(result.Paths.LogsDirectory, Is.EqualTo(Path.Combine(Path.GetFullPath(PackageDir), "Logs")));
            Assert.That(result.Paths.SavesDirectory, Is.EqualTo(Path.Combine(Path.GetFullPath(PackageDir), "Saves")));
        }

        [Test]
        public void Load_ConfigArgumentPicksAnotherFile_AndAMissingOneIsAnError()
        {
            var files = new Dictionary<string, string> { { "alt.json", "{\"maxPlayers\": 3}" } };

            var config = new NetConfig();
            ServerStartupResult found = ServerStartup.Load(config, new[] { "-config", "alt.json" }, PackageDir, Env(), Files(files));
            Assert.That(found.IsValid, Is.True);
            Assert.That(config.MaxPlayers, Is.EqualTo(3));

            ServerStartupResult missing = ServerStartup.Load(new NetConfig(), new[] { "-config", "nope.json" }, PackageDir, Env(), Files(files));
            Assert.That(missing.Errors, Has.Some.Contains("nope.json"));
        }

        [Test]
        public void Load_CollectsErrorsFromEveryLayer()
        {
            var files = new Dictionary<string, string> { { "server.json", "{\"port\": \"x\"}" } };
            ServerStartupResult result = ServerStartup.Load(new NetConfig(), new[] { "-maxPlayers", "0", "-healthport", "abc" }, PackageDir, Env(), Files(files));

            Assert.That(result.Errors, Has.Some.Contains("\"port\""));
            Assert.That(result.Errors, Has.Some.Contains("-maxplayers"));
            Assert.That(result.Errors, Has.Some.Contains("-healthport"));
        }

        [Test]
        public void Load_RefusesAHealthPortOnTheWebSocketGamePort()
        {
            ServerStartupResult result = ServerStartup.Load(new NetConfig(), new[] { "-transport", "websocket", "-port", "9000", "-healthport", "9000" },
                PackageDir, Env(), Files(new Dictionary<string, string>()));
            Assert.That(result.Errors, Has.Some.Contains("health port"));
        }

        [Test]
        public void PackageArguments_ReadAdminLogsAndSaves()
        {
            var settings = new ServerSettings();
            IReadOnlyList<string> errors = ServerStartup.ApplyPackageArguments(settings, new[] { "-admin", "-logs", "/tmp/l", "-saves=World", "-port", "1" });
            Assert.That(errors, Is.Empty);
            Assert.That(settings.AdminCommands, Is.True);
            Assert.That(settings.LogsDirectory, Is.EqualTo("/tmp/l"));
            Assert.That(settings.SavesDirectory, Is.EqualTo("World"));

            errors = ServerStartup.ApplyPackageArguments(settings, new[] { "-admin", "off" });
            Assert.That(errors, Is.Empty);
            Assert.That(settings.AdminCommands, Is.False);

            errors = ServerStartup.ApplyPackageArguments(settings, new[] { "-admin", "maybe", "-saves" });
            Assert.That(errors, Has.Count.EqualTo(2));
        }

        [Test]
        public void CommandLine_ReadsNameAndPassword()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-name", "My server", "-password=pw" });
            Assert.That(result.IsValid, Is.True);
            Assert.That(config.ServerName, Is.EqualTo("My server"));
            Assert.That(config.Password, Is.EqualTo("pw"));
        }

        [Test]
        public void Environment_ReadsTheServerPassword()
        {
            var config = new NetConfig();
            NetEnvironment.Apply(config, Env(new Dictionary<string, string> { { "MINDATTIC_SERVER_PASSWORD", "pw" } }));
            Assert.That(config.Password, Is.EqualTo("pw"));
        }

        // --- paths ------------------------------------------------------------------------------------------------

        [Test]
        public void Paths_ResolveRelativeAndKeepAbsolute_AndSavePathStaysInside()
        {
            string root = Path.Combine(Path.GetTempPath(), "pkg");
            string absoluteLogs = Path.Combine(Path.GetTempPath(), "elsewhere", "logs");
            var paths = new ServerPaths(root, new ServerSettings { LogsDirectory = absoluteLogs, SavesDirectory = "Data/Saves" });

            Assert.That(paths.LogsDirectory, Is.EqualTo(Path.GetFullPath(absoluteLogs)));
            Assert.That(paths.SavesDirectory, Is.EqualTo(Path.GetFullPath(Path.Combine(root, "Data", "Saves"))));
            Assert.That(paths.SavePath("world.dat"), Is.EqualTo(Path.Combine(paths.SavesDirectory, "world.dat")));
            Assert.Throws<ArgumentException>(() => paths.SavePath("../escape.dat"));
            Assert.Throws<ArgumentException>(() => paths.SavePath(absoluteLogs));
        }

        [Test]
        public void Paths_EnsureCreated_MakesBothFolders()
        {
            string root = Path.Combine(Path.GetTempPath(), "MindAtticPaths-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = new ServerPaths(root, new ServerSettings());
                paths.EnsureCreated();
                Assert.That(Directory.Exists(paths.LogsDirectory), Is.True);
                Assert.That(Directory.Exists(paths.SavesDirectory), Is.True);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
