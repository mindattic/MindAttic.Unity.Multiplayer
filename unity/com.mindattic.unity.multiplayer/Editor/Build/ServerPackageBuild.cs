using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Multiplayer.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Editor
{
    /// <summary>What <see cref="ServerPackageBuild.Build"/> builds.</summary>
    public sealed class ServerPackageBuildOptions
    {
        /// <summary>The hosting profile's id. Null: <see cref="HostingProfiles.Default"/> (GenericVps).</summary>
        public string ProfileId { get; set; }

        /// <summary>The package folder (relative to the project folder unless rooted). Null: <c>Builds/ServerPackage/&lt;profile&gt;</c>.</summary>
        public string OutputDirectory { get; set; }

        /// <summary>The server executable's file name. Null: the product name, made file-safe, plus <c>.x86_64</c>.</summary>
        public string ExecutableName { get; set; }

        /// <summary>The scenes to build. Null: the enabled scenes in Build Settings.</summary>
        public string[] Scenes { get; set; }

        /// <summary>The settings written to <c>server.json</c>. Null: the open scene's NetBootstrap Inspector values.</summary>
        public NetConfig Config { get; set; }

        /// <summary>The package settings written to <c>server.json</c>. Null: the defaults.</summary>
        public ServerSettings Settings { get; set; }

        /// <summary>Mono or IL2CPP for this build only. Null: the project's Dedicated Server setting.</summary>
        public ScriptingImplementation? ScriptingBackend { get; set; }

        /// <summary>A development build.</summary>
        public bool Development { get; set; }

        /// <summary>Run the prefab validator first and refuse to build on errors.</summary>
        public bool ValidatePrefabs { get; set; } = true;

        /// <summary>Write only the package's own files (server.json, start.sh, ...), not the Unity player: to refresh them after a settings change.</summary>
        public bool SkipPlayerBuild { get; set; }
    }

    /// <summary>What a server package build produced.</summary>
    public sealed class ServerPackageBuildResult
    {
        internal ServerPackageBuildResult()
        {
        }

        /// <summary>True when the player built (unless skipped) and every package file was written.</summary>
        public bool Succeeded { get; internal set; }

        /// <summary>Why it failed; null on success.</summary>
        public string Error { get; internal set; }

        /// <summary>The package spec built, or null when the options were refused before one existed.</summary>
        public ServerPackageSpec Spec { get; internal set; }

        /// <summary>The package folder, full path.</summary>
        public string OutputDirectory { get; internal set; }

        /// <summary>The package files written, relative to <see cref="OutputDirectory"/>.</summary>
        public IReadOnlyList<string> WrittenFiles { get; internal set; } = new string[0];

        /// <summary>The player build, or null when it was skipped or never started.</summary>
        public DedicatedServerBuildResult PlayerBuild { get; internal set; }
    }

    /// <summary>
    /// MindAttic ▸ Multiplayer ▸ Build Server Package ▸ &lt;profile&gt;: the provider-neutral server package
    /// (<see cref="ServerPackageSpec"/>) for one hosting profile. It makes the Linux x86_64 Dedicated Server build of
    /// <see cref="DedicatedServerBuild"/> (UM-T15) and writes <c>server.json</c>, <c>start.sh</c>, the manifest,
    /// <c>Logs/</c>, <c>Saves/</c> and the profile's files beside it. CI calls <see cref="BuildFromCommandLine"/>.
    /// </summary>
    /// <remarks>
    /// A later profile registers itself with <see cref="HostingProfiles.Register"/> and adds its own menu item that calls
    /// <see cref="Build"/> with its id; nothing here or in any game changes.
    /// </remarks>
    public static class ServerPackageBuild
    {
        /// <summary>The submenu holding one item per profile.</summary>
        public const string MenuRoot = "MindAttic/Multiplayer/Build Server Package/";

        /// <summary>Where packages go by default, relative to the project folder; the profile's id is appended.</summary>
        public const string DefaultOutputRoot = "Builds/ServerPackage";

        /// <summary><see cref="BuildFromCommandLine"/>: the hosting profile's id.</summary>
        public const string ProfileArg = "-serverProfile";

        [MenuItem(MenuRoot + "GenericVps (default)", false, 41)]
        private static void BuildGenericVpsMenu()
        {
            BuildMenu(GenericVpsProfile.ProfileId);
        }

        [MenuItem(MenuRoot + "RunPod", false, 42)]
        private static void BuildRunPodMenu()
        {
            BuildMenu(RunPodProfile.ProfileId);
        }

        /// <summary>Builds the package for <paramref name="profileId"/> from the open scene, reporting in a dialog and the Console.</summary>
        public static void BuildMenu(string profileId)
        {
            const string title = "Build Server Package";
            if (!DedicatedServerBuild.IsLinuxServerSupported())
            {
                EditorUtility.DisplayDialog(title,
                    "The Linux Dedicated Server build support module is not installed. Add \"Linux Dedicated Server Build Support\" to this editor in Unity Hub.", "OK");
                return;
            }

            ServerPackageBuildResult result = Build(new ServerPackageBuildOptions
            {
                ProfileId = profileId,
                Config = ConfigFromScene(SceneManager.GetActiveScene()),
            });

            if (result.Succeeded)
            {
                Debug.Log("[MindAttic.Multiplayer] Server package (" + result.Spec.Profile.Id + ") built in " + result.OutputDirectory
                    + "\nPorts: " + string.Join(", ", PortList(result.Spec)) + ". Start it with ./start.sh, or see HOSTING.txt.");
                EditorUtility.RevealInFinder(Path.Combine(result.OutputDirectory, ServerPackageSpec.StartScriptName));
            }
            else
            {
                Debug.LogError("[MindAttic.Multiplayer] Server package build failed: " + result.Error);
                EditorUtility.DisplayDialog(title, result.Error, "OK");
            }
        }

        /// <summary>
        /// Batch-mode entry point: <c>Unity -batchmode -quit -projectPath . -executeMethod
        /// Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine [-serverProfile GenericVps|RunPod]
        /// [-serverOutput dir] [-port n] [-transport udp|websocket] [-serverBackend mono|il2cpp] [-serverDevelopment]</c>.
        /// Exits 1 on failure.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            ServerPackageBuildOptions options = OptionsFromArgs(args, out string error);
            ServerPackageBuildResult result = error == null ? Build(options) : null;
            if (result != null && result.Succeeded)
            {
                Debug.Log("[MindAttic.Multiplayer] Server package (" + result.Spec.Profile.Id + ") built in " + result.OutputDirectory);
                return;
            }

            Debug.LogError("[MindAttic.Multiplayer] Server package build failed: " + (error ?? result.Error));
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Reads <see cref="BuildFromCommandLine"/>'s arguments; <paramref name="error"/> names the first bad one.</summary>
        public static ServerPackageBuildOptions OptionsFromArgs(string[] args, out string error)
        {
            args = args ?? new string[0];
            DedicatedServerBuildOptions server = DedicatedServerBuild.OptionsFromArgs(args, out error);
            var options = new ServerPackageBuildOptions
            {
                ScriptingBackend = server.ScriptingBackend,
                Development = server.Development,
                Config = new NetConfig { Port = server.Port, Transport = server.Transport },
            };

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], DedicatedServerBuild.OutputArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.OutputDirectory = args[i + 1];
                }
                else if (string.Equals(args[i], ProfileArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.ProfileId = args[i + 1];
                    if (error == null && HostingProfiles.Find(options.ProfileId) == null)
                    {
                        error = ProfileArg + " '" + options.ProfileId + "' is not a hosting profile (" + string.Join(", ", ProfileIds()) + ").";
                    }
                }
            }

            return options;
        }

        /// <summary>
        /// The session settings of <paramref name="scene"/>'s NetBootstrap as Inspector values (port, transport, tick
        /// rate, player cap, server name, reconnect grace, join codes, rendezvous URL, public address and port); defaults
        /// when the scene has none.
        /// </summary>
        public static NetConfig ConfigFromScene(Scene scene)
        {
            var config = new NetConfig { Topology = Topology.Server };
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return config;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                NetBootstrap bootstrap = root.GetComponentInChildren<NetBootstrap>(true);
                if (bootstrap == null)
                {
                    continue;
                }

                // Serialized values only: BuildConfig would fold in the editor's own command line.
                var serialized = new SerializedObject(bootstrap);
                int port = serialized.FindProperty("port").intValue;
                if (port > 0 && port <= ushort.MaxValue)
                {
                    config.Port = (ushort)port;
                }

                config.Transport = (TransportKind)serialized.FindProperty("transport").intValue;
                config.TickRate = Mathf.Clamp(serialized.FindProperty("tickRate").intValue, 1, NetConfig.MaxTickRate);
                config.MaxPlayers = Mathf.Clamp(serialized.FindProperty("maxPlayers").intValue, 1, NetConfig.MaxMaxPlayers);
                config.ReconnectGraceSeconds = Mathf.Max(0f, serialized.FindProperty("reconnectGraceSeconds").floatValue);
                config.JoinCodes = (JoinCodeProvider)serialized.FindProperty("joinCodes").intValue;
                config.ServerName = NullIfBlank(serialized.FindProperty("serverName").stringValue);
                config.RendezvousUrl = NullIfBlank(serialized.FindProperty("rendezvousUrl").stringValue);
                config.PublicAddress = NullIfBlank(serialized.FindProperty("publicAddress").stringValue);
                config.PublicPort = (ushort)Mathf.Clamp(serialized.FindProperty("publicPort").intValue, 0, ushort.MaxValue);
                break;
            }

            return config;
        }

        /// <summary>Builds the player (unless skipped) and writes the package files for the chosen profile.</summary>
        public static ServerPackageBuildResult Build(ServerPackageBuildOptions options)
        {
            options = options ?? new ServerPackageBuildOptions();
            var result = new ServerPackageBuildResult();

            IHostingProfile profile = string.IsNullOrWhiteSpace(options.ProfileId) ? HostingProfiles.Default : HostingProfiles.Find(options.ProfileId.Trim());
            if (profile == null)
            {
                return Fail(result, "'" + options.ProfileId + "' is not a hosting profile (" + string.Join(", ", ProfileIds()) + ").");
            }

            var serverOptions = new DedicatedServerBuildOptions
            {
                ExecutableName = options.ExecutableName,
                Scenes = options.Scenes,
                ScriptingBackend = options.ScriptingBackend,
                Development = options.Development,
                ValidatePrefabs = options.ValidatePrefabs,
                WriteDockerfile = false,
            };

            string executable = DedicatedServerBuild.ExecutableNameFor(serverOptions);
            if (!ServerPackageSpec.IsSafeFileName(executable))
            {
                return Fail(result, "'" + executable + "' is not a valid executable name (letters, digits, '.', '_' and '-' only).");
            }

            NetConfig config = options.Config ?? ConfigFromScene(SceneManager.GetActiveScene());
            ServerPackageSpec spec = ServerPackageSpec.For(profile, executable, config, options.Settings);
            result.Spec = spec;
            IReadOnlyList<string> problems = spec.Validate();
            if (problems.Count > 0)
            {
                return Fail(result, "The server settings are not valid: " + string.Join(" ", problems));
            }

            string output = string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? DefaultOutputRoot + "/" + profile.Id
                : options.OutputDirectory;
            result.OutputDirectory = Path.GetFullPath(output);
            serverOptions.OutputDirectory = result.OutputDirectory;

            if (!options.SkipPlayerBuild)
            {
                result.PlayerBuild = DedicatedServerBuild.Build(serverOptions);
                if (!result.PlayerBuild.Succeeded)
                {
                    return Fail(result, result.PlayerBuild.Error);
                }
            }

            try
            {
                result.WrittenFiles = WriteFiles(spec, result.OutputDirectory);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                return Fail(result, "Could not write the package files: " + ex.Message);
            }

            result.Succeeded = true;
            return result;
        }

        /// <summary>
        /// Writes <paramref name="spec"/>'s files into <paramref name="outputDirectory"/> (UTF-8 without a byte order mark,
        /// LF line endings) and creates its folders, leaving anything already in them (such as saves) alone.
        /// </summary>
        /// <returns>The paths written, relative to the folder.</returns>
        public static IReadOnlyList<string> WriteFiles(ServerPackageSpec spec, string outputDirectory)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            Directory.CreateDirectory(outputDirectory);
            var written = new List<string>();
            var utf8 = new UTF8Encoding(false);
            foreach (ServerPackageFile file in spec.ComposeFiles())
            {
                string path = Path.Combine(outputDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (file.IsDirectory)
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, file.Contents.Replace("\r\n", "\n"), utf8);
                }

                written.Add(file.ToString());
            }

            return written;
        }

        /// <summary>The entries of <paramref name="spec"/>'s <see cref="ServerPackageSpec.ExpectedLayout"/> missing from <paramref name="outputDirectory"/>.</summary>
        public static IReadOnlyList<string> MissingFromLayout(ServerPackageSpec spec, string outputDirectory)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var missing = new List<string>();
            foreach (ServerPackageFile entry in spec.ExpectedLayout())
            {
                string path = Path.Combine(outputDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                bool exists = entry.IsDirectory ? Directory.Exists(path) : File.Exists(path);
                if (!exists)
                {
                    missing.Add(entry.ToString());
                }
            }

            return missing;
        }

        private static IEnumerable<string> ProfileIds()
        {
            foreach (IHostingProfile profile in HostingProfiles.All)
            {
                yield return profile.Id;
            }
        }

        private static IEnumerable<string> PortList(ServerPackageSpec spec)
        {
            foreach (DeclaredPort port in spec.Ports)
            {
                yield return port.ToString();
            }
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static ServerPackageBuildResult Fail(ServerPackageBuildResult result, string error)
        {
            result.Succeeded = false;
            result.Error = error;
            return result;
        }
    }
}
