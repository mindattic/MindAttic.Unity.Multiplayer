using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity.Multiplayer.Editor
{
    /// <summary>
    /// The Linux player an end-to-end smoke runs (UM-T57): one StandaloneLinux64 build of the game plays every role, the
    /// dedicated server (<c>-mode server</c>) and each bot (<c>-bot &lt;role&gt;</c>). A plain player rather than the
    /// Dedicated Server subtarget, because GameCI's editor image has no Linux Dedicated Server build support; a container
    /// runs it with <c>SDL_VIDEODRIVER=dummy</c> (<c>mp-host game smoke</c>'s image sets it), since a player with no
    /// display otherwise crashes in SDL. No game needs a build method of its own.
    /// </summary>
    /// <remarks>
    /// Batch: <c>Unity -batchmode -quit -nographics -projectPath . -executeMethod
    /// Unity.Multiplayer.Editor.BotPlayerBuild.BuildFromCommandLine [-botOutput dir] [-botScene Assets/.../Main.unity]
    /// [-botDevelopment]</c>. The output defaults to <c>Builds/Smoke/player</c>; the scene to the first enabled Build
    /// Settings scene (the rest follow it). Exits 1 on failure.
    /// </remarks>
    public static class BotPlayerBuild
    {
        /// <summary>The executable's name in the output folder (its data folder is <c>Game_Data</c>).</summary>
        public const string ExecutableName = "Game.x86_64";

        /// <summary>The output folder argument.</summary>
        public const string OutputArg = "-botOutput";

        /// <summary>The scene the player boots (the rest of the Build Settings scenes follow it).</summary>
        public const string SceneArg = "-botScene";

        /// <summary>Builds a development player.</summary>
        public const string DevelopmentArg = "-botDevelopment";

        /// <summary>Where the build goes by default, under the project.</summary>
        public const string DefaultOutput = "Builds/Smoke/player";

        /// <summary>The batch entry point (see the class remarks).</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string output = Arg(args, OutputArg);
                if (string.IsNullOrWhiteSpace(output))
                {
                    output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DefaultOutput));
                }

                string[] scenes = ScenesFor(Arg(args, SceneArg), EnabledBuildScenes());
                if (scenes.Length == 0)
                {
                    Fail("no scene to build: enable one in Build Settings or pass " + SceneArg + " Assets/.../Main.unity.");
                    return;
                }

                if (!File.Exists(scenes[0]))
                {
                    Fail(SceneArg + " '" + scenes[0] + "' does not exist in the project.");
                    return;
                }

                Directory.CreateDirectory(output);
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(output, ExecutableName),
                    target = BuildTarget.StandaloneLinux64,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = Has(args, DevelopmentArg) ? BuildOptions.Development : BuildOptions.None,
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    Fail("the Linux player build " + report.summary.result + " (" + report.summary.totalErrors + " errors).");
                    return;
                }

                Debug.Log("[MindAttic.Multiplayer] Bot/smoke Linux player built (" + scenes[0] + " first): " + output);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Fail(ex.Message);
            }
        }

        /// <summary>The scenes to build: <paramref name="scene"/> first when given, then the enabled Build Settings scenes.</summary>
        public static string[] ScenesFor(string scene, IList<string> enabledScenes)
        {
            if (string.IsNullOrWhiteSpace(scene))
            {
                var all = new List<string>();
                if (enabledScenes != null)
                {
                    all.AddRange(enabledScenes);
                }

                return all.ToArray();
            }

            return ServerPackageBuild.ScenesWithServerFirst(scene.Replace('\\', '/'), enabledScenes);
        }

        private static string[] EnabledBuildScenes()
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

        private static void Fail(string message)
        {
            Debug.LogError("[MindAttic.Multiplayer] Bot/smoke player build failed: " + message);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool Has(string[] args, string name)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
