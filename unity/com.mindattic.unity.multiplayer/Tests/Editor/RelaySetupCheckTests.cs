using System;
using System.IO;
using NUnit.Framework;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer.Editor.Tests
{
    /// <summary>UM-T71: the New Game wizard's Unity Relay step: the package line, the Cloud project link, and what to do.</summary>
    public class RelaySetupCheckTests
    {
        private string _temp;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "um-t71-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_temp, "Packages"));
            File.WriteAllText(Path.Combine(_temp, "Packages", "manifest.json"),
                "{\n  \"dependencies\": {\n    \"com.unity.netcode.gameobjects\": \"2.11.0\"\n  }\n}\n");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temp))
            {
                Directory.Delete(_temp, true);
            }
        }

        [Test]
        public void AddPackage_AddsTheVerifiedVersion_Once()
        {
            Assert.That(RelaySetupCheck.Check(_temp).PackageInManifest, Is.False);

            string first = RelaySetupCheck.AddPackage(_temp);
            string manifest = File.ReadAllText(Path.Combine(_temp, "Packages", "manifest.json"));
            Assert.That(first, Does.StartWith("Added to"));
            Assert.That(manifest, Does.Contain("\"" + RelaySetup.PackageName + "\": \"" + RelaySetup.PackageVersion + "\""));
            Assert.That(manifest, Does.Contain("\"com.unity.netcode.gameobjects\": \"2.11.0\""), "the rest of the manifest is kept");
            Assert.That(RelaySetupCheck.Check(_temp).PackageInManifest, Is.True);

            string second = RelaySetupCheck.AddPackage(_temp);
            Assert.That(second, Does.Contain("already"));
            Assert.That(File.ReadAllText(Path.Combine(_temp, "Packages", "manifest.json")), Is.EqualTo(manifest), "nothing added twice");
        }

        [Test]
        public void Message_NotInstalledNotLinked_SaysWhatToDoForEach()
        {
            var status = new RelaySetupStatus { ManifestPath = "Packages/manifest.json", ProjectId = string.Empty };
            Assert.That(status.Readiness, Is.EqualTo(RelayReadiness.NotInstalled));
            Assert.That(status.IsReady, Is.False);
            Assert.That(status.Message, Does.Contain(RelaySetup.PackageName + " is not in Packages/manifest.json")
                .And.Contain(RelaySetup.PackageVersion)
                .And.Contain("Cloud project: not linked.")
                .And.Contain("Project Settings > Services")
                .And.Contain(RelaySetup.DashboardUrl));
        }

        [Test]
        public void Message_InstalledAndLinked_NamesTheProject()
        {
            var status = new RelaySetupStatus
            {
                ManifestPath = "Packages/manifest.json",
                PackageInManifest = true,
                PackageCompiled = true,
                ProjectId = "0f8fad5b-d9cb-469f-a165-70867728950e",
                ProjectName = "Cursory",
                OrganizationName = "MindAttic",
            };

            Assert.That(status.IsReady, Is.True);
            Assert.That(status.Message, Does.Contain("is installed (MINDATTIC_UGS)")
                .And.Contain("linked to Cursory (0f8fad5b-d9cb-469f-a165-70867728950e) in MindAttic")
                .And.Contain("Products > Relay"));
        }

        [Test]
        public void Message_InTheManifestButNotCompiled_IsNotReadyYet()
        {
            var status = new RelaySetupStatus { ManifestPath = "m", PackageInManifest = true, ProjectId = "0f8fad5b-d9cb-469f-a165-70867728950e" };
            Assert.That(status.Readiness, Is.EqualTo(RelayReadiness.Ready));
            Assert.That(status.IsReady, Is.False, "waiting for the import");
            Assert.That(status.Message, Does.Contain("has not finished importing"));
        }
    }
}
