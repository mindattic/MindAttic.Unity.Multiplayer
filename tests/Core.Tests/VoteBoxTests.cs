using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// UM-T54: the engine-free vote rules (promoted from Unity.Cursory's UC-T10 VoteBox): quorum, early reject, timeout on
    /// the tick clock, departures, one vote at a time, cooldown, who may start, and kinds with payloads, checks, voter
    /// filters and effects.
    /// </summary>
    public class VoteBoxTests
    {
        private const ulong U1 = 1, U2 = 2, U3 = 3, U4 = 4, Latecomer = 9;
        private const int Rate = 30;

        private ManualTickClock _clock;
        private VoteBox _box;
        private readonly List<VoteStatus> _changes = new List<VoteStatus>();
        private readonly List<VoteResult> _ended = new List<VoteResult>();
        private readonly List<string> _effects = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualTickClock(Rate);
            _box = new VoteBox(_clock);
            _changes.Clear();
            _ended.Clear();
            _effects.Clear();
            _box.Changed += s => _changes.Add(s);
            _box.Ended += r => _ended.Add(r);
            _box.Register(new VoteKind("reset", "Reset the level?") { Passed = v => _effects.Add("reset") });
            _box.Register(new VoteKind("level", r => "Switch to Level " + r.Number + "?")
            {
                Check = r => r.Number >= 1 && r.Number <= 14 ? null : "There is no level " + r.Number + ".",
                Passed = v => _effects.Add("level " + v.Number),
            });
        }

        [TearDown]
        public void TearDown() => _box.Dispose();

        private VoteStartResult Reset(ulong starter, params ulong[] roster) => _box.Start(new VoteRequest("reset", starter), roster);

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        [TestCase(5, 4)]
        [TestCase(6, 4)]
        [TestCase(9, 6)]
        [TestCase(10, 7)]
        public void The_default_quorum_is_two_thirds_rounded_up(int voters, int quorum)
        {
            Assert.That(VoteRules.QuorumFor(voters, VoteRules.TwoThirds), Is.EqualTo(quorum));
            Assert.That(new VoteRules().QuorumFor(voters), Is.EqualTo(quorum));
        }

        [TestCase(4, 0.5, 2)]
        [TestCase(5, 0.5, 3)]
        [TestCase(3, 1.0, 3)]
        [TestCase(10, 0.01, 1)]
        public void The_quorum_fraction_is_configurable(int voters, double fraction, int quorum)
        {
            Assert.That(VoteRules.QuorumFor(voters, fraction), Is.EqualTo(quorum));
        }

        [Test]
        public void Bad_rules_are_refused()
        {
            Assert.That(() => VoteRules.QuorumFor(0, 0.5), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => VoteRules.QuorumFor(3, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => VoteRules.QuorumFor(3, 1.5), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new VoteBox(_clock, new VoteRules { TimeoutSeconds = 0 }), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new VoteBox(_clock, new VoteRules { CooldownSeconds = -1 }), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _box.Register(new VoteKind("x", "X?") { QuorumFraction = 2 }), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new VoteKind(" ", "X?"), Throws.ArgumentException);
            Assert.That(() => new VoteKind("x", ""), Throws.ArgumentException);
            Assert.That(() => new VoteBox(null), Throws.ArgumentNullException);
        }

        [Test]
        public void A_solo_vote_passes_at_once_with_the_starters_own_yes()
        {
            var result = Reset(U1, U1);
            Assert.That(result.Started, Is.True);
            Assert.That(result.Sequence, Is.EqualTo(1));
            Assert.That(_box.Current, Is.Null, "a one-voter quorum resolves in the same call");
            Assert.That(_effects, Is.EqualTo(new[] { "reset" }));
            Assert.That(_ended.Single().Ending, Is.EqualTo(VoteEnding.Passed));
        }

        [Test]
        public void Two_voters_need_both_and_a_no_rejects_it_early()
        {
            Reset(U1, U1, U2);
            Assert.That(_box.Current.Quorum, Is.EqualTo(2));
            Assert.That(_box.ChoiceOf(U1), Is.EqualTo(VoteChoice.Yes));
            Assert.That(_effects, Is.Empty, "nothing happens before it passes");

            Assert.That(_box.Cast(U2, false), Is.True);
            Assert.That(_box.Current, Is.Null, "1 yes + 1 no of 2: the quorum of 2 can no longer be reached");
            Assert.That(_ended.Single().Ending, Is.EqualTo(VoteEnding.Rejected));
            Assert.That(_effects, Is.Empty);
        }

        [Test]
        public void A_vote_passes_as_soon_as_yes_reaches_the_quorum()
        {
            Reset(U1, U1, U2, U3, U4); // quorum 3
            _box.Cast(U2, true);
            Assert.That(_box.IsActive, Is.True);
            _box.Cast(U3, true);
            Assert.That(_box.IsActive, Is.False);
            Assert.That(_effects, Is.EqualTo(new[] { "reset" }));
            Assert.That(_ended.Single().Yes, Is.EqualTo(3));
        }

        [Test]
        public void A_payload_reaches_the_title_and_the_effect()
        {
            var result = _box.Start(new VoteRequest("level", U2, 3), new[] { U1, U2, U3 });
            Assert.That(result.Started, Is.True);
            Assert.That(_box.Status.Title, Is.EqualTo("Switch to Level 3?"));
            Assert.That(_box.Status.Number, Is.EqualTo(3));
            Assert.That(_box.Status.StartedBy, Is.EqualTo(U2));
            _box.Cast(U3, true);
            Assert.That(_effects, Is.EqualTo(new[] { "level 3" }));
            Assert.That(_ended.Single().Number, Is.EqualTo(3));
            Assert.That(_ended.Single().Title, Is.EqualTo("Switch to Level 3?"));
        }

        [Test]
        public void A_text_payload_and_description_reach_every_status()
        {
            _box.Register(new VoteKind("map", r => "Play " + r.Text + "?") { Description = r => "Started by " + r.StartedBy });
            _box.Start(new VoteRequest("map", U1, text: "Docks"), new[] { U1, U2 });
            Assert.That(_box.Status.Title, Is.EqualTo("Play Docks?"));
            Assert.That(_box.Status.Text, Is.EqualTo("Docks"));
            Assert.That(_box.Status.Description, Is.EqualTo("Started by 1"));
        }

        [Test]
        public void An_invalid_request_is_refused_with_the_kinds_reason()
        {
            var result = _box.Start(new VoteRequest("level", U1, 99), new[] { U1, U2 });
            Assert.That(result.Started, Is.False);
            Assert.That(result.Refusal, Is.EqualTo(VoteRefusal.Invalid));
            Assert.That(result.Reason, Is.EqualTo("There is no level 99."));
            Assert.That(_box.Sequence, Is.EqualTo(0), "a refused start is not a vote");
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void Unknown_kinds_and_strangers_are_refused()
        {
            Assert.That(_box.Start(new VoteRequest("nope", U1), new[] { U1 }).Refusal, Is.EqualTo(VoteRefusal.UnknownKind));
            Assert.That(Reset(Latecomer, U1, U2).Refusal, Is.EqualTo(VoteRefusal.NotAVoter));
            Assert.That(Reset(U1, U1, U2).Started, Is.True);
        }

        [Test]
        public void One_vote_at_a_time()
        {
            Reset(U1, U1, U2, U3);
            var second = _box.Start(new VoteRequest("level", U2, 4), new[] { U1, U2, U3 });
            Assert.That(second.Refusal, Is.EqualTo(VoteRefusal.VoteInProgress));
            Assert.That(second.Reason, Is.EqualTo("A vote is already running."));
            Assert.That(_box.Status.Kind, Is.EqualTo("reset"));
            Assert.That(_box.Sequence, Is.EqualTo(1));
        }

        [Test]
        public void Who_may_start_is_a_rule()
        {
            _box.Rules.MayStart = id => id == U1;
            Assert.That(Reset(U2, U1, U2).Refusal, Is.EqualTo(VoteRefusal.NotAllowed));
            Assert.That(Reset(U1, U1, U2).Started, Is.True);
        }

        [Test]
        public void A_kind_can_leave_players_out_of_the_vote()
        {
            _box.Register(new VoteKind("kick", r => "Kick " + r.Number + "?")
            {
                MayVote = (r, id) => id != (ulong)r.Number,
                Passed = v => _effects.Add("kick " + v.Number),
            });
            _box.Start(new VoteRequest("kick", U1, (long)U3), new[] { U1, U2, U3 });
            Assert.That(_box.Current.Voters, Is.EquivalentTo(new[] { U1, U2 }));
            Assert.That(_box.Current.Quorum, Is.EqualTo(2), "2/3 of the two voters, not of three");
            Assert.That(_box.ChoiceOf(U3), Is.EqualTo(VoteChoice.NotAVoter));
            Assert.That(_box.Cast(U3, false), Is.False, "the target's ballot does not count");
            _box.Cast(U2, true);
            Assert.That(_effects, Is.EqualTo(new[] { "kick 3" }));
        }

        [Test]
        public void Nobody_left_to_vote_is_refused()
        {
            _box.Register(new VoteKind("ghost", "Boo?") { MayVote = (r, id) => false });
            Assert.That(_box.Start(new VoteRequest("ghost", U1), new[] { U1 }).Refusal, Is.EqualTo(VoteRefusal.NoVoters));
        }

        [Test]
        public void A_kind_can_have_its_own_quorum_and_time()
        {
            _box.Register(new VoteKind("unanimous", "All agree?") { QuorumFraction = 1, TimeoutSeconds = 2 });
            _box.Start(new VoteRequest("unanimous", U1), new[] { U1, U2, U3 });
            Assert.That(_box.Status.Quorum, Is.EqualTo(3));
            Assert.That(_box.Status.SecondsLeft, Is.EqualTo(2));
            _clock.Step(2 * Rate);
            Assert.That(_ended.Single().Ending, Is.EqualTo(VoteEnding.TimedOut));
        }

        [Test]
        public void An_undecided_vote_times_out_on_the_clock_and_counts_down_each_second()
        {
            _clock.Step(10);
            Reset(U1, U1, U2, U3);
            Assert.That(_box.Status.SecondsLeft, Is.EqualTo(15));
            _changes.Clear();

            _clock.Step(Rate);   // 14 s left
            _clock.Step(Rate);   // 13 s left
            Assert.That(_changes.Select(c => c.SecondsLeft), Is.EqualTo(new[] { 14, 13 }), "one change per second of the countdown");

            _clock.Step(15 * Rate - 2 * Rate - 1);
            Assert.That(_box.IsActive, Is.True, "one tick short of the timeout");
            _clock.Step();
            Assert.That(_box.IsActive, Is.False);
            Assert.That(_ended.Single().Ending, Is.EqualTo(VoteEnding.TimedOut));
            Assert.That(_changes.Last(), Is.Null, "the end is reported as no vote");
            Assert.That(_effects, Is.Empty);

            Assert.That(Reset(U2, U1, U2, U3).Sequence, Is.EqualTo(2), "a new vote can start once it lapses");
        }

        [TestCase(0, 15)]
        [TestCase(1, 15)]
        [TestCase(30, 14)]
        [TestCase(449, 1)]
        [TestCase(450, 0)]
        public void The_countdown_rounds_up_to_whole_seconds(int elapsed, int seconds)
        {
            Reset(U1, U1, U2);
            var vote = _box.Current;
            Assert.That(vote.SecondsLeft(elapsed, Rate), Is.EqualTo(seconds));
            Assert.That(vote.TicksLeft(elapsed), Is.EqualTo(Math.Max(0, 450 - elapsed)));
        }

        [Test]
        public void A_departure_recomputes_the_quorum_and_can_pass_the_vote()
        {
            Reset(U1, U1, U2, U3, U4); // quorum 3
            _box.Cast(U2, true);
            Assert.That(_box.Current.Quorum, Is.EqualTo(3));
            _box.RemoveVoter(U4); // 3 voters: quorum 2, already met
            Assert.That(_box.Current, Is.Null);
            Assert.That(_effects, Is.EqualTo(new[] { "reset" }));
        }

        [Test]
        public void A_departing_yes_voter_takes_their_vote_with_them()
        {
            Reset(U1, U1, U2, U3);
            _box.RemoveVoter(U1); // the starter leaves: 0 yes of 2, quorum 2
            Assert.That(_box.Current.Voters, Is.EquivalentTo(new[] { U2, U3 }));
            Assert.That(_box.Current.Yes, Is.Empty);
            Assert.That(_box.Current.Quorum, Is.EqualTo(2));
        }

        [Test]
        public void A_departure_can_make_the_quorum_unreachable()
        {
            Reset(U1, U1, U2, U3);
            _box.Cast(U2, false);
            _box.RemoveVoter(U1); // U2 No, U3 undecided: 2 of 2 can't be reached
            Assert.That(_ended.Single().Ending, Is.EqualTo(VoteEnding.Rejected));
        }

        [Test]
        public void A_vote_whose_last_voter_leaves_is_abandoned()
        {
            Reset(U1, U1, U2, U3);
            _box.RemoveVoter(U1);
            _box.RemoveVoter(U2);
            Assert.That(_box.Current.Quorum, Is.EqualTo(1));
            _box.RemoveVoter(U3);
            Assert.That(_ended.Single().Ending, Is.EqualTo(VoteEnding.Abandoned));
            Assert.That(_effects, Is.Empty);
        }

        [Test]
        public void Removing_someone_off_the_roster_and_repeat_ballots_change_nothing()
        {
            Reset(U1, U1, U2, U3);
            _changes.Clear();
            _box.RemoveVoter(U4);
            Assert.That(_box.Cast(U1, true), Is.False, "the starter already said Yes");
            Assert.That(_box.Cast(Latecomer, true), Is.False, "a late joiner does not vote");
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void Changing_your_mind_is_allowed()
        {
            Reset(U1, U1, U2, U3, U4);
            _box.Cast(U2, false);
            Assert.That(_box.ChoiceOf(U2), Is.EqualTo(VoteChoice.No));
            _box.Cast(U2, true);
            Assert.That(_box.ChoiceOf(U2), Is.EqualTo(VoteChoice.Yes));
            Assert.That(_box.Status.No, Is.EqualTo(0));
        }

        [Test]
        public void The_starter_may_start_undecided()
        {
            _box.Rules.StarterVotesYes = false;
            Reset(U1, U1);
            Assert.That(_box.ChoiceOf(U1), Is.EqualTo(VoteChoice.Undecided));
            _box.Cast(U1, true);
            Assert.That(_effects, Is.EqualTo(new[] { "reset" }));
        }

        [Test]
        public void A_failed_vote_cools_its_starter_down_and_nobody_else()
        {
            _box.Rules.CooldownSeconds = 10;
            Reset(U1, U1, U2);
            _box.Cast(U2, false);

            var again = Reset(U1, U1, U2);
            Assert.That(again.Refusal, Is.EqualTo(VoteRefusal.CoolingDown));
            Assert.That(again.Reason, Is.EqualTo("Wait 10 s before starting another vote."));
            Assert.That(_box.CooldownSecondsLeft(U1), Is.EqualTo(10));

            Assert.That(Reset(U2, U1, U2).Started, Is.True, "another player is not held up");
            _box.Cancel();

            _clock.Step(10 * Rate);
            Assert.That(_box.CooldownSecondsLeft(U1), Is.EqualTo(0));
            Assert.That(Reset(U1, U1, U2).Started, Is.True);
        }

        [Test]
        public void A_passed_vote_never_cools_anyone_down()
        {
            _box.Rules.CooldownSeconds = 10;
            Reset(U1, U1);
            Assert.That(Reset(U1, U1).Started, Is.True);
        }

        [Test]
        public void Cancel_ends_it_with_no_effect()
        {
            Reset(U1, U1, U2);
            Assert.That(_box.Cancel(), Is.True);
            Assert.That(_ended.Single().Ending, Is.EqualTo(VoteEnding.Cancelled));
            Assert.That(_box.Cancel(), Is.False);
            Assert.That(_effects, Is.Empty);
        }

        [Test]
        public void The_effect_has_run_before_the_end_is_reported()
        {
            int effectsWhenEnded = -1;
            _box.Ended += r => effectsWhenEnded = _effects.Count;
            Reset(U1, U1);
            Assert.That(effectsWhenEnded, Is.EqualTo(1));
        }

        [Test]
        public void Every_change_is_reported_and_the_end_says_how()
        {
            Reset(U1, U1, U2, U3);
            _box.Cast(U2, false);
            _box.Cancel();
            Assert.That(_changes.Select(c => c == null ? -1 : c.Yes + 10 * c.No), Is.EqualTo(new[] { 1, 11, -1 }));
            Assert.That(_ended.Single().No, Is.EqualTo(1), "the ended vote as it stood");
        }

        [Test]
        public void Status_is_what_an_overlay_shows()
        {
            Assert.That(_box.Status, Is.Null);
            Assert.That(_box.ChoiceOf(U1), Is.EqualTo(VoteChoice.NotAVoter));
            _clock.Step(100);
            _box.Start(new VoteRequest("level", U2, 9), new[] { U1, U2, U3 });
            _box.Cast(U3, false);
            _clock.Step(Rate + 1);
            Assert.That(_box.Status, Is.EqualTo(new VoteStatus(1, "level", "Switch to Level 9?", "", 9, "", U2, 1, 1, 3, 2, 14)));
            Assert.That(_box.Status.Undecided, Is.EqualTo(1));
        }

        [Test]
        public void A_disposed_box_stops_ticking()
        {
            Reset(U1, U1, U2);
            _box.Dispose();
            _clock.Step(20 * Rate);
            Assert.That(_ended, Is.Empty);
            Assert.That(() => Reset(U1, U1), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void The_words_an_overlay_uses()
        {
            var status = new VoteStatus(1, "reset", "Reset the level?", "", 0, "", U1, 1, 0, 3, 2, 12);
            Assert.That(VoteText.Tally(status), Is.EqualTo("Yes 1 · No 0 · needs 2 of 3 · 12 s"));
            Assert.That(VoteText.Standing(VoteChoice.Undecided), Is.EqualTo("Your vote"));
            Assert.That(VoteText.Standing(VoteChoice.Yes), Is.EqualTo("You voted Yes"));
            Assert.That(VoteText.Standing(VoteChoice.NotAVoter), Is.EqualTo("You are not voting on this one"));
            Assert.That(VoteText.CanVote(VoteChoice.Undecided), Is.True);
            Assert.That(VoteText.CanVote(VoteChoice.No), Is.False);
            Assert.That(VoteText.Ended("Kick Bob?", VoteEnding.Passed), Is.EqualTo("Vote passed: Kick Bob"));
            Assert.That(VoteText.Ended("Reset the level?", VoteEnding.Rejected), Is.EqualTo("Vote failed: Reset the level"));
            Assert.That(VoteText.Ended("", VoteEnding.TimedOut), Is.EqualTo("Vote timed out"));
            Assert.That(VoteText.Refused(VoteRefusal.CoolingDown, 3), Is.EqualTo("Wait 3 s before starting another vote."));
            foreach (VoteRefusal refusal in Enum.GetValues(typeof(VoteRefusal)))
            {
                Assert.That(VoteText.Refused(refusal), refusal == VoteRefusal.None ? Is.Empty : Is.Not.Empty, refusal.ToString());
            }
        }
    }
}
