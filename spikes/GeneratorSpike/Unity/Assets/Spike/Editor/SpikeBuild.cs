using System;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MindAttic.Spike.Editor
{
    /// <summary>
    /// Batch-mode entry points used by ../../run-spike.ps1:
    ///   SpikeBuild.Setup             writes the SpikeEntity prefab and the Spike scene
    ///   SpikeBuild.BuildLinuxServer  IL2CPP Linux Dedicated Server  -> Builds/LinuxServer/SpikeServer.x86_64
    ///   SpikeBuild.BuildWindowsPlayer Mono Windows player (host or client) -> Builds/Windows/SpikePlayer.exe
    /// </summary>
    public static class SpikeBuild
    {
        const string PrefabPath = "Assets/Spike/SpikeEntity.prefab";
        const string ScenePath = "Assets/Spike/Spike.unity";

        [MenuItem("MindAttic/Spike/Set Up Scene")]
        public static void Setup()
        {
            GameObject entity = new GameObject("SpikeEntity");
            entity.AddComponent<NetworkObject>();
            entity.AddComponent<SpikeEntity>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(entity, PrefabPath);
            UnityEngine.Object.DestroyImmediate(entity);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject net = new GameObject("NetworkManager");
            NetworkManager nm = net.AddComponent<NetworkManager>();
            UnityTransport utp = net.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = utp, TickRate = 30 };
            SpikeBootstrap boot = net.AddComponent<SpikeBootstrap>();
            boot.entityPrefab = prefab;
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[spike] setup: wrote " + PrefabPath + " and " + ScenePath);
        }

        public static void BuildLinuxServer()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
            Build(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                locationPathName = "Builds/LinuxServer/SpikeServer.x86_64",
                options = BuildOptions.None,
            });
        }

        public static void BuildWindowsPlayer()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Build(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                locationPathName = "Builds/Windows/SpikePlayer.exe",
                options = BuildOptions.None,
            });
        }

        static void Build(BuildPlayerOptions options)
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "MindAtticGeneratorSpike";
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[spike] build " + options.target + " -> " + report.summary.result + " (" + report.summary.totalErrors + " errors, " + report.summary.totalTime + ")");
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
