using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T54: the votes kit over a real Unity Transport on loopback. A Host and two clients (Ann, Bob); one server-owned
    /// <see cref="VoteBoard"/> spawned from the package's own prefab with <see cref="VoteBoard.Spawn"/>, serving a game's
    /// kinds: "reset", "level" (a number payload with a check) and "map" (a text payload with a description), plus the
    /// built-in kick. Pass, early reject, timeout, a leaver, refusals and a kick, as every peer sees them.
    /// </summary>
    public class VoteBoardTests : NetSessionTest
    {
        private NetSession _host;
        private NetSession _ann;
        private NetSession _bob;

        private VoteBoard _onHost;
        private VoteBoard _onAnn;
        private VoteBoard _onBob;
        private ManualTickClock _clock;

        private readonly List<string> _effects = new List<string>();
        private readonly Dictionary<VoteBoard, List<VoteResult>> _ended = new Dictionary<VoteBoard, List<VoteResult>>();
        private readonly Dictionary<VoteBoard, List<string>> _refused = new Dictionary<VoteBoard, List<string>>();

        public VoteBoardTests() : base(Topology.Host, clients: 2)
        {
        }

        protected override string GameVersion => "um-t54";

        protected override void OnSessionsCreated()
        {
            _host = Server;
            _ann = Clients[0];
            _bob = Clients[1];
            _host.Identity = new PlayerIdentity("host-token", "Host", PlayerColor.White);
            _ann.Identity = new PlayerIdentity("ann-token", "Ann", new PlayerColor(0xE2, 0x4B, 0x4A));
            _bob.Identity = new PlayerIdentity("bob-token", "Bob", new PlayerColor(0x1D, 0x9E, 0x75));
            foreach (NetSession session in Sessions)
            {
                Assert.That(VoteBoard.RegisterPrefab(session), Is.True, "the package's own prefab registers");
                Assert.That(VoteBoard.RegisterPrefab(session), Is.False, "and only once");
            }
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _effects.Clear();
            _ended.Clear();
            _refused.Clear();
            _host = _ann = _bob = null;
            yield return null;
        }

        /// <summary>Everyone in, the board spawned by the server and seen by both clients, the game's kinds registered.</summary>
        private IEnumerator StartBoard(VoteRules rules = null, bool manualClock = false)
        {
            yield return StartAndJoinAll();

            _onHost = VoteBoard.Spawn(_host, rules);
            if (manualClock)
            {
                _clock = new ManualTickClock(30);
                _onHost.Serve(rules, _clock);
            }

            _onHost.AddKind(new VoteKind("reset", "Reset the level?") { Passed = v => _effects.Add("reset") })
                .AddKind(new VoteKind("level", r => "Switch to Level " + r.Number + "?")
                {
                    Check = r => r.Number >= 1 && r.Number <= 5 ? null : "There is no level " + r.Number + ".",
                    Passed = v => _effects.Add("level " + v.Number),
                })
                .AddKind(new VoteKind("map", r => "Play " + r.Text + "?") { Description = r => "A new map for everyone", Passed = v => _effects.Add("map " + v.Text) })
                .AddKickKind("Voted out.");

            Awaiting<VoteBoard> onAnn = WaitForCopy(_ann, _onHost);
            Awaiting<VoteBoard> onBob = WaitForCopy(_bob, _onHost);
            yield return onAnn;
            yield return onBob;
            _onAnn = onAnn.Result;
            _onBob = onBob.Result;

            foreach (VoteBoard board in All)
            {
                var ended = _ended[board] = new List<VoteResult>();
                var refused = _refused[board] = new List<string>();
                board.VoteEnded += r => ended.Add(r);
                board.VoteRefused += (why, reason) => refused.Add(why + ": " + reason);
            }

            Assert.That(VoteBoard.For(_host), Is.SameAs(_onHost));
            Assert.That(VoteBoard.For(_ann), Is.SameAs(_onAnn));
        }

        private IEnumerable<VoteBoard> All => new[] { _onHost, _onAnn, _onBob };

        private IEnumerator WaitEverywhere(System.Func<VoteBoard, bool> condition, string what)
        {
            yield return WaitUntil(() => All.All(condition), () => what + " (host " + _onHost.Status + ", Ann " + _onAnn.Status + ", Bob " + _onBob.Status + ")");
        }

        private static IEnumerator Settle()
        {
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator A_custom_kind_with_a_number_reaches_every_peer_and_passing_runs_its_effect_on_the_server()
        {
            yield return StartBoard();

            _onAnn.StartVote("level", 3);
            yield return WaitEverywhere(b => b.Status != null && b.ChoiceOf(_ann.LocalClientId) == VoteChoice.Yes, "every peer sees Ann's level vote with her Yes");
            foreach (VoteBoard board in All)
            {
                VoteStatus s = board.Status;
                Assert.That(s.Kind, Is.EqualTo("level"));
                Assert.That(s.Title, Is.EqualTo("Switch to Level 3?"));
                Assert.That(s.Number, Is.EqualTo(3));
                Assert.That(s.StartedBy, Is.EqualTo(_ann.LocalClientId));
                Assert.That(s.Yes, Is.EqualTo(1));
                Assert.That(s.Voters, Is.EqualTo(3));
                Assert.That(s.Quorum, Is.EqualTo(2), "ceil(2/3 x 3)");
                Assert.That(s.SecondsLeft, Is.InRange(14, 15));
            }

            Assert.That(_onAnn.LocalChoice, Is.EqualTo(VoteChoice.Yes));
            Assert.That(_onBob.LocalChoice, Is.EqualTo(VoteChoice.Undecided));
            Assert.That(_onHost.LocalChoice, Is.EqualTo(VoteChoice.Undecided), "the host's own player votes too");
            Assert.That(_effects, Is.Empty, "nothing happens before it passes");

            _onBob.CastVote(true);
            yield return WaitEverywhere(b => b.Status == null && _ended[b].Count == 1, "the vote ended everywhere");
            foreach (VoteBoard board in All)
            {
                VoteResult r = _ended[board][0];
                Assert.That(r.Ending, Is.EqualTo(VoteEnding.Passed));
                Assert.That(r.Kind, Is.EqualTo("level"));
                Assert.That(r.Number, Is.EqualTo(3));
                Assert.That(r.Yes, Is.EqualTo(2));
            }

            Assert.That(_effects, Is.EqualTo(new[] { "level 3" }), "the effect ran once, on the server");
        }

        [UnityTest]
        public IEnumerator A_text_payload_and_description_reach_every_peer()
        {
            yield return StartBoard();
            _onBob.StartVote("map", text: "Docks");
            yield return WaitEverywhere(b => b.Status != null, "Bob's map vote");
            foreach (VoteBoard board in All)
            {
                Assert.That(board.Status.Title, Is.EqualTo("Play Docks?"));
                Assert.That(board.Status.Text, Is.EqualTo("Docks"));
                Assert.That(board.Status.Description, Is.EqualTo("A new map for everyone"));
            }

            _onHost.CastVote(true);
            yield return WaitEverywhere(b => _ended[b].Count == 1, "the vote ended everywhere");
            Assert.That(_effects, Is.EqualTo(new[] { "map Docks" }));
            Assert.That(_ended[_onAnn][0].Text, Is.EqualTo("Docks"));
        }

        [UnityTest]
        public IEnumerator A_vote_is_rejected_early_once_its_quorum_is_unreachable()
        {
            yield return StartBoard();
            _onHost.StartVote("reset");
            yield return WaitEverywhere(b => b.Status != null, "every peer sees the host's reset vote");

            _onAnn.CastVote(false);
            yield return WaitEverywhere(b => b.Status != null && b.Status.No == 1, "Ann's No");
            Assert.That(_onBob.ChoiceOf(_ann.LocalClientId), Is.EqualTo(VoteChoice.No));

            _onBob.CastVote(false);
            yield return WaitEverywhere(b => b.Status == null && _ended[b].Count == 1, "the vote ended everywhere");
            Assert.That(_ended[_onBob][0].Ending, Is.EqualTo(VoteEnding.Rejected));
            Assert.That(_effects, Is.Empty, "a rejected vote does nothing");
        }

        [UnityTest]
        public IEnumerator An_undecided_vote_times_out_on_the_tick_clock()
        {
            yield return StartBoard(new VoteRules { TimeoutSeconds = 10 }, manualClock: true);
            _onAnn.StartVote("level", 2);
            yield return WaitEverywhere(b => b.Status != null && b.Status.SecondsLeft == 10, "the vote, 10 s left");

            _clock.Step(4 * 30);
            yield return WaitEverywhere(b => b.Status != null && b.Status.SecondsLeft == 6, "the countdown at 6 s");

            _clock.Step(6 * 30 - 1);
            yield return Settle();
            Assert.That(_onBob.Status, Is.Not.Null, "one tick short of the timeout");

            _clock.Step();
            yield return WaitEverywhere(b => b.Status == null && _ended[b].Count == 1, "the vote lapsed everywhere");
            Assert.That(_ended[_onBob][0].Ending, Is.EqualTo(VoteEnding.TimedOut));
            Assert.That(_effects, Is.Empty);

            _onBob.StartVote("reset");
            yield return WaitEverywhere(b => b.Status != null && b.Status.Sequence == 2, "a new vote can start once it lapses");
        }

        [UnityTest]
        public IEnumerator A_player_leaving_mid_vote_recomputes_the_quorum()
        {
            yield return StartBoard();
            _onAnn.StartVote("reset");
            yield return WaitEverywhere(b => b.Status != null && b.Status.Quorum == 2, "the vote, quorum 2 of 3");

            _bob.Leave();
            yield return WaitUntil(() => _onHost.Status != null && _onHost.Status.Voters == 2 && _onAnn.Status != null && _onAnn.Status.Voters == 2,
                () => "Bob's departure never reached the vote (host " + _onHost.Status + ", Ann " + _onAnn.Status + ")");
            Assert.That(_onAnn.Status.Quorum, Is.EqualTo(2), "ceil(2/3 x 2)");

            _onHost.CastVote(true);
            yield return WaitUntil(() => _ended[_onHost].Count == 1 && _ended[_onAnn].Count == 1, "the vote never passed");
            Assert.That(_ended[_onAnn][0].Ending, Is.EqualTo(VoteEnding.Passed));
            Assert.That(_effects, Is.EqualTo(new[] { "reset" }));
        }

        [UnityTest]
        public IEnumerator Refused_starts_change_nothing_and_only_the_starter_hears_why()
        {
            yield return StartBoard();
            _onAnn.StartVote("level", 99);
            _onAnn.StartVote("nope");
            yield return WaitUntil(() => _refused[_onAnn].Count == 2, () => "Ann never heard why (" + string.Join(", ", _refused[_onAnn]) + ")");
            Assert.That(_refused[_onAnn][0], Is.EqualTo("Invalid: There is no level 99."));
            Assert.That(_refused[_onAnn][1], Does.StartWith("UnknownKind: "));
            Assert.That(All.All(b => b.Status == null), Is.True);

            yield return new WaitForSecondsRealtime(1.1f); // StartRateLimit: two starts a second per player
            _onAnn.StartVote("reset");
            yield return WaitEverywhere(b => b.Status != null, "Ann's vote");
            _onBob.StartVote("level", 4);
            yield return WaitUntil(() => _refused[_onBob].Count == 1, "Bob never heard that a vote is running");
            Assert.That(_refused[_onBob][0], Is.EqualTo("VoteInProgress: A vote is already running."));
            Assert.That(_onHost.Status.Kind, Is.EqualTo("reset"), "one vote at a time");
            Assert.That(_onHost.Status.Sequence, Is.EqualTo(1));
            yield return Settle();
            Assert.That(_refused[_onHost], Is.Empty, "refusals go to the starter only");
            Assert.That(_refused[_onAnn], Has.Count.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator A_kick_vote_leaves_its_target_out_and_passing_kicks_them()
        {
            yield return StartBoard();
            ulong bob = _bob.LocalClientId;

            _onAnn.StartKickVote(bob);
            yield return WaitEverywhere(b => b.Status != null && b.Status.Kind == VoteBoard.KickKindId, "every peer sees the kick vote");
            Assert.That(_onBob.Status.Title, Is.EqualTo("Kick Bob?"), "named from Bob's seat");
            Assert.That(_onBob.Status.Voters, Is.EqualTo(2), "Bob does not vote on his own kick");
            Assert.That(_onBob.Status.Quorum, Is.EqualTo(2));
            yield return WaitUntil(() => _onBob.ChoiceOf(_ann.LocalClientId) == VoteChoice.Yes, "Ann's ballot");
            Assert.That(_onBob.LocalChoice, Is.EqualTo(VoteChoice.NotAVoter));

            _onHost.CastVote(true);
            yield return WaitUntil(() => _bob.State == SessionState.Disconnected, () => "Bob was never removed: " + _bob.State + " " + _bob.LastDisconnect);
            Assert.That(_bob.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Kicked));
            Assert.That(_bob.LastDisconnect.Message, Does.Contain("Voted out."));
            yield return WaitUntil(() => _ended[_onHost].Count == 1 && _ended[_onAnn].Count == 1, "the kick vote never ended");
            Assert.That(_ended[_onAnn][0].Ending, Is.EqualTo(VoteEnding.Passed));
        }

        [UnityTest]
        public IEnumerator Kick_votes_against_the_host_or_yourself_are_refused()
        {
            yield return StartBoard();
            _onAnn.StartKickVote(NetSender.ServerClientId);
            _onAnn.StartKickVote(_ann.LocalClientId);
            yield return new WaitForSecondsRealtime(1.1f); // StartRateLimit: two starts a second per player
            _onAnn.StartKickVote(12345);
            yield return WaitUntil(() => _refused[_onAnn].Count == 3, () => "Ann heard " + string.Join(", ", _refused[_onAnn]));
            Assert.That(_refused[_onAnn], Is.EqualTo(new[]
            {
                "Invalid: The host can't be kicked.",
                "Invalid: You can't start a vote to kick yourself.",
                "Invalid: That player isn't in the game.",
            }));
        }

        [UnityTest]
        public IEnumerator Only_the_host_starts_votes_when_the_rules_say_so()
        {
            yield return StartBoard(new VoteRules { MayStart = id => id == NetSender.ServerClientId });
            _onAnn.StartVote("reset");
            yield return WaitUntil(() => _refused[_onAnn].Count == 1, "Ann was never refused");
            Assert.That(_refused[_onAnn][0], Does.StartWith("NotAllowed: "));
            _onHost.StartVote("reset");
            yield return WaitEverywhere(b => b.Status != null, "the host's vote");
        }
    }

    /// <summary>UM-T54: <see cref="NetVotes"/> beside a real <see cref="NetBootstrap"/> spawns and serves the board by itself.</summary>
    public class NetVotesTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
                _go = null;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator A_server_with_NetVotes_serves_a_board_with_its_kinds()
        {
            _go = new GameObject("NetVotes test");
            _go.SetActive(false);
            NetworkManager manager = _go.AddComponent<NetworkManager>();
            _go.AddComponent<UnityTransport>();
            NetBootstrap bootstrap = _go.AddComponent<NetBootstrap>();
            bootstrap.Configure(new NetConfig { Topology = Topology.Host, Port = FreePort.Udp(), GameVersion = "um-t54" });
            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }

            manager.NetworkConfig.EnableSceneManagement = false;
            NetVotes votes = _go.AddComponent<NetVotes>();
            int passed = 0;
            votes.AddKind(new VoteKind("restart", "Restart the round?") { Passed = v => passed++ });
            _go.SetActive(true);
            yield return null; // Start: the prefab is registered before anything serves

            Assert.That(bootstrap.Session.Prefabs.Contains(VoteBoard.LoadPrefab()), Is.True);
            if (!bootstrap.Session.IsActive)
            {
                Assert.That(bootstrap.Session.StartHost(), Is.True);
            }

            float deadline = Time.realtimeSinceStartup + 5f;
            while (votes.Board == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(votes.Board, Is.Not.Null, "the server spawned its board");
            Assert.That(votes.Board.Box, Is.Not.Null, "and serves it");
            Assert.That(votes.Board.Box.TryGetKind("restart", out _), Is.True, "with the kind added before it existed");
            Assert.That(votes.Board.Box.TryGetKind(VoteBoard.KickKindId, out _), Is.True, "and the built-in kick");

            if (bootstrap.Session.IsHost)
            {
                votes.Board.StartVote("restart"); // the host alone: its own Yes passes it
                deadline = Time.realtimeSinceStartup + 5f;
                while (passed == 0 && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(passed, Is.EqualTo(1));
            }
        }
    }
}
