using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The words a vote overlay shows, engine-free: the tally line, where the local player stands, why a start was
    /// refused and how a vote ended. The one home of that wording, so a game's own UI can say the same thing.
    /// </summary>
    public static class VoteText
    {
        /// <summary>"Yes 1 · No 0 · needs 2 of 3 · 12 s".</summary>
        public static string Tally(VoteStatus status)
        {
            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }

            return "Yes " + status.Yes + " · No " + status.No + " · needs " + status.Quorum + " of " + status.Voters + " · " + status.SecondsLeft + " s";
        }

        /// <summary>How the local player stands: "Your vote", "You voted Yes", "You voted No", or why they can't vote.</summary>
        public static string Standing(VoteChoice choice)
        {
            switch (choice)
            {
                case VoteChoice.Yes: return "You voted Yes";
                case VoteChoice.No: return "You voted No";
                case VoteChoice.Undecided: return "Your vote";
                default: return "You are not voting on this one";
            }
        }

        /// <summary>True while the local player may still press Yes or No: once they have voted the buttons are done.</summary>
        public static bool CanVote(VoteChoice choice) => choice == VoteChoice.Undecided;

        /// <summary>A toast for how a vote ended: "Vote passed: Kick Bob", "Vote failed: Reset the level".</summary>
        public static string Ended(VoteResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return Ended(result.Title, result.Ending);
        }

        /// <summary>A toast for a vote titled <paramref name="title"/> that ended <paramref name="ending"/>.</summary>
        public static string Ended(string title, VoteEnding ending)
        {
            string what = (title ?? string.Empty).Trim().TrimEnd('?').Trim();
            string head;
            switch (ending)
            {
                case VoteEnding.Passed: head = "Vote passed"; break;
                case VoteEnding.Rejected: head = "Vote failed"; break;
                case VoteEnding.TimedOut: head = "Vote timed out"; break;
                case VoteEnding.Abandoned: head = "Vote abandoned"; break;
                default: head = "Vote cancelled"; break;
            }

            return what.Length == 0 ? head : head + ": " + what;
        }

        /// <summary>The sentence a refused starter is told, when the kind gave no reason of its own.</summary>
        public static string Refused(VoteRefusal refusal, int cooldownSecondsLeft = 0)
        {
            switch (refusal)
            {
                case VoteRefusal.None: return string.Empty;
                case VoteRefusal.VoteInProgress: return "A vote is already running.";
                case VoteRefusal.UnknownKind: return "That vote isn't available in this game.";
                case VoteRefusal.NotAVoter: return "Only players in the game can start a vote.";
                case VoteRefusal.NotAllowed: return "You can't start a vote in this game.";
                case VoteRefusal.CoolingDown:
                    return cooldownSecondsLeft > 0
                        ? "Wait " + cooldownSecondsLeft + " s before starting another vote."
                        : "Wait a moment before starting another vote.";
                case VoteRefusal.NoVoters: return "Nobody could vote on that.";
                default: return "That vote can't start.";
            }
        }
    }
}
