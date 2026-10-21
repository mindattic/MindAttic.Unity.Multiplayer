// The game's own PlayMode tests, on the kit's test fixtures (Unity.Multiplayer.Testing): every test gets a real host or
// dedicated server and real clients over Unity Transport on a free loopback port, and the waits fail saying what never
// happened. Run them from Window > General > Test Runner > PlayMode, or in batch mode:
//   Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -assemblyNames MyGame.Tests
using System.Collections;
using NUnit.Framework;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace MyGame.Tests
{
    internal static class GamePrefabs
    {
        public static GameObject Player => Load("Game/Player");
        public static GameObject Crate => Load("Game/Crate");

        private static GameObject Load(string path)
        {
            var prefab = Resources.Load<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, "Resources/" + path + ".prefab is missing");
            return prefab;
        }
    }

    /// <summary>A host and one client: input goes up through [ToServer], the position comes back through [Synced], and the
    /// name and colour come from the roster.</summary>
    public class PlayerTests : NetSessionTest
    {
        public PlayerTests() : base(Topology.Host, clients: 1) { }

        protected override void OnSessionsCreated()
        {
            SetPlayerPrefab(GamePrefabs.Player);
            RegisterPrefab(GamePrefabs.Crate);
            Client.Identity = new PlayerIdentity("ann-token", "Ann", new PlayerColor(230, 80, 60));
        }

        [UnityTest]
        public IEnumerator TheServerMovesAPlayerThatAsksToMove_AndEveryoneSeesItsNameAndColour()
        {
            yield return StartAndJoinAll();

            var annOnHerClient = WaitForSpawn<Player>(Client, owner: Client.LocalClientId);
            yield return annOnHerClient;
            var annOnHost = WaitForSpawn<Player>(Server, owner: Client.LocalClientId);
            yield return annOnHost;

            Vector2 start = annOnHost.Result.Position;
            annOnHerClient.Result.Move(Vector2.right);            // what her arrow key does
            yield return WaitUntil(() => annOnHerClient.Result.Position.x > start.x + 1f,
                () => "Ann's own client never saw her move right; she is at " + annOnHerClient.Result.Position);

            yield return WaitUntil(() => annOnHost.Result.DisplayName == "Ann", () => "the host sees her as " + annOnHost.Result.DisplayName);
            Assert.That(annOnHost.Result.Color, Is.EqualTo(new Color32(230, 80, 60, 255)));
        }

        [UnityTest]
        public IEnumerator TheServerClampsInput_SoNobodyMovesFasterThanTheSpeed()
        {
            yield return StartAndJoinAll();
            var ann = WaitForSpawn<Player>(Client, owner: Client.LocalClientId);
            yield return ann;

            Vector2 start = ann.Result.Position;
            ann.Result.Move(new Vector2(0f, -50f));               // a cheating client
            float began = Time.realtimeSinceStartup;
            yield return WaitUntil(() => ann.Result.Position.y < start.y - 1f, "Ann never moved down");
            float seconds = Time.realtimeSinceStartup - began;
            Assert.That(start.y - ann.Result.Position.y, Is.LessThanOrEqualTo(Player.Speed * (seconds + 0.5f)));
        }
    }

    /// <summary>The keyed crates: spawned by key on the server, found by key on a client, pushed, and put back by a vote.</summary>
    public class CrateTests : NetSessionTest
    {
        public CrateTests() : base(Topology.Host, clients: 1) { }

        protected override void OnSessionsCreated()
        {
            RegisterPrefab(GamePrefabs.Crate);
            foreach (NetSession session in Sessions)
            {
                VoteBoard.RegisterPrefab(session);                // what NetVotes does in the scene
            }
        }

        [UnityTest]
        public IEnumerator EveryCrateIsFoundByItsKeyOnAClient()
        {
            yield return StartAndJoinAll();
            Arena.Serve(Server, GamePrefabs.Crate);

            for (int i = 0; i < Arena.CrateCount; i++)
            {
                string key = Arena.CrateKey(i);
                Crate crate = null;
                yield return WaitUntil(() => Client.Objects.TryGet(key, out crate), "the client never got " + key);
                yield return WaitForSynced(() => crate.Position, Arena.CrateStart(i), key + "'s position");
                Assert.That(crate.Key, Is.EqualTo(key));
            }
        }

        [UnityTest]
        public IEnumerator ARestartVotePutsEveryCrateBack()
        {
            yield return StartAndJoinAll();
            Arena arena = Arena.Serve(Server, GamePrefabs.Crate);
            VoteBoard board = VoteBoard.Spawn(Server);
            board.AddKind(arena.CreateRestartVote());

            Server.Objects.Get<Crate>(Arena.CrateKey(0)).Position = new Vector2(5f, 5f);   // pushed away
            Crate pushed = null;
            yield return WaitUntil(() => Client.Objects.TryGet(Arena.CrateKey(0), out pushed) && pushed.Position == new Vector2(5f, 5f),
                "the client never saw crate:0 pushed");

            VoteBoard onClient = null;
            yield return WaitUntil(() => (onClient = VoteBoard.For(Client)) != null, "the client never got the vote board");
            onClient.StartVote(Arena.RestartVote);                // the client starts it (its Yes counts)
            yield return WaitUntil(() => board.IsVoteOpen, "the vote never opened");
            board.CastVote(true);                                 // the host says Yes too: 2 of 2

            Crate back = null;
            yield return WaitUntil(() => Client.Objects.TryGet(Arena.CrateKey(0), out back) && back != pushed && back.Position == Arena.CrateStart(0),
                "crate:0 never came back to its start after the vote");
        }
    }

    /// <summary>The nightly smoke without Docker: a dedicated server and two bots (GameBot) that join by code and push crates.</summary>
    public class BotTests : NetBotTest
    {
        public BotTests() : base(bots: 2) { }

        protected override float BotTimeoutSeconds => 60f;

        protected override void OnSessionsCreated()
        {
            SetPlayerPrefab(GamePrefabs.Player);
            RegisterPrefab(GamePrefabs.Crate);
            Arena.Serve(Server, GamePrefabs.Crate);               // the server's rules, on the fixture's server
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TwoBotsJoinByCode_AndEachPushesItsCrate()
        {
            yield return RunBots(role => new GameBot(), "A", "B");

            foreach (NetBotRunner bot in Bots)
            {
                Assert.That(bot.PassMessage, Does.Contain(GameBot.CrateFor(bot.Role)));
                Crate crate = Server.Objects.Get<Crate>(GameBot.CrateFor(bot.Role));
                Assert.That(crate.Position.y, Is.LessThan(Arena.CrateStart(0).y - 1f), "the server moved " + crate.Key);
            }
        }
    }
}
