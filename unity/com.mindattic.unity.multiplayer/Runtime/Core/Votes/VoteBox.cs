using System;
using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The server's vote rules, engine-free: one vote at a time over a roster of client ids, worded and checked by the
    /// game's registered <see cref="VoteKind"/>s and decided by <see cref="VoteRules"/>. The roster when a vote starts is
    /// its voter list (a later joiner does not vote), the starter counts as Yes, and it passes as soon as Yes reaches the
    /// quorum: the kind's <see cref="VoteKind.Passed"/> effect runs, then <see cref="Ended"/> is raised. It is rejected as
    /// soon as the quorum can no longer be reached, and lapses after its time on the <see cref="ITickClock"/>. A voter who
    /// leaves (<see cref="RemoveVoter"/>) is dropped and the quorum recomputed from who is left, which can pass or reject
    /// it at once.
    /// </summary>
    /// <remarks>
    /// The networked <c>VoteBoard</c> runs one on the server; a game can also drive one directly (a bot, a test, a
    /// non-networked mode). Not thread-safe: call it from the clock's thread (Unity's main thread).
    /// </remarks>
    public sealed class VoteBox : IDisposable
    {
        private readonly ITickClock _clock;
        private readonly Dictionary<string, VoteKind> _kinds = new Dictionary<string, VoteKind>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, long> _coolUntil = new Dictionary<ulong, long>();
        private Vote _active;
        private int _lastSecondsLeft = -1;
        private bool _disposed;

        /// <summary>A box timed by <paramref name="clock"/> (it subscribes to <see cref="ITickClock.Ticked"/> until disposed).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="rules"/> has a value out of range.</exception>
        public VoteBox(ITickClock clock, VoteRules rules = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Rules = rules ?? new VoteRules();
            Rules.Validate();
            _clock.Ticked += OnTicked;
        }

        /// <summary>
        /// Raised after every change to the running vote (start, ballot, departure, each second of the countdown) with its
        /// status, and with null once it ends.
        /// </summary>
        public event Action<VoteStatus> Changed;

        /// <summary>Raised when a vote ends, after a passing vote's effect has run.</summary>
        public event Action<VoteResult> Ended;

        /// <summary>The rules every vote follows (a kind may override the quorum and time). Changes apply to the next vote.</summary>
        public VoteRules Rules { get; }

        /// <summary>The clock votes are timed on.</summary>
        public ITickClock Clock => _clock;

        /// <summary>Every registered kind.</summary>
        public IReadOnlyCollection<VoteKind> Kinds => _kinds.Values;

        /// <summary>The running vote, or null when there is none.</summary>
        public Vote Current => _active;

        /// <summary>True while a vote is running.</summary>
        public bool IsActive => _active != null;

        /// <summary>The running vote's number (1 for the first, counting up); the last vote's once it has ended; 0 before any.</summary>
        public int Sequence { get; private set; }

        /// <summary>The running vote's status now, or null when there is none.</summary>
        public VoteStatus Status => _active == null ? null : _active.StatusAt(_clock.CurrentTick, _clock.TickRate);

        /// <summary>Where <paramref name="player"/> stands on the running vote; <see cref="VoteChoice.NotAVoter"/> when none runs.</summary>
        public VoteChoice ChoiceOf(ulong player) => _active == null ? VoteChoice.NotAVoter : _active.ChoiceOf(player);

        /// <summary>Adds a kind, or replaces the one with the same id.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The kind's own quorum fraction or time is out of range.</exception>
        public VoteBox Register(VoteKind kind)
        {
            if (kind == null)
            {
                throw new ArgumentNullException(nameof(kind));
            }

            if (kind.QuorumFraction.HasValue)
            {
                VoteRules.ValidateFraction(kind.QuorumFraction.Value, nameof(VoteKind.QuorumFraction));
            }

            if (kind.TimeoutSeconds.HasValue)
            {
                VoteRules.ValidateTimeout(kind.TimeoutSeconds.Value, nameof(VoteKind.TimeoutSeconds));
            }

            _kinds[kind.Id] = kind;
            return this;
        }

        /// <summary>Removes the kind with <paramref name="id"/>. A running vote of that kind carries on.</summary>
        public bool Unregister(string id) => id != null && _kinds.Remove(id);

        /// <summary>The kind registered as <paramref name="id"/>.</summary>
        public bool TryGetKind(string id, out VoteKind kind)
        {
            kind = null;
            return id != null && _kinds.TryGetValue(id, out kind);
        }

        /// <summary>Whole seconds <paramref name="starter"/> must still wait before starting a vote; 0 when they may.</summary>
        public int CooldownSecondsLeft(ulong starter)
        {
            long until;
            if (!_coolUntil.TryGetValue(starter, out until))
            {
                return 0;
            }

            long left = until - _clock.CurrentTick;
            return left <= 0 ? 0 : (int)((left + _clock.TickRate - 1) / _clock.TickRate);
        }

        /// <summary>
        /// Starts the vote <paramref name="request"/> asks for, with <paramref name="roster"/> (the connected players' client
        /// ids) as its voters. In order it is refused when a vote is running, the kind is unknown, the starter is not in the
        /// roster, <see cref="VoteRules.MayStart"/> says no, the starter is cooling down, the kind's
        /// <see cref="VoteKind.Check"/> refuses it, or nobody is left to vote. A one-voter room passes at once.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The box was disposed.</exception>
        public VoteStartResult Start(VoteRequest request, IEnumerable<ulong> roster)
        {
            ThrowIfDisposed();
            if (_active != null)
            {
                return Refuse(VoteRefusal.VoteInProgress);
            }

            VoteKind kind;
            if (!TryGetKind(request.Kind, out kind))
            {
                return Refuse(VoteRefusal.UnknownKind);
            }

            var everyone = new List<ulong>();
            if (roster != null)
            {
                foreach (ulong id in roster)
                {
                    if (!everyone.Contains(id))
                    {
                        everyone.Add(id);
                    }
                }
            }

            if (!everyone.Contains(request.StartedBy))
            {
                return Refuse(VoteRefusal.NotAVoter);
            }

            if (Rules.MayStart != null && !Rules.MayStart(request.StartedBy))
            {
                return Refuse(VoteRefusal.NotAllowed);
            }

            int cooling = CooldownSecondsLeft(request.StartedBy);
            if (cooling > 0)
            {
                return new VoteStartResult(VoteRefusal.CoolingDown, VoteText.Refused(VoteRefusal.CoolingDown, cooling), 0);
            }

            _coolUntil.Remove(request.StartedBy);
            string invalid = kind.Check != null ? kind.Check(request) : null;
            if (!string.IsNullOrWhiteSpace(invalid))
            {
                return new VoteStartResult(VoteRefusal.Invalid, invalid, 0);
            }

            var voters = new List<ulong>();
            foreach (ulong id in everyone)
            {
                if (kind.MayVote == null || kind.MayVote(request, id))
                {
                    voters.Add(id);
                }
            }

            if (voters.Count == 0)
            {
                return Refuse(VoteRefusal.NoVoters);
            }

            double timeout = kind.TimeoutSeconds ?? Rules.TimeoutSeconds;
            long timeoutTicks = Math.Max(1, (long)Math.Ceiling(timeout * _clock.TickRate - 1e-9));
            string title = kind.Title(request);
            string description = kind.Description != null ? kind.Description(request) : null;

            Sequence++;
            _active = new Vote(Sequence, kind, request, title, description, voters, kind.QuorumFraction ?? Rules.QuorumFraction,
                _clock.CurrentTick, timeoutTicks);
            int sequence = Sequence;
            if (Rules.StarterVotesYes)
            {
                _active.Record(request.StartedBy, true);
            }

            RaiseChanged();
            Resolve();
            return new VoteStartResult(VoteRefusal.None, string.Empty, sequence);
        }

        /// <summary>
        /// Records <paramref name="voter"/>'s Yes or No. Voting again replaces the earlier choice; a player who is not a
        /// voter on the running vote (or no vote running) changes nothing.
        /// </summary>
        /// <returns>True when the ballot counted.</returns>
        public bool Cast(ulong voter, bool yes)
        {
            if (_active == null || !_active.Record(voter, yes))
            {
                return false;
            }

            RaiseChanged();
            Resolve();
            return true;
        }

        /// <summary>
        /// Drops <paramref name="player"/> (who left) from the running vote and recomputes the quorum from the voters left,
        /// which can pass or reject it at once. A vote whose last voter leaves is <see cref="VoteEnding.Abandoned"/>.
        /// </summary>
        public void RemoveVoter(ulong player)
        {
            if (_active == null || !_active.Remove(player))
            {
                return;
            }

            if (_active.Voters.Count == 0)
            {
                End(VoteEnding.Abandoned);
                return;
            }

            RaiseChanged();
            Resolve();
        }

        /// <summary>Calls off the running vote with no effect (<see cref="VoteEnding.Cancelled"/>).</summary>
        /// <returns>False when none was running.</returns>
        public bool Cancel()
        {
            if (_active == null)
            {
                return false;
            }

            End(VoteEnding.Cancelled);
            return true;
        }

        /// <summary>Stops listening to the clock. A running vote is dropped without being reported.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _clock.Ticked -= OnTicked;
            _active = null;
        }

        private void OnTicked(long tick)
        {
            if (_active == null)
            {
                return;
            }

            if (_active.IsExpired(tick))
            {
                End(VoteEnding.TimedOut);
                return;
            }

            if (_active.SecondsLeft(tick, _clock.TickRate) != _lastSecondsLeft)
            {
                RaiseChanged();
            }
        }

        private void Resolve()
        {
            if (_active == null)
            {
                return;
            }

            if (_active.HasPassed)
            {
                Vote passing = _active;
                try
                {
                    passing.Kind.Passed?.Invoke(passing);
                }
                finally
                {
                    if (_active == passing)
                    {
                        End(VoteEnding.Passed);
                    }
                }
            }
            else if (_active.CannotPass)
            {
                End(VoteEnding.Rejected);
            }
        }

        private void End(VoteEnding ending)
        {
            Vote ended = _active;
            _active = null;
            _lastSecondsLeft = -1;
            if (ending != VoteEnding.Passed && ending != VoteEnding.Cancelled && Rules.CooldownSeconds > 0)
            {
                _coolUntil[ended.StartedBy] = _clock.CurrentTick + (long)Math.Ceiling(Rules.CooldownSeconds * _clock.TickRate - 1e-9);
            }

            Changed?.Invoke(null);
            Ended?.Invoke(ended.ResultOf(ending));
        }

        private void RaiseChanged()
        {
            VoteStatus status = Status;
            _lastSecondsLeft = status.SecondsLeft;
            Changed?.Invoke(status);
        }

        private static VoteStartResult Refuse(VoteRefusal refusal) => new VoteStartResult(refusal, VoteText.Refused(refusal), 0);

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VoteBox));
            }
        }
    }
}
