using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Tests;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Editor.Tests
{
    public class ServerPackageBuildTests
    {
        private string _output;

        [SetUp]
        public void SetUp()
        {
            _output = Path.Combine(Path.GetTempPath(), "MindAtticServerPackage-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            TempAssets.Delete();
            if (Directory.Exists(_output))
            {
                Directory.Delete(_output, true);
            }
        }

        [Test]
        public void Menu_HasOneItemPerBuiltInProfile_UnderBuildServerPackage()
        {
            List<string> paths = typeof(ServerPackageBuild).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(m => m.GetCustomAttributes<MenuItem>())
                .Select(a => a.menuItem)
                .ToList();

            Assert.That(paths, Is.EquivalentTo(new[]
            {
                "MindAttic/Multiplayer/Build Server Package/GenericVps (default)",
                "MindAttic/Multiplayer/Build Server Package/RunPod",
            }));
        }

        [Test]
        public void ServerSceneFor_PrefersTheServerSceneArgument()
        {
            string scene = ServerPackageBuild.ServerSceneFor(
                new[] { "Unity", "-serverScene", @"Assets\Game\Server.unity" }, "Assets/Other.unity", new[] { "Assets/Game/Main.unity" });

            Assert.That(scene, Is.EqualTo("Assets/Game/Server.unity"));
        }

        [Test]
        public void ServerSceneFor_OpensTheFirstBuildSettingsScene_OnlyWhenTheEditorStartedUntitled()
        {
            string[] enabled = { "Assets/Game/Main.unity", "Assets/Game/Level2.unity" };

            Assert.That(ServerPackageBuild.ServerSceneFor(new string[0], "", enabled), Is.EqualTo("Assets/Game/Main.unity"));
            Assert.That(ServerPackageBuild.ServerSceneFor(new string[0], "Assets/Open.unity", enabled), Is.Null, "an open, saved scene is kept, as in the menu build");
            Assert.That(ServerPackageBuild.ServerSceneFor(new string[0], "", new string[0]), Is.Null);
        }

        [Test]
        public void ScenesWithServerFirst_PutsTheServerSceneFirst_WithoutDuplicates()
        {
            string[] scenes = ServerPackageBuild.ScenesWithServerFirst("Assets/B.unity", new[] { "Assets/A.unity", "Assets/B.unity", "Assets/C.unity" });

            Assert.That(scenes, Is.EqualTo(new[] { "Assets/B.unity", "Assets/A.unity", "Assets/C.unity" }));
            Assert.That(ServerPackageBuild.ScenesWithServerFirst("Assets/X.unity", null), Is.EqualTo(new[] { "Assets/X.unity" }));
        }

        [Test]
        public void ConfigFromScene_TakesTheNetBootstrapInspectorValues()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Assert.That(ServerPackageBuild.ConfigFromScene(scene).Port, Is.EqualTo(NetConfig.DefaultPort));

                var go = new GameObject("Net", typeof(NetworkManager));
                var serialized = new SerializedObject(go.AddComponent<NetBootstrap>());
                serialized.FindProperty("port").intValue = 7900;
                serialized.FindProperty("maxPlayers").intValue = 6;
                serialized.FindProperty("tickRate").intValue = 20;
                serialized.FindProperty("serverName").stringValue = " Friday ";
                serialized.FindProperty("rendezvousUrl").stringValue = "https://codes.example.com";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                NetConfig config = ServerPackageBuild.ConfigFromScene(scene);

                Assert.That(config.Topology, Is.EqualTo(Topology.Server));
                Assert.That(config.Port, Is.EqualTo(7900));
                Assert.That(config.MaxPlayers, Is.EqualTo(6));
                Assert.That(config.TickRate, Is.EqualTo(20));
                Assert.That(config.ServerName, Is.EqualTo("Friday"));
                Assert.That(config.RendezvousUrl, Is.EqualTo("https://codes.example.com"));
                Assert.That(config.PublicAddress, Is.Null);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void OptionsFromArgs_SeedsTheWholeConfigFromTheOpenScene_AndFlagsOverridePortAndTransport()
        {
            // UM-T50: a batch/CI build used to seed only Port/Transport from the scene, so every other
            // setting in server.json fell back to defaults and disagreed with the menu build.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var go = new GameObject("Net", typeof(NetworkManager));
                var serialized = new SerializedObject(go.AddComponent<NetBootstrap>());
                serialized.FindProperty("port").intValue = 7900;
                serialized.FindProperty("transport").intValue = (int)TransportKind.WebSocket;
                serialized.FindProperty("tickRate").intValue = 20;
                serialized.FindProperty("maxPlayers").intValue = 6;
                serialized.FindProperty("gameVersion").stringValue = "2.5";
                serialized.FindProperty("serverName").stringValue = "Friday";
                serialized.FindProperty("reconnectGraceSeconds").floatValue = 45f;
                serialized.FindProperty("joinCodes").intValue = (int)JoinCodeProvider.Rendezvous;
                serialized.FindProperty("rendezvousUrl").stringValue = "https://codes.example.com";
                serialized.FindProperty("publicAddress").stringValue = "203.0.113.7";
                serialized.FindProperty("publicPort").intValue = 30000;
                serialized.FindProperty("conditions").intValue = (int)NetConditionsProfile.Custom;
                serialized.FindProperty("simulatedLatencyMs").intValue = 80;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                NetConfig menu = ServerPackageBuild.ConfigFromScene(scene);
                ServerPackageBuildOptions batch = ServerPackageBuild.OptionsFromArgs(new[] { "Unity.exe" }, out string error);

                Assert.That(error, Is.Null);
                Assert.That(batch.Config.ToString(), Is.EqualTo(menu.ToString()));
                Assert.That(batch.Config.Port, Is.EqualTo(7900));
                Assert.That(batch.Config.Transport, Is.EqualTo(TransportKind.WebSocket));
                Assert.That(batch.Config.TickRate, Is.EqualTo(20));
                Assert.That(batch.Config.MaxPlayers, Is.EqualTo(6));
                Assert.That(batch.Config.GameVersion, Is.EqualTo("2.5"));
                Assert.That(batch.Config.ServerName, Is.EqualTo("Friday"));
                Assert.That(batch.Config.ReconnectGraceSeconds, Is.EqualTo(45).Within(0.001));
                Assert.That(batch.Config.JoinCodes, Is.EqualTo(JoinCodeProvider.Rendezvous));
                Assert.That(batch.Config.RendezvousUrl, Is.EqualTo("https://codes.example.com"));
                Assert.That(batch.Config.PublicAddress, Is.EqualTo("203.0.113.7"));
                Assert.That(batch.Config.PublicPort, Is.EqualTo(30000));
                Assert.That(batch.Config.Conditions, Is.EqualTo(NetConditionsProfile.Custom));
                Assert.That(batch.Config.SimulatedLatencyMs, Is.EqualTo(80));

                ServerPackageBuildOptions overridden = ServerPackageBuild.OptionsFromArgs(
                    new[] { "Unity.exe", "-port", "9100", "-transport", "udp" }, out error);
                Assert.That(error, Is.Null);
                Assert.That(overridden.Config.Port, Is.EqualTo(9100));
                Assert.That(overridden.Config.Transport, Is.EqualTo(TransportKind.Udp));
                Assert.That(overridden.Config.TickRate, Is.EqualTo(20));
                Assert.That(overridden.Config.GameVersion, Is.EqualTo("2.5"));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void OptionsFromArgs_ReadsTheProfileAndRefusesAnUnknownOne()
        {
            ServerPackageBuildOptions options = ServerPackageBuild.OptionsFromArgs(
                new[] { "Unity.exe", "-serverProfile", "RunPod", "-serverOutput", "out/pkg", "-port", "9001", "-serverBackend", "mono" }, out string error);

            Assert.That(error, Is.Null);
            Assert.That(options.ProfileId, Is.EqualTo("RunPod"));
            Assert.That(options.OutputDirectory, Is.EqualTo("out/pkg"));
            Assert.That(options.Config.Port, Is.EqualTo(9001));
            Assert.That(options.ScriptingBackend, Is.EqualTo(ScriptingImplementation.Mono2x));

            ServerPackageBuild.OptionsFromArgs(new[] { "-serverProfile", "GPortal" }, out error);
            Assert.That(error, Does.Contain("'GPortal' is not a hosting profile"));
        }

        [Test]
        public void Build_UnknownProfile_Fails()
        {
            ServerPackageBuildResult result = ServerPackageBuild.Build(new ServerPackageBuildOptions { ProfileId = "Nope", OutputDirectory = _output, SkipPlayerBuild = true });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("'Nope' is not a hosting profile"));
            Assert.That(Directory.Exists(_output), Is.False);
        }

        [Test]
        public void Build_InvalidSettings_FailsBeforeWritingAnything()
        {
            ServerPackageBuildResult result = ServerPackageBuild.Build(new ServerPackageBuildOptions
            {
                OutputDirectory = _output,
                SkipPlayerBuild = true,
                Config = new NetConfig { MaxPlayers = 0 },
            });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("MaxPlayers"));
            Assert.That(Directory.Exists(_output), Is.False);
        }

        [Test]
        public void Build_GenericVpsIsTheDefault_AndWritesThePackageFiles()
        {
            ServerPackageBuildResult result = ServerPackageBuild.Build(new ServerPackageBuildOptions
            {
                OutputDirectory = _output,
                ExecutableName = "Game.x86_64",
                SkipPlayerBuild = true,
                Config = new NetConfig { ServerName = "Friday", MaxPlayers = 4 },
            });

            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(result.Spec.Profile.Id, Is.EqualTo("GenericVps"));
            Assert.That(result.PlayerBuild, Is.Null);
            Assert.That(result.WrittenFiles, Is.EqualTo(new[]
            {
                "server.json", "start.sh", "server-package.json", "Logs/", "Saves/", "Dockerfile", ".dockerignore", "docker-compose.yml", "HOSTING.txt",
            }));

            foreach (ServerPackageFile file in result.Spec.ComposeFiles())
            {
                string path = Path.Combine(_output, file.RelativePath);
                if (file.IsDirectory)
                {
                    Assert.That(Directory.Exists(path), Is.True, file.RelativePath);
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(path);
                Assert.That(bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB, Is.False, file.RelativePath + " has a byte order mark");
                Assert.That(File.ReadAllText(path), Is.EqualTo(file.Contents), file.RelativePath);
            }

            var back = new NetConfig();
            ServerConfigFile.Apply(File.ReadAllText(Path.Combine(_output, "server.json")), back, new ServerSettings());
            Assert.That(back.ServerName, Is.EqualTo("Friday"));
            Assert.That(back.MaxPlayers, Is.EqualTo(4));

            // Without the player the layout is incomplete in exactly Unity's three entries.
            Assert.That(ServerPackageBuild.MissingFromLayout(result.Spec, _output), Is.EqualTo(new[] { "Game.x86_64", "Game_Data/", "UnityPlayer.so" }));
        }

        [Test]
        public void Build_RunPodWritesWebSocketSettings()
        {
            ServerPackageBuildResult result = ServerPackageBuild.Build(new ServerPackageBuildOptions
            {
                ProfileId = "RunPod",
                OutputDirectory = _output,
                ExecutableName = "Game.x86_64",
                SkipPlayerBuild = true,
                Config = new NetConfig { Transport = TransportKind.Udp },
            });

            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(File.ReadAllText(Path.Combine(_output, "server.json")), Does.Contain("\"transport\": \"websocket\""));
            Assert.That(File.Exists(Path.Combine(_output, "docker-compose.yml")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(_output, "HOSTING.txt")), Does.Contain("RunPod"));
        }

        [Test]
        public void Rebuild_KeepsWhatIsInSaves()
        {
            var options = new ServerPackageBuildOptions { OutputDirectory = _output, ExecutableName = "Game.x86_64", SkipPlayerBuild = true, Config = new NetConfig() };
            Assert.That(ServerPackageBuild.Build(options).Succeeded, Is.True);
            string save = Path.Combine(_output, "Saves", "world.dat");
            File.WriteAllText(save, "keep me");

            Assert.That(ServerPackageBuild.Build(options).Succeeded, Is.True);

            Assert.That(File.ReadAllText(save), Is.EqualTo("keep me"));
        }

        /// <summary>
        /// A real headless Linux build (Mono) of a networked scene as a GenericVps package: every entry of the package
        /// layout exists, and no UM-T15 Dockerfile is left beside it.
        /// </summary>
        [Test]
        [Category("Build")]
        [Timeout(1800000)]
        public void Build_MakesACompleteGenericVpsPackage()
        {
            if (!DedicatedServerBuild.IsLinuxServerSupported())
            {
                Assert.Ignore("Linux Dedicated Server build support is not installed in this editor.");
            }

            TempAssets.Create();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = TempAssets.Folder + "/PackageScene.unity";
            try
            {
                var avatar = new GameObject("PackageAvatar", typeof(NetworkObject), typeof(PlainProbeEntity));
                TempAssets.SavePrefab(avatar);
                NetSceneSetup.Run(scene, new NetSceneSetupOptions { SearchFolders = TempAssets.Folders, PrefabListPath = TempAssets.Folder + "/NetPrefabs.asset" });
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                ScriptingImplementation before = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);

                ServerPackageBuildResult result = ServerPackageBuild.Build(new ServerPackageBuildOptions
                {
                    Scenes = new[] { scenePath },
                    OutputDirectory = _output,
                    ExecutableName = "PackageServer.x86_64",
                    ScriptingBackend = ScriptingImplementation.Mono2x,
                    Config = new NetConfig { Port = 7791, ServerName = "Build test" },
                });

                Assert.That(result.Succeeded, Is.True, result.Error);
                Assert.That(result.PlayerBuild, Is.Not.Null);
                Assert.That(result.PlayerBuild.DockerfilePath, Is.Null);
                Assert.That(ServerPackageBuild.MissingFromLayout(result.Spec, _output), Is.Empty);
                Assert.That(File.ReadAllText(Path.Combine(_output, "Dockerfile")), Is.EqualTo(ServerPackageDockerfile.Compose(result.Spec)));
                Assert.That(File.ReadAllText(Path.Combine(_output, "start.sh")), Does.Contain("exec ./PackageServer.x86_64 "));
                Assert.That(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server), Is.EqualTo(before));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
