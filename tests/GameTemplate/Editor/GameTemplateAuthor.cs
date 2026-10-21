using System;
using System.IO;
using System.Linq;
using Unity.Multiplayer;
using Unity.Multiplayer.Editor;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The batch-mode steps run-new-game.ps1 -Author runs in its throwaway project to (re)build the game template's scene,
/// prefabs and prefab list, which can only be authored once the template's scripts have compiled:
/// <see cref="Prepare"/> copies the template's scripts into Assets/MyGame keeping their GUIDs, and, in the next Unity
/// run, <see cref="Author"/> builds Game.unity (Add Front End + Net Votes + the Game component), Resources/Game/Player.prefab,
/// Resources/Game/Crate.prefab and GamePrefabs.asset there, for the script to copy back into Samples~/GameTemplate.
/// Template types are looked up by name: this file compiles before the template does.
/// </summary>
public static class GameTemplateAuthor
{
    private const string Folder = "Assets/" + NewGame.TemplateToken;

    public static void Prepare()
    {
        try
        {
            string destination = Path.Combine(NewGame.ProjectFolder(), "Assets", NewGame.TemplateToken);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            int files = NewGame.CopyTemplate(NewGame.DefaultTemplateFolder(), destination, NewGame.TemplateToken, freshGuids: false);
            Debug.Log("[GameTemplateAuthor] Copied " + files + " template files into " + Folder);
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    public static void Author()
    {
        try
        {
            Type playerType = TemplateType("Player");
            Type crateType = TemplateType("Crate");
            Type gameType = TemplateType("Game");
            EnsureFolder(Folder + "/Resources/Game");

            var player = new GameObject("Player");
            player.AddComponent<NetworkObject>();
            player.AddComponent(playerType);
            GameObject playerPrefab = PrefabUtility.SaveAsPrefabAsset(player, Folder + "/Resources/Game/Player.prefab");
            UnityEngine.Object.DestroyImmediate(player);

            var crate = new GameObject("Crate");
            crate.AddComponent<NetworkObject>();
            crate.AddComponent<NetKey>();
            crate.AddComponent(crateType);
            GameObject cratePrefab = PrefabUtility.SaveAsPrefabAsset(crate, Folder + "/Resources/Game/Crate.prefab");
            UnityEngine.Object.DestroyImmediate(crate);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            // What a game does by hand: MindAttic > Multiplayer > Add Front End (it networks the scene first) ...
            NetFrontEndSetupResult setup = NetFrontEndSetup.Run(scene, new NetSceneSetupOptions
            {
                PrefabListPath = Folder + "/GamePrefabs.asset",
                SearchFolders = new[] { Folder },
            });
            if (!setup.Succeeded)
            {
                throw new InvalidOperationException(setup.Message);
            }

            setup.Scene.Manager.NetworkConfig.PlayerPrefab = playerPrefab;
            setup.Scene.Manager.NetworkConfig.EnableSceneManagement = false;
            EditorUtility.SetDirty(setup.Scene.Manager);

            // ... and Add Component > MindAttic > Multiplayer > Net Votes beside it.
            setup.Scene.Root.AddComponent<NetVotes>();

            var gameObject = new GameObject("Game");
            Component game = gameObject.AddComponent(gameType);
            var serialized = new SerializedObject(game);
            serialized.FindProperty("cratePrefab").objectReferenceValue = cratePrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, Folder + "/" + NewGame.SceneFile);
            AssetDatabase.SaveAssets();

            NetPrefabValidationReport report = NetPrefabValidator.ValidateProject(new[] { Folder });
            report.Log();
            Debug.Log("[GameTemplateAuthor] Authored " + Folder + ": " + string.Join(" ", setup.Changes));
            EditorApplication.Exit(report.HasErrors ? 1 : 0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static Type TemplateType(string name)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(NewGame.TemplateToken + "." + name))
            .FirstOrDefault(t => t != null);
        if (type == null)
        {
            throw new InvalidOperationException(NewGame.TemplateToken + "." + name + " is not compiled; run Prepare first (and check the Console).");
        }

        return type;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
