using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>Where one player stands on a vote.</summary>
    public enum VoteChoice
    {
        /// <summary>Not on the vote's roster (joined after it started, or the kind leaves them out), or no vote is running.</summary>
        NotAVoter = 0,

        /// <summary>On the roster and has voted neither way yet.</summary>
        Undecided = 1,

        /// <summary>Voted Yes.</summary>
        Yes = 2,

        /// <summary>Voted No.</summary>
        No = 3,
    }

    /// <summary>How a vote ended.</summary>
    public enum VoteEnding
    {
        /// <summary>Yes reached the quorum; the kind's <see cref="VoteKind.Passed"/> effect ran on the server.</summary>
        Passed = 0,

        /// <summary>Too many No votes (or departures) for the quorum to be reached any more.</summary>
        Rejected = 1,

        /// <summary>The time ran out before a decision.</summary>
        TimedOut = 2,

        /// <summary>Every voter left.</summary>
        Abandoned = 3,

        /// <summary>The server called it off (<see cref="VoteBox.Cancel"/>).</summary>
        Cancelled = 4,
    }

    /// <summary>Why a vote did not start (<see cref="VoteStartResult.Refusal"/>).</summary>
    public enum VoteRefusal
    {
        /// <summary>It started.</summary>
        None = 0,

        /// <summary>Another vote is running; there is one vote at a time.</summary>
        VoteInProgress = 1,

        /// <summary>No vote kind with that id is registered.</summary>
        UnknownKind = 2,

        /// <summary>The starter is not in the roster (not a connected player).</summary>
        NotAVoter = 3,

        /// <summary><see cref="VoteRules.MayStart"/> refused this player.</summary>
        NotAllowed = 4,

        /// <summary>The starter's last vote failed less than <see cref="VoteRules.CooldownSeconds"/> ago.</summary>
        CoolingDown = 5,

        /// <summary>The kind's <see cref="VoteKind.Check"/> refused the request (a level that does not exist, a player who already left).</summary>
        Invalid = 6,

        /// <summary>After the kind's <see cref="VoteKind.MayVote"/> filter nobody was left to vote.</summary>
        NoVoters = 7,
    }

    /// <summary>
    /// What a player asks for when they start a vote: the kind's id plus an optional number and text payload (a level
    /// number, a client id to kick, a map name). The server fills in <see cref="StartedBy"/>.
    /// </summary>
    public readonly struct VoteRequest
    {
        /// <summary>A request for a <paramref name="kind"/> vote by <paramref name="startedBy"/>, with an optional payload.</summary>
        public VoteRequest(string kind, ulong startedBy, long number = 0, string text = null)
        {
            Kind = kind ?? string.Empty;
            StartedBy = startedBy;
            Number = number;
            Text = text ?? string.Empty;
        }

        /// <summary>The vote kind's id (<see cref="VoteKind.Id"/>).</summary>
        public string Kind { get; }

        /// <summary>Client id of the player who asked.</summary>
        public ulong StartedBy { get; }

        /// <summary>The number payload (0 when the kind takes none).</summary>
        public long Number { get; }

        /// <summary>The text payload (empty when the kind takes none; never null).</summary>
        public string Text { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Kind + (Number != 0 ? " " + Number : string.Empty) + (Text.Length > 0 ? " \"" + Text + "\"" : string.Empty) + " by " + StartedBy;
    }

    /// <summary>What <see cref="VoteBox.Start"/> did.</summary>
    public readonly struct VoteStartResult
    {
        /// <summary>A result: <see cref="VoteRefusal.None"/> with the new vote's <paramref name="sequence"/>, or a refusal and its reason.</summary>
        public VoteStartResult(VoteRefusal refusal, string reason, int sequence)
        {
            Refusal = refusal;
            Reason = reason ?? string.Empty;
            Sequence = sequence;
        }

        /// <summary>True when the vote started (it may already have ended, when the starter's own Yes reached the quorum).</summary>
        public bool Started => Refusal == VoteRefusal.None;

        /// <summary>Why it did not start; <see cref="VoteRefusal.None"/> when it did.</summary>
        public VoteRefusal Refusal { get; }

        /// <summary>A sentence to show the starter (<see cref="VoteText.Refused"/>, or the kind's own reason); empty when it started.</summary>
        public string Reason { get; }

        /// <summary>The new vote's number when it started; 0 otherwise.</summary>
        public int Sequence { get; }

        /// <inheritdoc />
        public override string ToString() => Started ? "Started vote #" + Sequence : "Refused (" + Refusal + "): " + Reason;
    }

    /// <summary>
    /// What every peer knows about the vote in progress: what is being decided and the counts and countdown an overlay
    /// shows, without the voter lists (those stay on the server; each peer learns the ballots separately). Immutable.
    /// </summary>
    public sealed class VoteStatus : IEquatable<VoteStatus>
    {
        /// <summary>A status with every value given.</summary>
        public VoteStatus(int sequence, string kind, string title, string description, long number, string text, ulong startedBy,
            int yes, int no, int voters, int quorum, int secondsLeft)
        {
            Sequence = sequence;
            Kind = kind ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Number = number;
            Text = text ?? string.Empty;
            StartedBy = startedBy;
            Yes = yes;
            No = no;
            Voters = voters;
            Quorum = quorum;
            SecondsLeft = secondsLeft;
        }

        /// <summary>Which vote this is: 1 for the session's first, counting up, so a peer can tell a new vote from an update.</summary>
        public int Sequence { get; }

        /// <summary>The kind's id.</summary>
        public string Kind { get; }

        /// <summary>The question, as the kind words it ("Switch to Level 3?").</summary>
        public string Title { get; }

        /// <summary>An optional second line; empty when the kind has none.</summary>
        public string Description { get; }

        /// <summary>The request's number payload.</summary>
        public long Number { get; }

        /// <summary>The request's text payload (never null).</summary>
        public string Text { get; }

        /// <summary>Client id of the player who started it.</summary>
        public ulong StartedBy { get; }

        /// <summary>Yes votes so far.</summary>
        public int Yes { get; }

        /// <summary>No votes so far.</summary>
        public int No { get; }

        /// <summary>How many players may vote (the roster when it started, less anyone who has left since).</summary>
        public int Voters { get; }

        /// <summary>Yes votes needed to pass (<see cref="VoteRules.QuorumFor(int, double)"/>).</summary>
        public int Quorum { get; }

        /// <summary>Whole seconds until it lapses, rounded up.</summary>
        public int SecondsLeft { get; }

        /// <summary>Voters who have voted neither way.</summary>
        public int Undecided => Voters - Yes - No;

        /// <inheritdoc />
        public bool Equals(VoteStatus other) =>
            other != null && Sequence == other.Sequence && Kind == other.Kind && Title == other.Title && Description == other.Description &&
            Number == other.Number && Text == other.Text && StartedBy == other.StartedBy && Yes == other.Yes && No == other.No &&
            Voters == other.Voters && Quorum == other.Quorum && SecondsLeft == other.SecondsLeft;

        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as VoteStatus);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int h = Sequence;
                h = h * 31 + Kind.GetHashCode();
                h = h * 31 + Title.GetHashCode();
                h = h * 31 + Number.GetHashCode();
                h = h * 31 + Yes;
                h = h * 31 + No;
                h = h * 31 + Voters;
                h = h * 31 + Quorum;
                return h * 31 + SecondsLeft;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            "Vote #" + Sequence + " " + Kind + " \"" + Title + "\": " + Yes + " yes, " + No + " no of " + Voters + " (needs " + Quorum + "), " + SecondsLeft + "s left";
    }

    /// <summary>How a vote ended, as every peer hears it. Immutable.</summary>
    public sealed class VoteResult
    {
        /// <summary>A result with every value given.</summary>
        public VoteResult(int sequence, string kind, string title, long number, string text, ulong startedBy, VoteEnding ending,
            int yes, int no, int voters, int quorum)
        {
            Sequence = sequence;
            Kind = kind ?? string.Empty;
            Title = title ?? string.Empty;
            Number = number;
            Text = text ?? string.Empty;
            StartedBy = startedBy;
            Ending = ending;
            Yes = yes;
            No = no;
            Voters = voters;
            Quorum = quorum;
        }

        /// <summary>Which vote it was (<see cref="VoteStatus.Sequence"/>).</summary>
        public int Sequence { get; }

        /// <summary>The kind's id.</summary>
        public string Kind { get; }

        /// <summary>The question it asked.</summary>
        public string Title { get; }

        /// <summary>The request's number payload.</summary>
        public long Number { get; }

        /// <summary>The request's text payload (never null).</summary>
        public string Text { get; }

        /// <summary>Client id of the player who started it.</summary>
        public ulong StartedBy { get; }

        /// <summary>How it ended.</summary>
        public VoteEnding Ending { get; }

        /// <summary>True when it passed and its effect ran.</summary>
        public bool Passed => Ending == VoteEnding.Passed;

        /// <summary>Yes votes when it ended.</summary>
        public int Yes { get; }

        /// <summary>No votes when it ended.</summary>
        public int No { get; }

        /// <summary>Voters when it ended.</summary>
        public int Voters { get; }

        /// <summary>The quorum when it ended.</summary>
        public int Quorum { get; }

        /// <inheritdoc />
        public override string ToString() => "Vote #" + Sequence + " " + Kind + " \"" + Title + "\" " + Ending + " (" + Yes + " yes, " + No + " no of " + Voters + ")";
    }
}
