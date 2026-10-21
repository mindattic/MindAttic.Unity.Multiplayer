using System;
using System.Linq;
using Unity.Multiplayer.Editor;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The batch-mode entry point run-sample.ps1 -Sample FrontEnd -Author calls in tests/SampleHost (copied to its
/// Assets/Editor beside MinimalSampleHost, which imports the sample). <see cref="Author"/> (re)builds the Front End
/// sample's scene, player prefab and prefab list inside the imported copy, with the Add Front End wizard step, for
/// run-sample.ps1 to copy back into Samples~/FrontEnd. Sample types are looked up by name: this file compiles before the
/// sample is imported.
/// </summary>
public static class FrontEndSampleHost
{
    public const string SampleName = "Front End";
    private const string SampleNamespace = "Unity.Multiplayer.Samples.FrontEnd.";

    /// <summary>Builds FrontEnd.unity, FrontEndPlayer.prefab and FrontEndPrefabs.asset in the imported sample folder.</summary>
    public static void Author()
    {
        try
        {
            string folder = MinimalSampleHost.ToAssetsPath(MinimalSampleHost.Find(SampleName).importPath);
            Type playerType = SampleType("FrontEndPlayer");
            Type gameType = SampleType("FrontEndGame");

            var player = new GameObject("FrontEndPlayer");
            player.AddComponent<NetworkObject>();
            player.AddComponent(playerType);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, folder + "/FrontEndPlayer.prefab");
            UnityEngine.Object.DestroyImmediate(player);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            // The one step a game takes: MindAttic > Multiplayer > Add Front End (it networks the scene first).
            NetFrontEndSetupResult setup = NetFrontEndSetup.Run(scene, new NetSceneSetupOptions
            {
                PrefabListPath = folder + "/FrontEndPrefabs.asset",
                SearchFolders = new[] { folder },
            });
            if (!setup.Succeeded)
            {
                throw new InvalidOperationException(setup.Message);
            }

            setup.Scene.Manager.NetworkConfig.PlayerPrefab = prefab;
            setup.Scene.Manager.NetworkConfig.EnableSceneManagement = false;
            EditorUtility.SetDirty(setup.Scene.Manager);

            var gameObject = new GameObject("Game");
            Component game = gameObject.AddComponent(gameType);
            var serialized = new SerializedObject(game);
            serialized.FindProperty("frontEnd").objectReferenceValue = setup.FrontEnd;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, folder + "/FrontEnd.unity");
            AssetDatabase.SaveAssets();

            NetPrefabValidationReport report = NetPrefabValidator.ValidateProject(new[] { folder });
            report.Log();
            Debug.Log("[FrontEndSampleHost] Authored " + folder + ": " + string.Join(" ", setup.Changes));
            EditorApplication.Exit(report.HasErrors ? 1 : 0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static Type SampleType(string name)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(SampleNamespace + name))
            .FirstOrDefault(t => t != null);
        if (type == null)
        {
            throw new InvalidOperationException(SampleNamespace + name + " is not compiled; import the sample (and its packages) first.");
        }

        return type;
    }
}
