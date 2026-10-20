using System.Linq;
using MindAttic.Unity.Multiplayer.Tests;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MindAttic.Unity.Multiplayer.Editor.Tests
{
    public class NetSceneSetupTests
    {
        private Scene _scene;

        private static NetSceneSetupOptions Options()
        {
            return new NetSceneSetupOptions
            {
                SearchFolders = TempAssets.Folders,
                PrefabListPath = TempAssets.Folder + "/NetPrefabs.asset",
            };
        }

        [SetUp]
        public void SetUp()
        {
            TempAssets.Create();
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TempAssets.Delete();
        }

        private NetworkManager[] Managers()
        {
            return _scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<NetworkManager>(true)).ToArray();
        }

        private static NetPrefabList[] AssignedLists(NetBootstrap bootstrap)
        {
            SerializedProperty lists = new SerializedObject(bootstrap).FindProperty("prefabLists");
            return Enumerable.Range(0, lists.arraySize)
                .Select(i => lists.GetArrayElementAtIndex(i).objectReferenceValue as NetPrefabList)
                .ToArray();
        }

        [Test]
        public void EmptyScene_GetsNetworkManagerTransportBootstrapAndPrefabListInOneRun()
        {
            NetSceneSetupResult result = NetSceneSetup.Run(_scene, Options());

            NetworkManager manager = Managers().Single();
            Assert.That(result.Root, Is.SameAs(manager.gameObject));
            Assert.That(result.Root.name, Is.EqualTo(NetSceneSetup.RootName));
            Assert.That(result.Root.scene, Is.EqualTo(_scene));
            Assert.That(result.Transport, Is.SameAs(result.Root.GetComponent<UnityTransport>()).And.Not.Null);
            Assert.That(result.Bootstrap, Is.SameAs(result.Root.GetComponent<NetBootstrap>()).And.Not.Null);
            Assert.That(manager.NetworkConfig.NetworkTransport, Is.SameAs(result.Transport));

            Assert.That(result.CreatedPrefabList, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(result.PrefabList), Is.EqualTo(TempAssets.Folder + "/NetPrefabs.asset"));
            Assert.That(AssignedLists(result.Bootstrap), Is.EqualTo(new[] { result.PrefabList }));
            Assert.That(result.Changes, Has.Some.Contains("GameObject").And.Some.Contains("Created the prefab list"), string.Join("\n", result.Changes));
            Assert.That(_scene.isDirty, Is.True);
        }

        [Test]
        public void NewPrefabList_HoldsTheProjectsNetworkPrefabsNotYetRegistered()
        {
            var networked = new GameObject("Networked");
            networked.AddComponent<NetworkObject>();
            networked.AddComponent<PlainProbeEntity>();
            GameObject networkedPrefab = TempAssets.SavePrefab(networked);
            GameObject plainPrefab = TempAssets.SavePrefab(new GameObject("Plain"));

            NetSceneSetupResult result = NetSceneSetup.Run(_scene, Options());

            Assert.That(result.PrefabList.Prefabs, Is.EquivalentTo(new[] { networkedPrefab }));
            Assert.That(result.PrefabList.Prefabs, Has.No.Member(plainPrefab));
            Assert.That(NetPrefabValidator.ValidateProject(TempAssets.Folders).Issues, Is.Empty);
        }

        [Test]
        public void RegisterProjectPrefabsOff_CreatesAnEmptyList()
        {
            var networked = new GameObject("Networked");
            networked.AddComponent<NetworkObject>();
            TempAssets.SavePrefab(networked);
            NetSceneSetupOptions options = Options();
            options.RegisterProjectPrefabs = false;

            NetSceneSetupResult result = NetSceneSetup.Run(_scene, options);

            Assert.That(result.PrefabList.Prefabs, Is.Empty);
        }

        [Test]
        public void SecondRun_ChangesNothing()
        {
            NetSceneSetupResult first = NetSceneSetup.Run(_scene, Options());
            NetSceneSetupResult second = NetSceneSetup.Run(_scene, Options());

            Assert.That(second.Changes, Is.Empty, string.Join("\n", second.Changes));
            Assert.That(Managers(), Has.Length.EqualTo(1));
            Assert.That(second.Root, Is.SameAs(first.Root));
            Assert.That(second.PrefabList, Is.SameAs(first.PrefabList));
            Assert.That(second.CreatedPrefabList, Is.False);
            Assert.That(AssignedLists(second.Bootstrap), Has.Length.EqualTo(1));
            Assert.That(second.Root.GetComponents<UnityTransport>(), Has.Length.EqualTo(1));
        }

        [Test]
        public void ExistingNetworkManager_IsReusedAndCompleted()
        {
            var existing = new GameObject("MyNet");
            existing.AddComponent<NetworkManager>();

            NetSceneSetupResult result = NetSceneSetup.Run(_scene, Options());

            Assert.That(result.Root, Is.SameAs(existing));
            Assert.That(_scene.GetRootGameObjects(), Has.Length.EqualTo(1));
            Assert.That(existing.GetComponent<UnityTransport>(), Is.Not.Null);
            Assert.That(existing.GetComponent<NetBootstrap>(), Is.Not.Null);
            Assert.That(result.Manager.NetworkConfig.NetworkTransport, Is.SameAs(existing.GetComponent<UnityTransport>()));
            Assert.That(result.Changes, Has.None.Contains("GameObject"));
        }

        [Test]
        public void ExistingProjectPrefabList_IsAssignedInsteadOfANewOne()
        {
            NetPrefabList mine = TempAssets.SaveList("MyPrefabs");

            NetSceneSetupResult result = NetSceneSetup.Run(_scene, Options());

            Assert.That(result.PrefabList, Is.SameAs(mine));
            Assert.That(result.CreatedPrefabList, Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<NetPrefabList>(TempAssets.Folder + "/NetPrefabs.asset"), Is.Null);
            Assert.That(AssignedLists(result.Bootstrap), Is.EqualTo(new[] { mine }));
        }

        [Test]
        public void ExplicitPrefabList_IsAddedBesideTheAssignedOne()
        {
            NetSceneSetupResult first = NetSceneSetup.Run(_scene, Options());
            NetPrefabList extra = TempAssets.SaveList("Extra");
            NetSceneSetupOptions options = Options();
            options.PrefabList = extra;

            NetSceneSetupResult second = NetSceneSetup.Run(_scene, options);

            Assert.That(second.PrefabList, Is.SameAs(extra));
            Assert.That(AssignedLists(second.Bootstrap), Is.EqualTo(new[] { first.PrefabList, extra }));
        }

        [Test]
        public void Undo_RemovesWhatTheSetupAdded()
        {
            Undo.IncrementCurrentGroup();
            NetSceneSetup.Run(_scene, Options());
            Assert.That(Managers(), Has.Length.EqualTo(1));

            Undo.PerformUndo();

            Assert.That(Managers(), Is.Empty);
        }

        [Test]
        public void UnloadedScene_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => NetSceneSetup.Run(default(Scene)));
        }
    }
}
