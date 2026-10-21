using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Unity.Multiplayer.Editor.Tests
{
    /// <summary>A throwaway folder under Assets for tests that need real prefab and list assets.</summary>
    internal static class TempAssets
    {
        public const string Folder = "Assets/MindAtticEditorTestsTemp";

        public static void Create()
        {
            Delete();
            AssetDatabase.CreateFolder("Assets", "MindAtticEditorTestsTemp");
        }

        /// <summary>
        /// Drops the entries NGO added for the folder's prefabs to the project's NetworkPrefabsList assets (NGO puts every
        /// imported NetworkObject prefab in DefaultNetworkPrefabs and never takes it out), then deletes the folder.
        /// </summary>
        public static void Delete()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(NetworkPrefabsList).Name, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
                if (list == null || path.StartsWith(Folder + "/"))
                {
                    continue;
                }

                List<NetworkPrefab> ours = list.PrefabList
                    .Where(entry => entry == null || entry.Prefab == null || AssetDatabase.GetAssetPath(entry.Prefab).StartsWith(Folder + "/"))
                    .ToList();
                foreach (NetworkPrefab entry in ours)
                {
                    list.Remove(entry);
                }

                if (ours.Count > 0)
                {
                    EditorUtility.SetDirty(list);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>Saves <paramref name="go"/> as a prefab in the folder, destroys the scene object and returns the asset.</summary>
        public static GameObject SavePrefab(GameObject go)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/" + go.name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        public static NetPrefabList SaveList(string name, params GameObject[] prefabs)
        {
            var list = ScriptableObject.CreateInstance<NetPrefabList>();
            foreach (GameObject prefab in prefabs)
            {
                list.Add(prefab);
            }

            AssetDatabase.CreateAsset(list, Folder + "/" + name + ".asset");
            AssetDatabase.SaveAssets();
            return list;
        }

        public static string[] Folders
        {
            get { return new[] { Folder }; }
        }
    }
}
