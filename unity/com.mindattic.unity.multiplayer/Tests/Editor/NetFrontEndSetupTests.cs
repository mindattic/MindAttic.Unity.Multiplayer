using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Editor.Tests
{
    /// <summary>
    /// UM-T48: MindAttic ▸ Multiplayer ▸ Add Front End. The test host has MindAttic Components and Unity.Shared installed,
    /// so the real path adds a NetFrontEnd to a networked scene; the missing-package paths are checked with the package
    /// lookup replaced, since a test cannot uninstall a package.
    /// </summary>
    public class NetFrontEndSetupTests
    {
        private Scene _scene;
        private Func<string, bool> _realLookup;
        private Func<Type> _realType;

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
            _realLookup = NetFrontEndSetup.IsPackageInstalled;
            _realType = NetFrontEndSetup.FindFrontEndType;
        }

        [TearDown]
        public void TearDown()
        {
            NetFrontEndSetup.IsPackageInstalled = _realLookup;
            NetFrontEndSetup.FindFrontEndType = _realType;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TempAssets.Delete();
        }

        [Test]
        public void EmptyScene_IsNetworked_AndGetsTheFrontEndBesideTheBootstrap()
        {
            Assert.That(NetFrontEndSetup.FindFrontEndType(), Is.Not.Null, "Unity.Multiplayer.UI compiles in the test host (Components installed)");

            NetFrontEndSetupResult result = NetFrontEndSetup.Run(_scene, Options());

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.MissingPackages, Is.Empty);
            Assert.That(result.MissingOptionalPackages, Is.Empty, "Unity.Shared is installed too");
            Assert.That(result.Scene.Bootstrap, Is.Not.Null, "the scene was networked first");
            Assert.That(result.FrontEnd, Is.Not.Null);
            Assert.That(result.FrontEnd.GetType().FullName, Is.EqualTo("Unity.Multiplayer.UI.NetFrontEnd"));
            Assert.That(result.FrontEnd.gameObject, Is.SameAs(result.Scene.Root), "on the NetworkManager, so it lives as long as the session");
            Assert.That(new SerializedObject(result.FrontEnd).FindProperty("bootstrap").objectReferenceValue, Is.SameAs(result.Scene.Bootstrap));
            Assert.That(result.Changes, Has.Some.Contains("Added NetFrontEnd").And.Some.Contains("Added NetBootstrap"));
            Assert.That(result.Message, Does.StartWith("Front end added."));
            Assert.That(_scene.isDirty, Is.True);
        }

        [Test]
        public void SecondRun_ChangesNothing()
        {
            NetFrontEndSetupResult first = NetFrontEndSetup.Run(_scene, Options());
            NetFrontEndSetupResult second = NetFrontEndSetup.Run(_scene, Options());

            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.Changes, Is.Empty, string.Join("\n", second.Changes));
            Assert.That(second.FrontEnd, Is.SameAs(first.FrontEnd));
            Assert.That(second.Message, Is.EqualTo("The scene already has the multiplayer front end."));
            Assert.That(first.FrontEnd.gameObject.GetComponents(first.FrontEnd.GetType()), Has.Length.EqualTo(1));
        }

        [Test]
        public void ComponentsMissing_ChangesNothing_AndSaysExactlyWhatToAdd()
        {
            NetFrontEndSetup.IsPackageInstalled = name => false;

            NetFrontEndSetupResult result = NetFrontEndSetup.Run(_scene, Options());

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.MissingPackages, Is.EqualTo(new[] { NetFrontEndSetup.ComponentsPackage }));
            Assert.That(result.MissingOptionalPackages, Is.EqualTo(new[] { NetFrontEndSetup.SharedPackage }));
            Assert.That(result.Message, Does.Contain(NetFrontEndSetup.ComponentsManifestLine));
            Assert.That(result.Message, Does.Contain(NetFrontEndSetup.SharedManifestLine).And.Contain("optional"));
            Assert.That(result.Message, Does.Contain("Packages/manifest.json"));
            Assert.That(_scene.GetRootGameObjects(), Is.Empty, "the scene is untouched");
        }

        [Test]
        public void SharedMissing_StillAddsTheFrontEnd_AndMentionsTheProfileOption()
        {
            NetFrontEndSetup.IsPackageInstalled = name => name != NetFrontEndSetup.SharedPackage;

            NetFrontEndSetupResult result = NetFrontEndSetup.Run(_scene, Options());

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.MissingOptionalPackages, Is.EqualTo(new[] { NetFrontEndSetup.SharedPackage }));
            Assert.That(result.Message, Does.Contain(NetFrontEndSetup.SharedManifestLine).And.Contain("PlayerPrefs"));
        }

        [Test]
        public void InstalledButNotCompiled_SaysToWaitForTheCompile()
        {
            NetFrontEndSetup.FindFrontEndType = () => null;

            NetFrontEndSetupResult result = NetFrontEndSetup.Run(_scene, Options());

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.MissingPackages, Is.Empty);
            Assert.That(result.Message, Does.Contain("has not compiled yet"));
            Assert.That(_scene.GetRootGameObjects(), Is.Empty);
        }

        [Test]
        public void Undo_RemovesTheFrontEndAndTheNetworking()
        {
            Undo.IncrementCurrentGroup();
            NetFrontEndSetup.Run(_scene, Options());
            Assert.That(_scene.GetRootGameObjects(), Has.Length.EqualTo(1));

            Undo.PerformUndo();

            Assert.That(_scene.GetRootGameObjects().SelectMany(go => go.GetComponents<Component>()).Where(c => c != null && c.GetType().Name == "NetFrontEnd"), Is.Empty);
        }

        [Test]
        public void UnloadedScene_Throws()
        {
            Assert.Throws<ArgumentException>(() => NetFrontEndSetup.Run(default(Scene)));
        }
    }
}
