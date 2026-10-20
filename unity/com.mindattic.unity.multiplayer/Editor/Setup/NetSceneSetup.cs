using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MindAttic.Unity.Multiplayer.Editor
{
    /// <summary>What <see cref="NetSceneSetup.Run"/> uses besides the scene.</summary>
    public sealed class NetSceneSetupOptions
    {
        /// <summary>The prefab list to assign. Null: keep the one already assigned, else the project's first, else create one.</summary>
        public NetPrefabList PrefabList { get; set; }

        /// <summary>Where a new prefab list is created when the project has none.</summary>
        public string PrefabListPath { get; set; } = NetSceneSetup.DefaultPrefabListPath;

        /// <summary>
        /// When a new prefab list is created, fill it with the project's prefabs that have a NetworkObject on their root
        /// and are not registered in another list yet.
        /// </summary>
        public bool RegisterProjectPrefabs { get; set; } = true;

        /// <summary>Folders searched for prefab lists and prefabs. Default: Assets only, never packages.</summary>
        public string[] SearchFolders { get; set; } = { "Assets" };
    }

    /// <summary>What <see cref="NetSceneSetup.Run"/> found, made and changed.</summary>
    public sealed class NetSceneSetupResult
    {
        internal NetSceneSetupResult()
        {
        }

        /// <summary>The GameObject holding the NetworkManager, its transport and the NetBootstrap.</summary>
        public GameObject Root { get; internal set; }

        /// <summary>The scene's NetworkManager.</summary>
        public NetworkManager Manager { get; internal set; }

        /// <summary>The NetworkManager's Unity Transport.</summary>
        public UnityTransport Transport { get; internal set; }

        /// <summary>The scene's NetBootstrap.</summary>
        public NetBootstrap Bootstrap { get; internal set; }

        /// <summary>The prefab list assigned to the NetBootstrap.</summary>
        public NetPrefabList PrefabList { get; internal set; }

        /// <summary>True when the prefab list asset was created by this run.</summary>
        public bool CreatedPrefabList { get; internal set; }

        /// <summary>One line per change made; empty when the scene was already set up.</summary>
        public List<string> Changes { get; } = new List<string>();
    }

    /// <summary>
    /// MindAttic ▸ Multiplayer ▸ Set Up Scene: networks the open scene in one click. Finds or creates the NetworkManager
    /// GameObject, gives it Unity Transport (wired as the NetworkManager's transport) and a <see cref="NetBootstrap"/>,
    /// and assigns a <see cref="NetPrefabList"/> (the project's, or a new one holding the project's network prefabs).
    /// Running it again changes only what is missing, and every change can be undone.
    /// </summary>
    public static class NetSceneSetup
    {
        /// <summary>The menu item.</summary>
        public const string MenuPath = "MindAttic/Multiplayer/Set Up Scene";

        /// <summary>Where a new prefab list goes by default.</summary>
        public const string DefaultPrefabListPath = "Assets/NetPrefabs.asset";

        /// <summary>The name of a GameObject the setup creates.</summary>
        public const string RootName = "NetworkManager";

        [MenuItem(MenuPath, false, 0)]
        private static void SetUpActiveScene()
        {
            NetSceneSetupResult result = Run(SceneManager.GetActiveScene(), new NetSceneSetupOptions());
            Selection.activeGameObject = result.Root;
            EditorGUIUtility.PingObject(result.Root);
            Debug.Log(result.Changes.Count == 0
                ? "[MindAttic.Multiplayer] The scene was already set up."
                : "[MindAttic.Multiplayer] Scene set up:\n  " + string.Join("\n  ", result.Changes), result.Root);
        }

        /// <summary>Sets <paramref name="scene"/> up for multiplayer (see the class summary).</summary>
        /// <exception cref="System.ArgumentException"><paramref name="scene"/> is not loaded.</exception>
        public static NetSceneSetupResult Run(Scene scene, NetSceneSetupOptions options = null)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new System.ArgumentException("The scene must be loaded.", nameof(scene));
            }

            options = options ?? new NetSceneSetupOptions();
            var result = new NetSceneSetupResult();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Up Multiplayer Scene");

            NetworkManager manager = FindManager(scene, result);
            GameObject root;
            if (manager != null)
            {
                root = manager.gameObject;
            }
            else
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Create NetworkManager");
                manager = Undo.AddComponent<NetworkManager>(root);
                result.Changes.Add("Created the " + RootName + " GameObject with a NetworkManager.");
            }

            UnityTransport transport = root.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = Undo.AddComponent<UnityTransport>(root);
                result.Changes.Add("Added Unity Transport.");
            }

            if (manager.NetworkConfig == null || manager.NetworkConfig.NetworkTransport != transport)
            {
                Undo.RecordObject(manager, "Wire Unity Transport");
                if (manager.NetworkConfig == null)
                {
                    manager.NetworkConfig = new NetworkConfig();
                }

                manager.NetworkConfig.NetworkTransport = transport;
                EditorUtility.SetDirty(manager);
                result.Changes.Add("Set Unity Transport as the NetworkManager's transport.");
            }

            NetBootstrap bootstrap = root.GetComponent<NetBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = Undo.AddComponent<NetBootstrap>(root);
                result.Changes.Add("Added NetBootstrap.");
            }

            AssignPrefabList(bootstrap, options, result);

            result.Root = root;
            result.Manager = manager;
            result.Transport = transport;
            result.Bootstrap = bootstrap;
            Undo.CollapseUndoOperations(undoGroup);
            if (result.Changes.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return result;
        }

        private static NetworkManager FindManager(Scene scene, NetSceneSetupResult result)
        {
            var found = new List<NetworkManager>();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                found.AddRange(go.GetComponentsInChildren<NetworkManager>(true));
            }

            if (found.Count > 1)
            {
                Debug.LogWarning("[MindAttic.Multiplayer] The scene has " + found.Count + " NetworkManagers; setting up '" + found[0].name + "'. Keep one.", found[0]);
            }

            return found.Count > 0 ? found[0] : null;
        }

        private static void AssignPrefabList(NetBootstrap bootstrap, NetSceneSetupOptions options, NetSceneSetupResult result)
        {
            var serialized = new SerializedObject(bootstrap);
            SerializedProperty lists = serialized.FindProperty("prefabLists");
            var assigned = new List<NetPrefabList>();
            for (int i = 0; i < lists.arraySize; i++)
            {
                var list = lists.GetArrayElementAtIndex(i).objectReferenceValue as NetPrefabList;
                if (list != null)
                {
                    assigned.Add(list);
                }
            }

            NetPrefabList wanted = options.PrefabList;
            if (wanted == null && assigned.Count > 0)
            {
                result.PrefabList = assigned[0];
                return;
            }

            if (wanted == null)
            {
                wanted = FindProjectList(options.SearchFolders);
            }

            if (wanted == null)
            {
                wanted = CreateList(options, result);
            }

            result.PrefabList = wanted;
            if (assigned.Contains(wanted))
            {
                return;
            }

            // Fill an empty slot left in the Inspector before growing the array.
            int slot = -1;
            for (int i = 0; i < lists.arraySize; i++)
            {
                if (lists.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                slot = lists.arraySize;
                lists.arraySize++;
            }

            lists.GetArrayElementAtIndex(slot).objectReferenceValue = wanted;
            serialized.ApplyModifiedProperties();
            result.Changes.Add("Assigned the prefab list '" + AssetDatabase.GetAssetPath(wanted) + "' to NetBootstrap.");
        }

        private static NetPrefabList FindProjectList(string[] folders)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(NetPrefabList).Name, ValidFolders(folders)))
            {
                var list = AssetDatabase.LoadAssetAtPath<NetPrefabList>(AssetDatabase.GUIDToAssetPath(guid));
                if (list != null)
                {
                    return list;
                }
            }

            return null;
        }

        private static NetPrefabList CreateList(NetSceneSetupOptions options, NetSceneSetupResult result)
        {
            string path = string.IsNullOrWhiteSpace(options.PrefabListPath) ? DefaultPrefabListPath : options.PrefabListPath;
            var existing = AssetDatabase.LoadAssetAtPath<NetPrefabList>(path);
            if (existing != null)
            {
                return existing;
            }

            string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(folder);
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var list = ScriptableObject.CreateInstance<NetPrefabList>();
            if (options.RegisterProjectPrefabs)
            {
                foreach (GameObject prefab in NetPrefabValidator.FindUnregisteredPrefabs(options.SearchFolders))
                {
                    list.Add(prefab);
                }
            }

            AssetDatabase.CreateAsset(list, path);
            Undo.RegisterCreatedObjectUndo(list, "Create Net Prefab List");
            AssetDatabase.SaveAssets();
            result.CreatedPrefabList = true;
            result.Changes.Add("Created the prefab list '" + path + "' with " + list.Prefabs.Count + " prefab(s).");
            return list;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }

        internal static string[] ValidFolders(string[] folders)
        {
            var valid = new List<string>();
            foreach (string folder in folders ?? new[] { "Assets" })
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    valid.Add(folder);
                }
            }

            return valid.Count > 0 ? valid.ToArray() : new[] { "Assets" };
        }
    }
}
