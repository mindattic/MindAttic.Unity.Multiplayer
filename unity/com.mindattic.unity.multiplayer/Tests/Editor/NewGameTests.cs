using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Unity.Multiplayer.Editor.Tests
{
    /// <summary>
    /// UM-T59: MindAttic ▸ Multiplayer ▸ New Game. The manifest check names exactly the lines a project lacks and adds
    /// them without breaking the JSON; the template copies into a game with its own namespace and fresh, consistent GUIDs.
    /// The whole wizard in a brand-new project (create, the game's own EditMode and PlayMode tests, the smoke) is
    /// tests/run-new-game.ps1.
    /// </summary>
    public class NewGameTests
    {
        private const string Bare = "{\n  \"dependencies\": {\n    \"com.mindattic.unity.multiplayer\": \"file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer\",\n    \"com.unity.modules.physics\": \"1.0.0\"\n  }\n}\n";

        private string _temp;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "um-t59-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temp))
            {
                Directory.Delete(_temp, true);
            }
        }

        // <temp>/repos/{Unity.Multiplayer,MindAttic.Components,Unity.Shared}/unity/<package>, <temp>/repos/Game/Packages.
        private List<ManifestRequirement> SiblingRequirements(bool withShared = true)
        {
            string repos = Path.Combine(_temp, "repos");
            string multiplayer = Directory.CreateDirectory(Path.Combine(repos, "Unity.Multiplayer", "unity", "com.mindattic.unity.multiplayer")).FullName;
            Directory.CreateDirectory(Path.Combine(repos, "MindAttic.Components", "unity", "com.mindattic.components.unity"));
            if (withShared)
            {
                Directory.CreateDirectory(Path.Combine(repos, "Unity.Shared", "unity", "com.mindattic.unity.shared"));
            }

            string packages = Directory.CreateDirectory(Path.Combine(repos, "Game", "Packages")).FullName;
            return NewGameManifest.Requirements(packages, multiplayer);
        }

        [Test]
        public void ABareManifest_IsMissingTheKitsPackages_TheTestFramework_AndNgoAsATestable()
        {
            ManifestCheck check = NewGameManifest.Check(Bare, SiblingRequirements());

            Assert.That(check.IsComplete, Is.False);
            Assert.That(check.Missing.Select(r => r.Package), Is.EquivalentTo(new[]
            {
                "com.mindattic.components.unity", "com.mindattic.unity.shared", "com.unity.test-framework", "com.unity.netcode.gameobjects",
            }));
            Assert.That(check.Missing.Single(r => r.Package == "com.mindattic.unity.shared").Required, Is.False, "Unity.Shared is optional");
            Assert.That(check.Message, Does.Contain("Packages/manifest.json"));
            Assert.That(check.Message, Does.Contain("\"com.mindattic.components.unity\": \"file:../../MindAttic.Components/unity/com.mindattic.components.unity\""),
                "the exact line, with the path from the game's Packages folder to the sibling repo");
            Assert.That(check.Message, Does.Contain("\"com.unity.test-framework\": \"1.6.0\""));
            Assert.That(check.Message, Does.Contain("in \"testables\""));
        }

        [Test]
        public void AddedLines_MakeTheManifestComplete_AndKeepItValidJson()
        {
            List<ManifestRequirement> requirements = SiblingRequirements();
            ManifestCheck check = NewGameManifest.Check(Bare, requirements);

            string added = NewGameManifest.AddLines(Bare, check.Missing);

            Assert.That(NewGameManifest.Check(added, requirements).Missing, Is.Empty, added);
            Assert.That(added, Does.Contain("\"com.unity.modules.physics\": \"1.0.0\""), "existing lines stay");
            Assert.That(UnityEngine.JsonUtility.FromJson<Probe>(added), Is.Not.Null);
            Assert.That(Regex.Matches(added, ",\\s*[}\\]]").Count, Is.Zero, "no trailing commas: " + added);
        }

        [Test]
        public void AnEmptyDependenciesBlock_AndAnExistingTestablesArray_AreFilledIn()
        {
            const string manifest = "{\n  \"dependencies\": {},\n  \"testables\": [\n    \"com.example.other\"\n  ]\n}";
            List<ManifestRequirement> requirements = SiblingRequirements();

            string added = NewGameManifest.AddLines(manifest, NewGameManifest.Check(manifest, requirements).Missing);

            Assert.That(NewGameManifest.Check(added, requirements).Missing, Is.Empty, added);
            Assert.That(added, Does.Contain("\"com.example.other\""));
            Assert.That(Regex.Matches(added, ",\\s*[}\\]]").Count, Is.Zero, "no trailing commas: " + added);
        }

        [Test]
        public void ARepoThatIsNotThere_IsNamedWithTheDocumentedPath_ButNeverWrittenIntoTheManifest()
        {
            List<ManifestRequirement> requirements = SiblingRequirements(withShared: false);
            ManifestRequirement shared = requirements.Single(r => r.Package == "com.mindattic.unity.shared");
            Assert.That(shared.SourceFound, Is.False);
            Assert.That(shared.Line, Is.EqualTo("\"com.mindattic.unity.shared\": \"file:../../Unity.Shared/unity/com.mindattic.unity.shared\""));

            ManifestCheck check = NewGameManifest.Check(Bare, requirements);
            Assert.That(check.Message, Does.Contain("not found at that path"));
            string added = NewGameManifest.AddLines(Bare, check.Missing);
            Assert.That(added, Does.Not.Contain("com.mindattic.unity.shared"));
            Assert.That(added, Does.Contain("com.mindattic.components.unity"));
        }

        [Test]
        public void APackageInstalledAnotherWay_CountsAsPresent()
        {
            ManifestCheck check = NewGameManifest.Check(Bare, SiblingRequirements(), isInstalledElsewhere: p => p == "com.unity.test-framework");
            Assert.That(check.Missing.Select(r => r.Package), Does.Not.Contain("com.unity.test-framework"));
        }

        [Test]
        public void NamesBecomeIdentifiersAndSlugs()
        {
            Assert.That(NewGame.SanitizeName("space race!"), Is.EqualTo("SpaceRace"));
            Assert.That(NewGame.SanitizeName("3 Lives"), Is.EqualTo("Game3Lives"));
            Assert.That(NewGame.SanitizeName("  "), Is.EqualTo(NewGame.TemplateToken));
            Assert.That(NewGame.Slug("SpaceRace"), Is.EqualTo("spacerace"));
            Assert.That(NewGame.Slug("Space Race"), Is.EqualTo("space-race"));
        }

        [Test]
        public void TheTemplateCopiesWithTheGamesNamespace_AndFreshGuidsThatStillPointAtEachOther()
        {
            string template = NewGame.DefaultTemplateFolder();
            Assert.That(File.Exists(Path.Combine(template, NewGame.SceneFile)), "the package ships Samples~/GameTemplate/Game.unity");
            string game = Path.Combine(_temp, "SpaceRace");

            int copied = NewGame.CopyTemplate(template, game, "SpaceRace");

            Assert.That(copied, Is.EqualTo(Directory.GetFiles(template, "*", SearchOption.AllDirectories).Length));
            string player = File.ReadAllText(Path.Combine(game, "Runtime", "Player.cs"));
            Assert.That(player, Does.Contain("namespace SpaceRace"));
            Assert.That(File.ReadAllText(Path.Combine(game, "Tests", "PlayMode", "MyGame.Tests.asmdef")), Does.Contain("\"SpaceRace.Tests\"").And.Contain("\"SpaceRace\","));

            HashSet<string> oldGuids = Guids(template, "*.meta");
            HashSet<string> newGuids = Guids(game, "*.meta");
            Assert.That(newGuids.Count, Is.EqualTo(oldGuids.Count));
            Assert.That(newGuids.Overlaps(oldGuids), Is.False, "every asset has a fresh GUID");

            // Every GUID the scene and prefabs reference is either one of the copy's own assets or a package's (never a template's).
            foreach (string asset in Directory.GetFiles(game, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".unity") || f.EndsWith(".prefab") || f.EndsWith(".asset")))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(asset), "guid: ([0-9a-f]{32})"))
                {
                    Assert.That(oldGuids.Contains(m.Groups[1].Value), Is.False, Path.GetFileName(asset) + " still points at the template's " + m.Groups[1].Value);
                }
            }

            string scene = File.ReadAllText(Path.Combine(game, NewGame.SceneFile));
            string playerGuid = Guids(Path.Combine(game, "Resources", "Game"), "Player.prefab.meta").Single();
            Assert.That(scene, Does.Contain(playerGuid), "the scene's Player Prefab is the copy's own player prefab");
        }

        [Test]
        public void MpHostJsonNamesTheServerSceneAndTheSmokeBots()
        {
            string json = NewGame.MpHostJson("SpaceRace", "Assets/SpaceRace/Game.unity");
            Assert.That(json, Does.Contain("\"name\": \"spacerace\""));
            Assert.That(json, Does.Contain("\"scene\": \"Assets/SpaceRace/Game.unity\""));
            Assert.That(json, Does.Contain("\"bots\": \"A,B\""));
            Assert.That(NewGame.NightlyWorkflow(null), Does.Contain("mindattic/Unity.Multiplayer/.github/workflows/game-smoke.yml@main"));
        }

        private static HashSet<string> Guids(string folder, string pattern)
        {
            var guids = new HashSet<string>();
            foreach (string meta in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
            {
                Match m = Regex.Match(File.ReadAllText(meta), @"^guid:\s*([0-9a-f]{32})", RegexOptions.Multiline);
                if (m.Success)
                {
                    guids.Add(m.Groups[1].Value);
                }
            }

            return guids;
        }

        [Serializable]
        private sealed class Probe
        {
            public string[] testables;
        }
    }
}
