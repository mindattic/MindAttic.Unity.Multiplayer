using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// One kind of vote a game offers (reset the level, change the map, kick a player...): its id, how its question
    /// reads, which requests are valid, who votes on it and what happens on the server when it passes. Register it on
    /// the server's <see cref="VoteBox"/> (or a <c>VoteBoard</c>); clients start one by its <see cref="Id"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// new VoteKind("level", r =&gt; "Switch to Level " + r.Number + "?")
    /// {
    ///     Check  = r =&gt; levels.Exists((int)r.Number) ? null : "There is no level " + r.Number + ".",
    ///     Passed = v =&gt; levels.Load((int)v.Number),
    /// };
    /// </code>
    /// </example>
    public sealed class VoteKind
    {
        /// <summary>A kind whose question is always <paramref name="title"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="title"/> is empty.</exception>
        public VoteKind(string id, string title)
            : this(id, ConstantTitle(title))
        {
        }

        /// <summary>A kind whose question is worded from the request (<paramref name="title"/>), e.g. from its number payload.</summary>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
        public VoteKind(string id, Func<VoteRequest, string> title)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A vote kind needs an id.", nameof(id));
            }

            Id = id;
            Title = title ?? throw new ArgumentNullException(nameof(title));
        }

        /// <summary>What clients pass to start one ("reset", "level", "kick").</summary>
        public string Id { get; }

        /// <summary>The question every peer sees ("Kick Bob?"), worded on the server when the vote starts.</summary>
        public Func<VoteRequest, string> Title { get; }

        /// <summary>An optional second line under the question; null for none.</summary>
        public Func<VoteRequest, string> Description { get; set; }

        /// <summary>
        /// Server side, before the vote starts: null or empty when the request is valid, else the sentence the starter is
        /// told (<see cref="VoteRefusal.Invalid"/>). Null (the default) accepts every request.
        /// </summary>
        public Func<VoteRequest, string> Check { get; set; }

        /// <summary>
        /// Which roster members vote on it, by client id; null (the default) lets all of them. A kick vote leaves its target
        /// out. The quorum counts only the voters left.
        /// </summary>
        public Func<VoteRequest, ulong, bool> MayVote { get; set; }

        /// <summary>Server side: what happens when it passes. Runs before every peer is told it passed.</summary>
        public Action<Vote> Passed { get; set; }

        /// <summary>This kind's quorum fraction; null (the default) uses the box's <see cref="VoteRules.QuorumFraction"/>.</summary>
        public double? QuorumFraction { get; set; }

        /// <summary>This kind's time limit in seconds; null (the default) uses the box's <see cref="VoteRules.TimeoutSeconds"/>.</summary>
        public double? TimeoutSeconds { get; set; }

        /// <inheritdoc />
        public override string ToString() => "VoteKind " + Id;

        private static Func<VoteRequest, string> ConstantTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("A vote kind needs a title.", nameof(title));
            }

            return _ => title;
        }
    }
}
