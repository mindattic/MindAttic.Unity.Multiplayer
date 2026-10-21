using System;
using System.IO;
using System.Linq;
using Unity.Multiplayer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-mode entry point tests/run-smoke.ps1 (and .github/workflows/nightly-smoke.yml) call, in the same throwaway
/// host project MinimalSampleHost.Import already populated, to build the Linux Player every role in the nightly
/// Docker Compose smoke test runs (RFC 0001, UM-T17): the dedicated server and both bot clients are the same build,
/// told apart at run time by SmokeDriver.cs (copied into Assets alongside this file) reading MINDATTIC_ROLE.
/// </summary>
public static class SmokeHostBuild
{
    private const string PackageName = "com.mindattic.unity.multiplayer";
    private const string SampleName = "Minimal";

    /// <summary>
    /// Opens the imported Minimal sample's scene, turns off its NetBootstrap's "serve when headless" default (the
    /// smoke test picks a role itself through SmokeDriver instead of NetLaunch's own default-to-server rule), and
    /// builds a StandaloneLinux64 Player into <c>-smokeOutput</c> (default <c>smoke/build</c> beside the repository
    /// root, where smoke/Bot.Dockerfile expects it).
    /// </summary>
    public static void BuildFromCommandLine()
    {
        try
        {
            string folder = ToAssetsPath(Find().importPath);
            string scenePath = folder + "/Minimal.unity";

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            NetBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<NetBootstrap>();
            if (bootstrap == null)
            {
                throw new InvalidOperationException(scenePath + " has no NetBootstrap; the committed sample should already have one from MinimalSampleHost.Author.");
            }

            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("serveWhenHeadless").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

            string output = Arg("-smokeOutput");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../smoke/build"));
            }

            Directory.CreateDirectory(output);
            string exe = Path.Combine(output, "Game.x86_64");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            bool ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log("[SmokeHostBuild] Build " + (ok ? "succeeded" : "FAILED") + ": " + output);
            EditorApplication.Exit(ok ? 0 : 1);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static Sample Find()
    {
        var samples = Sample.FindByPackage(PackageName, null).Where(s => s.displayName == SampleName).ToList();
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("Package Manager lists no \"" + SampleName + "\" sample for " + PackageName + "; import it first (MinimalSampleHost.Import).");
        }

        return samples[0];
    }

    private static string ToAssetsPath(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        string root = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? "Assets" + full.Substring(root.Length) : full;
    }

    private static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
