using System;
using System.IO;
using System.Linq;
using Unity.Multiplayer;
using Unity.Multiplayer.Editor;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-mode entry points run-sample.ps1 calls in the throwaway tests/SampleHost project (copied to its Assets/Editor).
/// <see cref="Import"/> imports the package's Minimal sample the way Package Manager's Import button does;
/// <see cref="Author"/> (re)builds the sample's scene, player prefab and prefab list inside the imported copy, for
/// run-sample.ps1 -Author to copy back into Samples~/Minimal. Sample types are looked up by name: this file compiles
/// before the sample is imported.
/// </summary>
public static class MinimalSampleHost
{
    public const string PackageName = "com.mindattic.unity.multiplayer";
    public const string SampleName = "Minimal";
    private const string SampleNamespace = "Unity.Multiplayer.Samples.Minimal.";

    /// <summary>Imports the Minimal sample through Package Manager's sample API and writes its folder to logs/sample-path.txt.</summary>
    public static void Import()
    {
        try
        {
            Sample sample = Find();
            bool ok = sample.Import(Sample.ImportOptions.OverridePreviousImports | Sample.ImportOptions.HideImportWindow);
            string assetsPath = ToAssetsPath(sample.importPath);
            Debug.Log("[MinimalSampleHost] Import " + (ok ? "succeeded" : "FAILED") + " into " + assetsPath);
            if (!ok || !Directory.Exists(sample.importPath))
            {
                EditorApplication.Exit(1);
                return;
            }

            File.WriteAllText(Path.Combine("logs", "sample-path.txt"), assetsPath);
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Builds Minimal.unity, MinimalPlayer.prefab and MinimalPrefabs.asset in the imported sample folder: a camera, the
    /// NetworkManager networked by the Set Up Scene wizard (the player prefab as its player prefab, scene management
    /// off because the sample has one scene) and the menu.
    /// </summary>
    public static void Author()
    {
        try
        {
            string folder = ToAssetsPath(Find().importPath);
            Type playerType = SampleType("MinimalPlayer");
            Type menuType = SampleType("MinimalMenu");

            // The player prefab: a NetworkObject and the MinimalPlayer entity.
            var player = new GameObject("MinimalPlayer");
            player.AddComponent<NetworkObject>();
            player.AddComponent(playerType);
            string prefabPath = folder + "/MinimalPlayer.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, prefabPath);
            UnityEngine.Object.DestroyImmediate(player);

            // The scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            NetSceneSetupResult setup = NetSceneSetup.Run(scene, new NetSceneSetupOptions
            {
                PrefabListPath = folder + "/MinimalPrefabs.asset",
                SearchFolders = new[] { folder },
            });
            setup.Manager.NetworkConfig.PlayerPrefab = prefab;
            setup.Manager.NetworkConfig.EnableSceneManagement = false;
            EditorUtility.SetDirty(setup.Manager);

            var menuObject = new GameObject("Menu");
            Component menu = menuObject.AddComponent(menuType);
            var serialized = new SerializedObject(menu);
            serialized.FindProperty("bootstrap").objectReferenceValue = setup.Bootstrap;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, folder + "/Minimal.unity");
            AssetDatabase.SaveAssets();

            NetPrefabValidationReport report = NetPrefabValidator.ValidateProject(new[] { folder });
            report.Log();
            Debug.Log("[MinimalSampleHost] Authored " + folder + ": " + string.Join(" ", setup.Changes));
            EditorApplication.Exit(report.HasErrors ? 1 : 0);
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
            throw new InvalidOperationException("Package Manager lists no \"" + SampleName + "\" sample for " + PackageName + ".");
        }

        return samples[0];
    }

    private static Type SampleType(string name)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(SampleNamespace + name))
            .FirstOrDefault(t => t != null);
        if (type == null)
        {
            throw new InvalidOperationException(SampleNamespace + name + " is not compiled; import the sample first.");
        }

        return type;
    }

    private static string ToAssetsPath(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        string root = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? "Assets" + full.Substring(root.Length) : full;
    }
}
