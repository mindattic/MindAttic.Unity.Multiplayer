using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Generators.Tests
{
    /// <summary>The generated <c>[assembly: NetProtocol]</c>: Core's algorithm, one hash per assembly, wire-relevant only.</summary>
    [TestFixture]
    public class ProtocolTests
    {
        private const string Usings = "using MindAttic.Unity.Multiplayer;\nusing MindAttic.Unity.Multiplayer.Core;\nusing UnityEngine;\n";

        private const string Avatar = Usings + @"
namespace Game
{
    public partial class Avatar : NetEntity
    {
        [ToServer(Delivery = Delivery.Unreliable, RateLimit = RATE)]
        public partial void Move(Vector2 p);
        void OnMove(Vector2 p) { }

        [ToClient] public partial void Tell(ulong to, int code);
        void OnTell(int code) { }

        [Synced] float _mass;
        [Synced] int _score;
    }
}";

        private const string Door = Usings + @"
namespace Game
{
    public partial class Door : NetEntity
    {
        [ToClients] public partial void Open();
        void OnOpen() { }
    }
}";

        private static string ProtocolFile(params string[] sources)
        {
            RunResult result = Harness.Run(sources);
            Assert.That(result.Errors, Is.Empty, Harness.Format(result.Errors));
            return result.Source(NetEntityGenerator.ProtocolHintName);
        }

        private static string Hash(string protocolFile)
        {
            return Regex.Match(protocolFile, "NetProtocol\\(\"([0-9a-f]{16})\", (\\d+)\\)").Groups[1].Value;
        }

        private static string Avatars(string rate = "5", string moveType = "Vector2", string fields = "[Synced] float _mass;\n        [Synced] int _score;")
        {
            return Avatar.Replace("RATE", rate).Replace("Move(Vector2 p)", "Move(" + moveType + " p)").Replace("OnMove(Vector2 p)", "OnMove(" + moveType + " p)")
                .Replace("[Synced] float _mass;\n        [Synced] int _score;", fields);
        }

        [Test]
        public void HashIsCoresProtocolHashOverTheListedSignatures()
        {
            string file = ProtocolFile(Avatars(), Door);

            List<string> signatures = file.Split('\n').Where(l => l.StartsWith("//   ")).Select(l => l.Substring(5)).ToList();
            Assert.That(signatures, Is.EqualTo(new[]
            {
                "rpc ToClient Game.Avatar.Tell(int) Reliable",
                "rpc ToClients Game.Door.Open() Reliable",
                "rpc ToServer Game.Avatar.Move(UnityEngine.Vector2) Unreliable",
                "synced Game.Avatar#0 Mass: float",
                "synced Game.Avatar#1 Score: int",
            }));
            Assert.That(Hash(file), Is.EqualTo(ProtocolHash.Compute(signatures).ToString()));
            Assert.That(file, Does.Contain("NetProtocol(\"" + Hash(file) + "\", 5)"));
        }

        [Test]
        public void HashIsTheSameWhateverOrderTheTypesAreDeclaredIn()
        {
            Assert.That(Hash(ProtocolFile(Door, Avatars())), Is.EqualTo(Hash(ProtocolFile(Avatars(), Door))));
        }

        [Test]
        public void ChangingAParameterTypeChangesTheHash()
        {
            Assert.That(Hash(ProtocolFile(Avatars(moveType: "Vector3"))), Is.Not.EqualTo(Hash(ProtocolFile(Avatars()))));
        }

        [Test]
        public void ReorderingSyncedFieldsChangesTheHashBecauseVariablesMatchByOrder()
        {
            string swapped = Avatars(fields: "[Synced] int _score;\n        [Synced] float _mass;");
            Assert.That(Hash(ProtocolFile(swapped)), Is.Not.EqualTo(Hash(ProtocolFile(Avatars()))));
        }

        [Test]
        public void ServerSidePolicyLikeRateLimitDoesNotChangeTheHash()
        {
            Assert.That(Hash(ProtocolFile(Avatars(rate: "50"))), Is.EqualTo(Hash(ProtocolFile(Avatars()))));
        }

        [Test]
        public void AnAssemblyWithoutNetworkMembersGetsNoProtocolAttribute()
        {
            RunResult result = Harness.Run(Usings + "public partial class Quiet : NetEntity { }");
            Assert.That(result.Sources.ContainsKey(NetEntityGenerator.ProtocolHintName), Is.False);
        }

        [Test]
        public void GeneratorDomainLineMatchesCore()
        {
            string symbols = File.ReadAllText(Path.Combine(Harness.RepoRoot, "src", "Generators", "Symbols.cs"));
            Assert.That(symbols, Does.Contain("ProtocolDomain = \"" + ProtocolHash.Domain + "\""));
        }
    }

    /// <summary>The NetEntity stand-in in Stubs/ must keep the real adapter class's generated-code surface.</summary>
    [TestFixture]
    public class NetEntityStubTests
    {
        [TestCase("protected virtual void NetBindSynced()")]
        [TestCase("protected virtual void NetUnbindSynced()")]
        [TestCase("protected bool NetReceiveToServer(string rpc, ulong senderClientId, bool requireOwnership, int rateLimit, int burst, out NetSender sender)")]
        [TestCase("protected bool NetCanSendToClients(string rpc)")]
        [TestCase("protected bool NetReceiveFromServer(string rpc, ulong senderClientId)")]
        [TestCase("protected void NetWriteSynced<T>(NetworkVariable<T> variable, T value, string name)")]
        [TestCase("public abstract class NetEntity : NetworkBehaviour")]
        public void RealNetEntityDeclares(string member)
        {
            string real = File.ReadAllText(Harness.PackagePath("Runtime/Entities/NetEntity.cs"));
            Assert.That(real, Does.Contain(member));
        }
    }
}
