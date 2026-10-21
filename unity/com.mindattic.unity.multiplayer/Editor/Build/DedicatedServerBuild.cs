using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Multiplayer.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Editor
{
    /// <summary>What <see cref="DedicatedServerBuild.Build"/> builds.</summary>
    public sealed class DedicatedServerBuildOptions
    {
        /// <summary>The folder the build and its Dockerfile go in (relative to the project folder unless rooted).</summary>
        public string OutputDirectory { get; set; } = DedicatedServerBuild.DefaultOutputDirectory;

        /// <summary>The server executable's file name. Null: the product name, made file-safe, plus <c>.x86_64</c>.</summary>
        public string ExecutableName { get; set; }

        /// <summary>The scenes to build. Null: the enabled scenes in Build Settings.</summary>
        public string[] Scenes { get; set; }

        /// <summary>The port the container's server listens on and exposes.</summary>
        public ushort Port { get; set; } = NetConfig.DefaultPort;

        /// <summary>UDP, or WebSocket (the Dockerfile then exposes the port over TCP).</summary>
        public TransportKind Transport { get; set; } = TransportKind.Udp;

        /// <summary>Mono or IL2CPP for this build only. Null: the project's Dedicated Server setting.</summary>
        public ScriptingImplementation? ScriptingBackend { get; set; }

        /// <summary>A development build (keeps the network simulator stage and debug logging).</summary>
        public bool Development { get; set; }

        /// <summary>Run the <see cref="NetPrefabValidator"/> over Assets first and refuse to build on errors.</summary>
        public bool ValidatePrefabs { get; set; } = true;

        /// <summary>Write the Dockerfile and .dockerignore beside the build.</summary>
        public bool WriteDockerfile { get; set; } = true;

        /// <summary>The Docker base image. Null: <see cref="ServerDockerfile.DefaultBaseImage"/>.</summary>
        public string BaseImage { get; set; }

        /// <summary>More server arguments for the Dockerfile's entrypoint.</summary>
        public string[] ExtraArgs { get; set; }
    }

    /// <summary>What a dedicated server build produced.</summary>
    public sealed class DedicatedServerBuildResult
    {
        internal DedicatedServerBuildResult()
        {
        }

        /// <summary>True when the player built (and the Dockerfile, when asked for, was written).</summary>
        public bool Succeeded { get; internal set; }

        /// <summary>Why it failed; null on success.</summary>
        public string Error { get; internal set; }

        /// <summary>The full path of the server executable.</summary>
        public string ExecutablePath { get; internal set; }

        /// <summary>The full path of the Dockerfile, or null when none was written.</summary>
        public string DockerfilePath { get; internal set; }

        /// <summary>Unity's build report, or null when the build never started.</summary>
        public BuildReport Report { get; internal set; }

        /// <summary>The prefab validation run first, or null when it was skipped.</summary>
        public NetPrefabValidationReport Validation { get; internal set; }
    }

    /// <summary>
    /// MindAttic ▸ Multiplayer ▸ Build Dedicated Server (Linux): a headless Linux x86_64 Dedicated Server build of the
    /// enabled Build Settings scenes, with a Dockerfile and .dockerignore beside it so the folder builds straight into an
    /// image. The port and transport come from the open scene's <see cref="NetBootstrap"/>. CI calls
    /// <see cref="BuildFromCommandLine"/>.
    /// </summary>
    public static class DedicatedServerBuild
    {
        /// <summary>The menu item.</summary>
        public const string MenuPath = "MindAttic/Multiplayer/Build Dedicated Server (Linux)";

        /// <summary>Where the build goes by default, relative to the project folder.</summary>
        public const string DefaultOutputDirectory = "Builds/LinuxServer";

        /// <summary>The extension Unity gives a Linux x86_64 executable.</summary>
        public const string ExecutableExtension = ".x86_64";

        /// <summary><see cref="BuildFromCommandLine"/>: the output folder.</summary>
        public const string OutputArg = "-serverOutput";

        /// <summary><see cref="BuildFromCommandLine"/>: <c>mono</c> or <c>il2cpp</c>.</summary>
        public const string BackendArg = "-serverBackend";

        /// <summary><see cref="BuildFromCommandLine"/>: a development build.</summary>
        public const string DevelopmentArg = "-serverDevelopment";

        [MenuItem(MenuPath, false, 40)]
        private static void BuildMenu()
        {
            if (!IsLinuxServerSupported())
            {
                EditorUtility.DisplayDialog("Build Dedicated Server (Linux)",
                    "The Linux Dedicated Server build support module is not installed. Add \"Linux Dedicated Server Build Support\" to this editor in Unity Hub.", "OK");
                return;
            }

            DedicatedServerBuildOptions options = OptionsFromScene(SceneManager.GetActiveScene());
            DedicatedServerBuildResult result = Build(options);
            if (result.Succeeded)
            {
                Debug.Log("[MindAttic.Multiplayer] Dedicated server built: " + result.ExecutablePath
                    + (result.DockerfilePath != null ? "\nDocker: cd \"" + Path.GetDirectoryName(result.DockerfilePath) + "\" && docker build -t game-server ." : string.Empty));
                EditorUtility.RevealInFinder(result.ExecutablePath);
            }
            else
            {
                Debug.LogError("[MindAttic.Multiplayer] Dedicated server build failed: " + result.Error);
                EditorUtility.DisplayDialog("Build Dedicated Server (Linux)", result.Error, "OK");
            }
        }

        /// <summary>
        /// Batch-mode entry point: <c>Unity -batchmode -quit -projectPath . -executeMethod
        /// Unity.Multiplayer.Editor.DedicatedServerBuild.BuildFromCommandLine [-serverOutput dir] [-port n]
        /// [-transport udp|websocket] [-serverBackend mono|il2cpp] [-serverDevelopment]</c>. Exits 1 on failure.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            DedicatedServerBuildOptions options = OptionsFromArgs(Environment.GetCommandLineArgs(), out string error);
            DedicatedServerBuildResult result = error == null ? Build(options) : null;
            if (result != null && result.Succeeded)
            {
                Debug.Log("[MindAttic.Multiplayer] Dedicated server built: " + result.ExecutablePath);
                return;
            }

            Debug.LogError("[MindAttic.Multiplayer] Dedicated server build failed: " + (error ?? result.Error));
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Reads <see cref="BuildFromCommandLine"/>'s arguments; <paramref name="error"/> names the first bad one.
        /// Starts from the open scene's NetBootstrap (<see cref="OptionsFromScene"/>), same as the menu command, so
        /// a batch build that omits <c>-port</c>/<c>-transport</c> still matches the scene instead of silently
        /// falling back to <see cref="NetConfig.DefaultPort"/>/UDP; either flag overrides it when given.
        /// </summary>
        public static DedicatedServerBuildOptions OptionsFromArgs(string[] args, out string error)
        {
            error = null;
            DedicatedServerBuildOptions options = OptionsFromScene(SceneManager.GetActiveScene());
            args = args ?? new string[0];
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : null;
                if (Is(arg, OutputArg) && value != null)
                {
                    options.OutputDirectory = value;
                    i++;
                }
                else if (Is(arg, NetCommandLine.PortArg) && value != null)
                {
                    if (!ushort.TryParse(value, out ushort port) || port == 0)
                    {
                        error = NetCommandLine.PortArg + " '" + value + "' is not a port (1-65535).";
                        return options;
                    }

                    options.Port = port;
                    i++;
                }
                else if (Is(arg, NetCommandLine.TransportArg) && value != null)
                {
                    if (Is(value, "udp"))
                    {
                        options.Transport = TransportKind.Udp;
                    }
                    else if (Is(value, "websocket") || Is(value, "ws"))
                    {
                        options.Transport = TransportKind.WebSocket;
                    }
                    else
                    {
                        error = NetCommandLine.TransportArg + " '" + value + "' is not udp or websocket.";
                        return options;
                    }

                    i++;
                }
                else if (Is(arg, BackendArg) && value != null)
                {
                    if (Is(value, "mono"))
                    {
                        options.ScriptingBackend = ScriptingImplementation.Mono2x;
                    }
                    else if (Is(value, "il2cpp"))
                    {
                        options.ScriptingBackend = ScriptingImplementation.IL2CPP;
                    }
                    else
                    {
                        error = BackendArg + " '" + value + "' is not mono or il2cpp.";
                        return options;
                    }

                    i++;
                }
                else if (Is(arg, DevelopmentArg))
                {
                    options.Development = true;
                }
            }

            return options;
        }

        /// <summary>Default options with the port and transport of <paramref name="scene"/>'s NetBootstrap, when it has one.</summary>
        public static DedicatedServerBuildOptions OptionsFromScene(Scene scene)
        {
            var options = new DedicatedServerBuildOptions();
            NetBootstrap bootstrap = FindBootstrap(scene);
            if (bootstrap != null)
            {
                NetConfig config = bootstrap.DefaultConfig;
                options.Port = config.Port;
                options.Transport = config.Transport;
            }

            return options;
        }

        /// <summary>
        /// The first NetBootstrap in <paramref name="scene"/> (inactive ones included), or null when the scene is not
        /// loaded or has none. Its <see cref="NetBootstrap.DefaultConfig"/> (the Inspector values, or the last
        /// <c>Configure</c>, never the editor's own command line) is the one home every build reads the scene's
        /// session settings from (UM-T50).
        /// </summary>
        public static NetBootstrap FindBootstrap(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                NetBootstrap bootstrap = root.GetComponentInChildren<NetBootstrap>(true);
                if (bootstrap != null)
                {
                    return bootstrap;
                }
            }

            return null;
        }

        /// <summary>True when this editor can make Linux Dedicated Server builds.</summary>
        public static bool IsLinuxServerSupported()
        {
            return BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
        }

        /// <summary>
        /// A file-safe executable name from <paramref name="productName"/>: letters, digits, '.', '_' and '-' kept, runs of
        /// anything else become one '_'; "Server" when nothing is left.
        /// </summary>
        public static string SanitizeExecutableName(string productName)
        {
            var name = new StringBuilder();
            bool lastReplaced = false;
            foreach (char c in productName ?? string.Empty)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (ok)
                {
                    name.Append(c);
                    lastReplaced = false;
                }
                else if (!lastReplaced)
                {
                    name.Append('_');
                    lastReplaced = true;
                }
            }

            string result = name.ToString().Trim('_', '.');
            return result.Length == 0 ? "Server" : result;
        }

        /// <summary>The executable name <paramref name="options"/> builds.</summary>
        public static string ExecutableNameFor(DedicatedServerBuildOptions options)
        {
            string name = options != null && !string.IsNullOrWhiteSpace(options.ExecutableName)
                ? options.ExecutableName.Trim()
                : SanitizeExecutableName(PlayerSettings.productName) + ExecutableExtension;
            return name;
        }

        /// <summary>Validates the prefabs, builds the Linux Dedicated Server player and writes the Dockerfile beside it.</summary>
        public static DedicatedServerBuildResult Build(DedicatedServerBuildOptions options)
        {
            options = options ?? new DedicatedServerBuildOptions();
            var result = new DedicatedServerBuildResult();

            if (!IsLinuxServerSupported())
            {
                return Fail(result, "The Linux Dedicated Server build support module is not installed in this editor.");
            }

            string executable = ExecutableNameFor(options);
            if (SanitizeExecutableName(executable) != executable)
            {
                return Fail(result, "'" + executable + "' is not a valid executable name (letters, digits, '.', '_' and '-' only).");
            }

            string[] scenes = options.Scenes ?? EnabledScenes();
            if (scenes.Length == 0)
            {
                return Fail(result, "No scenes to build: enable at least one scene in File > Build Profiles (Build Settings).");
            }

            if (options.ValidatePrefabs)
            {
                result.Validation = NetPrefabValidator.ValidateProject();
                if (result.Validation.HasErrors)
                {
                    result.Validation.Log();
                    return Fail(result, "Fix the network prefab errors first (" + result.Validation.Summary + " See the Console.)");
                }
            }

            string output = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory) ? DefaultOutputDirectory : options.OutputDirectory);
            Directory.CreateDirectory(output);
            result.ExecutablePath = Path.Combine(output, executable);

            ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);
            bool changeBackend = options.ScriptingBackend.HasValue && options.ScriptingBackend.Value != previousBackend;
            try
            {
                if (changeBackend)
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, options.ScriptingBackend.Value);
                }

                result.Report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    target = BuildTarget.StandaloneLinux64,
                    subtarget = (int)StandaloneBuildSubtarget.Server,
                    locationPathName = result.ExecutablePath,
                    options = options.Development ? BuildOptions.Development : BuildOptions.None,
                });
            }
            finally
            {
                if (changeBackend)
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, previousBackend);
                }
            }

            BuildSummary summary = result.Report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                return Fail(result, "Unity's build " + summary.result + " with " + summary.totalErrors + " error(s); see the Console.");
            }

            if (options.WriteDockerfile)
            {
                result.DockerfilePath = Path.Combine(output, "Dockerfile");
                File.WriteAllText(result.DockerfilePath, ServerDockerfile.Compose(executable, options.Port, options.Transport, options.BaseImage, options.ExtraArgs));
                File.WriteAllText(Path.Combine(output, ".dockerignore"), ServerDockerfile.ComposeIgnore());
            }

            result.Succeeded = true;
            return result;
        }

        private static string[] EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }

        private static DedicatedServerBuildResult Fail(DedicatedServerBuildResult result, string error)
        {
            result.Succeeded = false;
            result.Error = error;
            return result;
        }

        private static bool Is(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
