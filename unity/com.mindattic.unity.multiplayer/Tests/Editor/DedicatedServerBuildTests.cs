using System;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Tests;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Editor.Tests
{
    public class DedicatedServerBuildTests
    {
        private string _output;

        [SetUp]
        public void SetUp()
        {
            _output = Path.Combine(Path.GetTempPath(), "MindAtticServerBuild-" + Guid.NewGuid().ToString("N"));
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

        // --- names and arguments ---------------------------------------------------------------------------------

        [TestCase("My Game", "My_Game")]
        [TestCase("Ünïcode  Game!", "n_code_Game")]
        [TestCase("ok.name-1_2", "ok.name-1_2")]
        [TestCase("  ", "Server")]
        [TestCase(null, "Server")]
        [TestCase("a/b\\c\"d", "a_b_c_d")]
        public void SanitizeExecutableName_KeepsOnlyFileSafeCharacters(string product, string expected)
        {
            Assert.That(DedicatedServerBuild.SanitizeExecutableName(product), Is.EqualTo(expected));
        }

        [Test]
        public void ExecutableName_DefaultsToTheProductNamePlusX86_64()
        {
            string expected = DedicatedServerBuild.SanitizeExecutableName(PlayerSettings.productName) + ".x86_64";
            Assert.That(DedicatedServerBuild.ExecutableNameFor(new DedicatedServerBuildOptions()), Is.EqualTo(expected));
            Assert.That(DedicatedServerBuild.ExecutableNameFor(new DedicatedServerBuildOptions { ExecutableName = "Srv.x86_64" }), Is.EqualTo("Srv.x86_64"));
        }

        [Test]
        public void OptionsFromArgs_ReadsTheCiArguments()
        {
            DedicatedServerBuildOptions options = DedicatedServerBuild.OptionsFromArgs(
                new[] { "Unity.exe", "-batchmode", "-serverOutput", "out/srv", "-port", "9000", "-transport", "ws", "-serverBackend", "MONO", "-serverDevelopment" },
                out string error);

            Assert.That(error, Is.Null);
            Assert.That(options.OutputDirectory, Is.EqualTo("out/srv"));
            Assert.That(options.Port, Is.EqualTo(9000));
            Assert.That(options.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(options.ScriptingBackend, Is.EqualTo(ScriptingImplementation.Mono2x));
            Assert.That(options.Development, Is.True);
        }

        [Test]
        public void OptionsFromArgs_DefaultsWithoutArguments()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DedicatedServerBuildOptions options = DedicatedServerBuild.OptionsFromArgs(new[] { "Unity.exe" }, out string error);

            Assert.That(error, Is.Null);
            Assert.That(options.OutputDirectory, Is.EqualTo(DedicatedServerBuild.DefaultOutputDirectory));
            Assert.That(options.Port, Is.EqualTo(NetConfig.DefaultPort));
            Assert.That(options.Transport, Is.EqualTo(TransportKind.Udp));
            Assert.That(options.ScriptingBackend, Is.Null);
        }

        [Test]
        public void OptionsFromArgs_FallsBackToTheOpenSceneWhenPortAndTransportAreOmitted()
        {
            // The menu command already reads the scene (OptionsFromScene); a batch build that omits
            // -port/-transport must match it too instead of silently defaulting to 7777/UDP.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var go = new GameObject("Net", typeof(NetworkManager));
                var serialized = new SerializedObject(go.AddComponent<NetBootstrap>());
                serialized.FindProperty("port").intValue = 8123;
                serialized.FindProperty("transport").intValue = (int)TransportKind.WebSocket;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                DedicatedServerBuildOptions options = DedicatedServerBuild.OptionsFromArgs(new[] { "Unity.exe" }, out string error);

                Assert.That(error, Is.Null);
                Assert.That(options.Port, Is.EqualTo(8123));
                Assert.That(options.Transport, Is.EqualTo(TransportKind.WebSocket));

                // An explicit CLI flag still overrides the scene.
                DedicatedServerBuildOptions overridden = DedicatedServerBuild.OptionsFromArgs(
                    new[] { "Unity.exe", "-port", "9000" }, out string overrideError);
                Assert.That(overrideError, Is.Null);
                Assert.That(overridden.Port, Is.EqualTo(9000));
                Assert.That(overridden.Transport, Is.EqualTo(TransportKind.WebSocket));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [TestCase("-port", "0", "-port '0'")]
        [TestCase("-port", "70000", "-port '70000'")]
        [TestCase("-transport", "tcp", "-transport 'tcp'")]
        [TestCase("-serverBackend", "dotnet", "-serverBackend 'dotnet'")]
        public void OptionsFromArgs_NamesABadArgument(string arg, string value, string expected)
        {
            DedicatedServerBuild.OptionsFromArgs(new[] { arg, value }, out string error);

            Assert.That(error, Does.StartWith(expected));
        }

        [Test]
        public void OptionsFromScene_TakesThePortAndTransportFromNetBootstrap()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Assert.That(DedicatedServerBuild.OptionsFromScene(scene).Port, Is.EqualTo(NetConfig.DefaultPort));

                var go = new GameObject("Net", typeof(NetworkManager));
                var serialized = new SerializedObject(go.AddComponent<NetBootstrap>());
                serialized.FindProperty("port").intValue = 8123;
                serialized.FindProperty("transport").intValue = (int)TransportKind.WebSocket;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                DedicatedServerBuildOptions options = DedicatedServerBuild.OptionsFromScene(scene);

                Assert.That(options.Port, Is.EqualTo(8123));
                Assert.That(options.Transport, Is.EqualTo(TransportKind.WebSocket));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        // --- Dockerfile ------------------------------------------------------------------------------------------

        [Test]
        public void Dockerfile_RunsTheHeadlessServerAsNonRootOnAUdpPort()
        {
            string dockerfile = ServerDockerfile.Compose("Game.x86_64", 7777, TransportKind.Udp);

            Assert.That(dockerfile, Does.Contain("\nFROM ubuntu:24.04\n"));
            Assert.That(dockerfile, Does.Contain("useradd --create-home --shell /usr/sbin/nologin server"));
            Assert.That(dockerfile, Does.Contain("\nWORKDIR /srv/game\n"));
            Assert.That(dockerfile, Does.Contain("\nCOPY --chown=server:server . .\n"));
            Assert.That(dockerfile, Does.Contain("\nRUN chmod +x Game.x86_64\n"));
            Assert.That(dockerfile, Does.Contain("\nUSER server\n"));
            Assert.That(dockerfile, Does.Contain("\nEXPOSE 7777/udp\n"));
            Assert.That(dockerfile, Does.Contain("docker run --rm -p 7777:7777/udp game-server"));
            Assert.That(dockerfile, Does.EndWith(
                "\nENTRYPOINT [\"./Game.x86_64\", \"-batchmode\", \"-nographics\", \"-logFile\", \"-\", \"-mode\", \"server\", \"-port\", \"7777\", \"-transport\", \"udp\"]\n"));
            Assert.That(dockerfile, Does.Not.Contain("\r"));
        }

        [Test]
        public void Dockerfile_ExposesTcpForWebSocketAndAppendsExtraArguments()
        {
            string dockerfile = ServerDockerfile.Compose("Game.x86_64", 8080, TransportKind.WebSocket, "debian:bookworm-slim", new[] { "-maxplayers", "4", "say \"hi\"" });

            Assert.That(dockerfile, Does.Contain("\nFROM debian:bookworm-slim\n"));
            Assert.That(dockerfile, Does.Contain("\nEXPOSE 8080/tcp\n"));
            Assert.That(dockerfile, Does.Contain("\"-port\", \"8080\", \"-transport\", \"websocket\", \"-maxplayers\", \"4\", \"say \\\"hi\\\"\"]"));
        }

        [Test]
        public void Dockerfile_RefusesAnUnsafeExecutableNameOrPortZero()
        {
            Assert.Throws<ArgumentException>(() => ServerDockerfile.Compose("my game", 7777, TransportKind.Udp));
            Assert.Throws<ArgumentException>(() => ServerDockerfile.Compose("", 7777, TransportKind.Udp));
            Assert.Throws<ArgumentOutOfRangeException>(() => ServerDockerfile.Compose("Game.x86_64", 0, TransportKind.Udp));
        }

        [Test]
        public void Dockerignore_KeepsUnityDebugFoldersOutOfTheImage()
        {
            string ignore = ServerDockerfile.ComposeIgnore();

            Assert.That(ignore, Does.Contain("*_BurstDebugInformation_DoNotShip\n"));
            Assert.That(ignore, Does.Contain("*_BackUpThisFolder_ButDontShipItWithYourGame\n"));
        }

        // --- the build -------------------------------------------------------------------------------------------

        [Test]
        public void Build_WithNoScenes_FailsBeforeBuilding()
        {
            DedicatedServerBuildResult result = DedicatedServerBuild.Build(new DedicatedServerBuildOptions { Scenes = new string[0], OutputDirectory = _output });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("No scenes"));
            Assert.That(result.Report, Is.Null);
            Assert.That(Directory.Exists(_output), Is.False);
        }

        [Test]
        public void Build_WithPrefabErrors_RefusesToBuild()
        {
            TempAssets.Create();
            // An entity with no NetworkObject (NGO itself registers every NetworkObject prefab in DefaultNetworkPrefabs).
            TempAssets.SavePrefab(new GameObject("NoNetworkObject", typeof(PlainProbeEntity)));

            LogAssert.Expect(LogType.Error, new Regex("Prefab 'NoNetworkObject' has networked components but no NetworkObject on its root"));
            DedicatedServerBuildResult result = DedicatedServerBuild.Build(new DedicatedServerBuildOptions { Scenes = new[] { "Assets/Nothing.unity" }, OutputDirectory = _output });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Validation, Is.Not.Null);
            Assert.That(result.Validation.HasErrors, Is.True);
            Assert.That(result.Error, Does.Contain("network prefab errors"));
            Assert.That(result.Report, Is.Null);
        }

        [Test]
        public void Build_WithAnInvalidExecutableName_Fails()
        {
            DedicatedServerBuildResult result = DedicatedServerBuild.Build(new DedicatedServerBuildOptions { ExecutableName = "bad name", OutputDirectory = _output });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("'bad name'"));
        }

        /// <summary>
        /// A real headless Linux build (Mono, for speed) of a scene the setup wizard networked: the executable, the
        /// Dockerfile and .dockerignore land in the output folder, and the project's server backend is left as it was.
        /// </summary>
        [Test]
        [Category("Build")]
        [Timeout(1800000)]
        public void Build_MakesAHeadlessLinuxServerWithADockerfile()
        {
            if (!DedicatedServerBuild.IsLinuxServerSupported())
            {
                Assert.Ignore("Linux Dedicated Server build support is not installed in this editor.");
            }

            TempAssets.Create();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = TempAssets.Folder + "/ServerScene.unity";
            try
            {
                var avatar = new GameObject("BuildAvatar", typeof(NetworkObject), typeof(PlainProbeEntity));
                TempAssets.SavePrefab(avatar);
                NetSceneSetup.Run(scene, new NetSceneSetupOptions { SearchFolders = TempAssets.Folders, PrefabListPath = TempAssets.Folder + "/NetPrefabs.asset" });
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);

                // Mono keeps the build fast; when the project is on IL2CPP this also checks the switch is undone.
                ScriptingImplementation before = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);

                DedicatedServerBuildResult result = DedicatedServerBuild.Build(new DedicatedServerBuildOptions
                {
                    Scenes = new[] { scenePath },
                    OutputDirectory = _output,
                    ExecutableName = "TestServer.x86_64",
                    ScriptingBackend = ScriptingImplementation.Mono2x,
                    Port = 7790,
                });

                Assert.That(result.Succeeded, Is.True, result.Error);
                Assert.That(result.Validation, Is.Not.Null);
                Assert.That(result.ExecutablePath, Is.EqualTo(Path.Combine(_output, "TestServer.x86_64")));
                Assert.That(File.Exists(result.ExecutablePath), Is.True);
                Assert.That(Directory.Exists(Path.Combine(_output, "TestServer_Data")), Is.True);
                Assert.That(File.ReadAllText(result.DockerfilePath), Is.EqualTo(ServerDockerfile.Compose("TestServer.x86_64", 7790, TransportKind.Udp)));
                Assert.That(File.ReadAllText(Path.Combine(_output, ".dockerignore")), Is.EqualTo(ServerDockerfile.ComposeIgnore()));
                Assert.That(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server), Is.EqualTo(before));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
