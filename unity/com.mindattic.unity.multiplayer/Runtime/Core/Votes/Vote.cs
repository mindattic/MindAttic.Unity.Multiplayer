using System;
using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A running vote on the server, as its <see cref="VoteKind.Passed"/> effect and <see cref="VoteBox.Current"/> see it.
    /// The roster when it started (less anyone the kind leaves out) is its voter list; it passes once Yes reaches
    /// <see cref="Quorum"/> and is rejected as soon as it no longer can. Players are identified by their network client id.
    /// Only the <see cref="VoteBox"/> changes it.
    /// </summary>
    public sealed class Vote
    {
        private readonly List<ulong> _voters;
        private readonly List<ulong> _yes = new List<ulong>();
        private readonly List<ulong> _no = new List<ulong>();

        internal Vote(int sequence, VoteKind kind, VoteRequest request, string title, string description, IEnumerable<ulong> voters,
            double quorumFraction, long startedAtTick, long timeoutTicks)
        {
            Sequence = sequence;
            Kind = kind;
            Request = request;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            _voters = new List<ulong>(voters);
            QuorumFraction = quorumFraction;
            StartedAtTick = startedAtTick;
            TimeoutTicks = timeoutTicks;
            Quorum = VoteRules.QuorumFor(_voters.Count, quorumFraction);
        }

        /// <summary>1 for the box's first vote, counting up.</summary>
        public int Sequence { get; }

        /// <summary>What is being voted on.</summary>
        public VoteKind Kind { get; }

        /// <summary>The request as the starter sent it.</summary>
        public VoteRequest Request { get; }

        /// <summary>The request's number payload.</summary>
        public long Number => Request.Number;

        /// <summary>The request's text payload (never null).</summary>
        public string Text => Request.Text;

        /// <summary>Client id of the player who started it.</summary>
        public ulong StartedBy => Request.StartedBy;

        /// <summary>The question, worded when it started.</summary>
        public string Title { get; }

        /// <summary>The optional second line; empty when there is none.</summary>
        public string Description { get; }

        /// <summary>Who may vote: the roster at the start, less the kind's exclusions and anyone who has left since.</summary>
        public IReadOnlyList<ulong> Voters => _voters;

        /// <summary>The voters who said Yes.</summary>
        public IReadOnlyList<ulong> Yes => _yes;

        /// <summary>The voters who said No.</summary>
        public IReadOnlyList<ulong> No => _no;

        /// <summary>Voters who have voted neither way.</summary>
        public int Undecided => _voters.Count - _yes.Count - _no.Count;

        /// <summary>The fraction <see cref="Quorum"/> is taken of.</summary>
        public double QuorumFraction { get; }

        /// <summary>Yes votes needed to pass, recomputed whenever a voter leaves.</summary>
        public int Quorum { get; private set; }

        /// <summary>The clock tick it started on.</summary>
        public long StartedAtTick { get; }

        /// <summary>How many ticks it runs before it lapses.</summary>
        public long TimeoutTicks { get; }

        /// <summary>Where <paramref name="player"/> stands on it.</summary>
        public VoteChoice ChoiceOf(ulong player)
        {
            if (_yes.Contains(player))
            {
                return VoteChoice.Yes;
            }

            if (_no.Contains(player))
            {
                return VoteChoice.No;
            }

            return _voters.Contains(player) ? VoteChoice.Undecided : VoteChoice.NotAVoter;
        }

        /// <summary>True once Yes has reached the quorum.</summary>
        public bool HasPassed => _yes.Count >= Quorum;

        /// <summary>True once Yes can no longer reach the quorum, even if every undecided voter says Yes.</summary>
        public bool CannotPass => _yes.Count + Undecided < Quorum;

        /// <summary>Ticks until it lapses at <paramref name="nowTick"/>; 0 once it has.</summary>
        public long TicksLeft(long nowTick) => Math.Max(0, StartedAtTick + TimeoutTicks - nowTick);

        /// <summary>True once its time has run out at <paramref name="nowTick"/>.</summary>
        public bool IsExpired(long nowTick) => TicksLeft(nowTick) == 0;

        /// <summary>Whole seconds until it lapses at <paramref name="nowTick"/>, rounded up (a countdown that shows 15 at the start and 0 once over).</summary>
        public int SecondsLeft(long nowTick, int tickRate)
        {
            if (tickRate < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "the tick rate must be at least 1");
            }

            return (int)((TicksLeft(nowTick) + tickRate - 1) / tickRate);
        }

        /// <summary>What every peer is told about it at <paramref name="nowTick"/>.</summary>
        public VoteStatus StatusAt(long nowTick, int tickRate) =>
            new VoteStatus(Sequence, Kind.Id, Title, Description, Number, Text, StartedBy, _yes.Count, _no.Count, _voters.Count, Quorum,
                SecondsLeft(nowTick, tickRate));

        /// <summary>How it ended, for every peer.</summary>
        public VoteResult ResultOf(VoteEnding ending) =>
            new VoteResult(Sequence, Kind.Id, Title, Number, Text, StartedBy, ending, _yes.Count, _no.Count, _voters.Count, Quorum);

        /// <inheritdoc />
        public override string ToString() => StatusAt(StartedAtTick, 1).ToString();

        internal bool Record(ulong voter, bool yes)
        {
            if (!_voters.Contains(voter))
            {
                return false;
            }

            VoteChoice before = ChoiceOf(voter);
            if (before == (yes ? VoteChoice.Yes : VoteChoice.No))
            {
                return false;
            }

            _yes.Remove(voter);
            _no.Remove(voter);
            (yes ? _yes : _no).Add(voter);
            return true;
        }

        internal bool Remove(ulong voter)
        {
            if (!_voters.Remove(voter))
            {
                return false;
            }

            _yes.Remove(voter);
            _no.Remove(voter);
            if (_voters.Count > 0)
            {
                Quorum = VoteRules.QuorumFor(_voters.Count, QuorumFraction);
            }

            return true;
        }
    }
}
