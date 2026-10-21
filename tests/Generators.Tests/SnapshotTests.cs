using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Unity.Multiplayer.Generators.Tests
{
    /// <summary>Pins the generated code: what the PlayMode suite's TestAvatar compiles against, and every member shape.</summary>
    [TestFixture]
    public class SnapshotTests
    {
        [Test]
        public void TestAvatarFromThePackageGeneratesItsPlumbingAndCompilesClean()
        {
            string avatar = File.ReadAllText(Harness.PackagePath("Tests/Runtime/Entities/TestAvatar.cs"));

            RunResult result = Harness.Run(avatar);

            Assert.That(result.GeneratorDiagnostics, Is.Empty, Harness.Format(result.GeneratorDiagnostics));
            Assert.That(result.CompileDiagnostics, Is.Empty, Harness.Format(result.CompileDiagnostics));
            Assert.That(result.Sources.Keys, Is.EquivalentTo(new[] { "Unity.Multiplayer.Tests.TestAvatar.Net.g.cs", NetEntityGenerator.ProtocolHintName }));
            Harness.MatchesSnapshot(result.Source("Unity.Multiplayer.Tests.TestAvatar.Net.g.cs"), "TestAvatar.Net.g.cs");
            Harness.MatchesSnapshot(result.Source(NetEntityGenerator.ProtocolHintName), "TestAvatar.Protocol.g.cs");
        }

        [Test]
        public void EveryMemberShapeGeneratesAndCompilesClean()
        {
            const string source = @"
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum Team { Red = 1, Blue = 2 }

public struct Loadout : INetworkSerializable { public int Gun; }

public partial class Arena
{
    internal partial class Board : NetEntity
    {
        // [ToServer] without a sender parameter, no arguments, ownership off, a rate and a burst.
        [ToServer(RequireOwnership = false, RateLimit = 10, Burst = 3)]
        public partial void Ready();
        private void OnReady() { }

        // A handler that takes the sender; a keyword parameter name; string and array parameters.
        [ToServer]
        protected internal partial void Say(string @event, int[] codes);
        private void OnSay(string @event, int[] codes, NetSender sender) { }

        [ToClients(Delivery = Delivery.Unreliable)]
        public partial void Tick();
        void OnTick() { }

        [ToOwner]
        public partial void Equip(Loadout loadout, FixedString64Bytes label);
        void OnEquip(Loadout loadout, FixedString64Bytes label) { }

        // [ToClient] whose only parameter is the target.
        [ToClient]
        public partial void Poke(ulong target);
        void OnPoke() { }

        [Synced] private Team m_team = Team.Blue;
        [Synced(OnChanged = nameof(HealthChanged))] private float _health = 100f;
        [Synced] private Loadout _loadout;
        [Synced(OnChanged = nameof(RoundChanged))] private int round = -1;

        void HealthChanged(float previous, float current) { }
        void RoundChanged(int previous, int current) { }
    }
}
";
            RunResult result = Harness.Run(source);

            Assert.That(result.GeneratorDiagnostics, Is.Empty, Harness.Format(result.GeneratorDiagnostics));
            Assert.That(result.CompileDiagnostics, Is.Empty, Harness.Format(result.CompileDiagnostics));
            Harness.MatchesSnapshot(result.Source("Arena.Board.Net.g.cs"), "EveryShape.Net.g.cs");
            Harness.MatchesSnapshot(result.Source(NetEntityGenerator.ProtocolHintName), "EveryShape.Protocol.g.cs");
        }

        [Test]
        public void AnEntityWithoutNetworkMembersGetsNoFiles()
        {
            RunResult result = Harness.Run("public partial class Plain : Unity.Multiplayer.NetEntity { }");

            Assert.That(result.Sources, Is.Empty);
            Assert.That(result.Errors, Is.Empty, Harness.Format(result.Errors));
        }

        [Test]
        public void GeneratedCodeParsesAsCSharp9()
        {
            string avatar = File.ReadAllText(Harness.PackagePath("Tests/Runtime/Entities/TestAvatar.cs"));
            RunResult result = Harness.Run(avatar);

            foreach (var tree in result.Output.SyntaxTrees.Where(t => t.FilePath.EndsWith(".g.cs")))
            {
                Assert.That(((Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)tree.Options).LanguageVersion, Is.EqualTo(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp9));
                Assert.That(tree.GetDiagnostics(), Is.Empty, tree.FilePath);
            }
        }
    }
}
