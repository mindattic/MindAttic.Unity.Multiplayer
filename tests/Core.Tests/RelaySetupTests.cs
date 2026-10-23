using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>UM-T71: what Unity Relay join codes need, and the sentences shown when something is missing.</summary>
    [TestFixture]
    public class RelaySetupTests
    {
        [TestCase(false, null, RelayReadiness.NotInstalled)]
        [TestCase(false, "0f8fad5b-d9cb-469f-a165-70867728950e", RelayReadiness.NotInstalled)]
        [TestCase(true, null, RelayReadiness.NotLinked)]
        [TestCase(true, "", RelayReadiness.NotLinked)]
        [TestCase(true, "   ", RelayReadiness.NotLinked)]
        [TestCase(true, "0f8fad5b-d9cb-469f-a165-70867728950e", RelayReadiness.Ready)]
        public void Check_ThePackageThenTheLink(bool installed, string projectId, RelayReadiness expected)
        {
            Assert.That(RelaySetup.Check(installed, projectId), Is.EqualTo(expected));
        }

        [Test]
        public void Describe_SaysWhatToDo()
        {
            Assert.That(RelaySetup.Describe(RelayReadiness.NotInstalled), Does.Contain(RelaySetup.PackageName).And.Contain("MINDATTIC_UGS"));
            Assert.That(RelaySetup.Describe(RelayReadiness.NotLinked), Does.Contain("isn't linked").And.Contain("Project Settings > Services")
                .And.Contain("tools/link-unity-cloud.ps1").And.Contain(RelaySetup.DashboardUrl));
            Assert.That(RelaySetup.Describe(RelayReadiness.Ready), Does.Contain("set up"));
        }

        [TestCase(null, "production")]
        [TestCase("", "production")]
        [TestCase("  staging ", "staging")]
        public void EnvironmentOrDefault_IsProductionWhenEmpty(string given, string expected)
        {
            Assert.That(RelaySetup.EnvironmentOrDefault(given), Is.EqualTo(expected));
        }

        [TestCase("0f8fad5b-d9cb-469f-a165-70867728950e", true)]
        [TestCase("0f8fad5bd9cb469fa16570867728950e", true)]
        [TestCase(" {0f8fad5b-d9cb-469f-a165-70867728950e} ", true)]
        [TestCase("my-project", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsProjectId_AcceptsGuids(string value, bool expected)
        {
            Assert.That(RelaySetup.IsProjectId(value), Is.EqualTo(expected));
        }

        [Test]
        public void ExplainFailure_NotLinked()
        {
            Assert.That(RelaySetup.ExplainFailure("UnityProjectNotLinkedException", null,
                "To use Unity's dashboard services, you need to link your Unity project to a project ID.", null), Is.EqualTo(RelaySetup.NotLinkedMessage));
            Assert.That(RelaySetup.ExplainFailure("ServicesInitializationException", null, "ProjectId is null or empty", null), Is.EqualTo(RelaySetup.NotLinkedMessage));
        }

        [Test]
        public void ExplainFailure_RelayOff()
        {
            Assert.That(RelaySetup.ExplainFailure("RelayServiceException", "Forbidden", "Forbidden", null), Is.EqualTo(RelaySetup.RelayOffMessage));
            Assert.That(RelaySetup.ExplainFailure("RequestFailedException", "401", "HTTP/1.1 401 Unauthorized", null), Is.EqualTo(RelaySetup.RelayOffMessage));
        }

        [Test]
        public void ExplainFailure_MissingEnvironment_NamesIt()
        {
            string message = RelaySetup.ExplainFailure("RequestFailedException", "EnvironmentNotFound", "Environment not found", "staging");
            Assert.That(message, Does.Contain("\"staging\"").And.Contain("Relay Environment"));
        }

        [Test]
        public void ExplainFailure_Offline()
        {
            Assert.That(RelaySetup.ExplainFailure("RequestFailedException", "0", "Cannot resolve destination host", null), Is.EqualTo(RelaySetup.UnreachableMessage));
            Assert.That(RelaySetup.ExplainFailure("RelayServiceException", "NetworkError", "Request timeout", null), Is.EqualTo(RelaySetup.UnreachableMessage));
        }

        [Test]
        public void ExplainFailure_AnythingElse_KeepsTheMessage()
        {
            Assert.That(RelaySetup.ExplainFailure("RelayServiceException", "AllocationNotFound", "  Allocation gone ", null), Is.EqualTo("Allocation gone"));
            Assert.That(RelaySetup.ExplainFailure(null, null, null, null), Is.EqualTo("Unity Relay failed."));
        }
    }
}
