using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The rules every vote on a <see cref="VoteBox"/> follows, unless its <see cref="VoteKind"/> overrides the quorum or
    /// the time: who may start one, how many Yes votes pass it, how long it runs and how long a player whose vote failed
    /// waits before starting another. Defaults: anyone, two thirds rounded up, 15 seconds, no cooldown.
    /// </summary>
    public sealed class VoteRules
    {
        /// <summary>Two thirds: the default quorum fraction.</summary>
        public const double TwoThirds = 2.0 / 3.0;

        /// <summary>The default time a vote runs before it lapses, in seconds.</summary>
        public const double DefaultTimeoutSeconds = 15;

        /// <summary>
        /// The fraction of the voters whose Yes passes a vote, rounded up (<see cref="QuorumFor(int, double)"/>): 2/3 by
        /// default (2 of 3, 3 of 4, 4 of 5), 0.5 for a simple half, 1 for everyone. Above 0, at most 1.
        /// </summary>
        public double QuorumFraction { get; set; } = TwoThirds;

        /// <summary>Seconds before an undecided vote lapses (<see cref="VoteEnding.TimedOut"/>). Above 0.</summary>
        public double TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

        /// <summary>
        /// Seconds a player must wait after a vote they started fails (rejected, timed out, abandoned) before they may start
        /// another; 0 (the default) for no wait. Other players are not held up. A vote that passes never cools anyone down.
        /// </summary>
        public double CooldownSeconds { get; set; }

        /// <summary>True (the default) when starting a vote counts as the starter's Yes.</summary>
        public bool StarterVotesYes { get; set; } = true;

        /// <summary>
        /// Who may start a vote, by client id; null (the default) lets any player in the roster. For example only the host:
        /// <c>rules.MayStart = id =&gt; id == NetSender.ServerClientId</c>.
        /// </summary>
        public Func<ulong, bool> MayStart { get; set; }

        /// <summary>A copy, so one board's changes never reach another's.</summary>
        public VoteRules Clone() => new VoteRules
        {
            QuorumFraction = QuorumFraction,
            TimeoutSeconds = TimeoutSeconds,
            CooldownSeconds = CooldownSeconds,
            StarterVotesYes = StarterVotesYes,
            MayStart = MayStart,
        };

        /// <summary>Throws when a value is out of range.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The fraction is not in (0, 1], the timeout is not positive or the cooldown is negative.</exception>
        public void Validate()
        {
            ValidateFraction(QuorumFraction, nameof(QuorumFraction));
            ValidateTimeout(TimeoutSeconds, nameof(TimeoutSeconds));
            if (double.IsNaN(CooldownSeconds) || CooldownSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(CooldownSeconds), CooldownSeconds, "the cooldown must be 0 or more seconds");
            }
        }

        /// <summary>Yes votes needed from <paramref name="voters"/> voters at this board's <see cref="QuorumFraction"/>.</summary>
        public int QuorumFor(int voters) => QuorumFor(voters, QuorumFraction);

        /// <summary>
        /// Yes votes needed from <paramref name="voters"/> voters: <c>ceil(voters x fraction)</c>, at least 1 and at most
        /// <paramref name="voters"/>. At the default 2/3: 1 of 1, 2 of 2, 2 of 3, 3 of 4, 4 of 5, 4 of 6.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="voters"/> is below one, or the fraction is not in (0, 1].</exception>
        public static int QuorumFor(int voters, double fraction)
        {
            if (voters < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(voters), voters, "a vote needs at least one voter");
            }

            ValidateFraction(fraction, nameof(fraction));

            // The small allowance keeps 3 x (2/3) at 2, not 3 (the double is a hair over 2).
            int quorum = (int)Math.Ceiling(voters * fraction - 1e-9);
            return Math.Min(voters, Math.Max(1, quorum));
        }

        internal static void ValidateFraction(double fraction, string name)
        {
            if (double.IsNaN(fraction) || fraction <= 0 || fraction > 1)
            {
                throw new ArgumentOutOfRangeException(name, fraction, "the quorum fraction must be above 0 and at most 1");
            }
        }

        internal static void ValidateTimeout(double seconds, string name)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                throw new ArgumentOutOfRangeException(name, seconds, "a vote must run for more than 0 seconds");
            }
        }
    }
}
