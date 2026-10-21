// EditMode checks that the game's scene and prefabs are wired the way the kit needs them, so a broken reference or an
// unregistered network prefab fails here instead of in a build. Run from Test Runner > EditMode, or:
//   Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames MyGame.EditorTests
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Multiplayer;
using Unity.Multiplayer.Editor;
using Unity.Multiplayer.UI;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyGame.EditorTests
{
    public class GameSetupTests
    {
        // The game's folder: where Game.cs lives, one level up from Runtime/.
        private static string GameFolder()
        {
            string script = AssetDatabase.FindAssets("t:MonoScript Game")
                .Select(AssetDatabase.GUIDToAssetPath)
                .First(p => AssetDatabase.LoadAssetAtPath<MonoScript>(p).GetClass() == typeof(Game));
            return Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\', '/');
        }

        [Test]
        public void TheSceneHasTheKitsComponents_AndThePlayerPrefab()
        {
            Scene scene = EditorSceneManager.OpenScene(GameFolder() + "/Game.unity", OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            NetworkManager manager = roots.Select(r => r.GetComponentInChildren<NetworkManager>()).First(m => m != null);

            Assert.That(manager.GetComponent<NetBootstrap>(), Is.Not.Null, "NetBootstrap");
            Assert.That(manager.GetComponent<NetFrontEnd>(), Is.Not.Null, "NetFrontEnd (the menus)");
            Assert.That(manager.GetComponent<NetVotes>(), Is.Not.Null, "NetVotes");
            Assert.That(manager.NetworkConfig.PlayerPrefab, Is.Not.Null, "the NetworkManager's Player Prefab");
            Assert.That(manager.NetworkConfig.PlayerPrefab.GetComponent<Player>(), Is.Not.Null, "the player prefab has a Player");

            Game game = roots.Select(r => r.GetComponentInChildren<Game>()).First(g => g != null);
            var crate = new SerializedObject(game).FindProperty("cratePrefab").objectReferenceValue as GameObject;
            Assert.That(crate, Is.Not.Null, "Game's crate prefab");
            Assert.That(crate.GetComponent<NetKey>(), Is.Not.Null, "the crate prefab has a NetKey");
        }

        [Test]
        public void EveryNetworkPrefabPassesTheValidator()
        {
            NetPrefabValidationReport report = NetPrefabValidator.ValidateProject(new[] { GameFolder() });
            report.Log();
            Assert.That(report.HasErrors, Is.False, "see the validator's messages in the log");
        }
    }
}
