using System;
using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// What a matchmaker asks an <see cref="IMatchAllocator"/> for: a fresh server for one match,
    /// started on demand and torn down with <see cref="IMatchAllocator.ReleaseAsync"/> once the match
    /// ends. Unlike <c>IHostingProfile</c>'s provision-once-and-keep-running servers, a new
    /// <see cref="MatchRequest"/> is made for every match.
    /// </summary>
    public sealed class MatchRequest
    {
        private static readonly Dictionary<string, string> EmptyMetadata = new Dictionary<string, string>();

        /// <summary>Creates a request for one match.</summary>
        /// <exception cref="ArgumentException"><paramref name="matchId"/> is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPlayers"/> is less than 1.</exception>
        public MatchRequest(string matchId, int maxPlayers, string region = null, IReadOnlyDictionary<string, string> metadata = null)
        {
            if (string.IsNullOrWhiteSpace(matchId))
            {
                throw new ArgumentException("A match request needs a match id.", nameof(matchId));
            }

            if (maxPlayers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), maxPlayers, "maxPlayers must be at least 1.");
            }

            MatchId = matchId.Trim();
            MaxPlayers = maxPlayers;
            Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
            Metadata = metadata ?? EmptyMetadata;
        }

        /// <summary>A caller-chosen id for this match; providers that support it tag the deployment/session with it for their own logs.</summary>
        public string MatchId { get; }

        /// <summary>How many players the server must have room for.</summary>
        public int MaxPlayers { get; }

        /// <summary>A preferred region/location code, or null to let the provider pick (usually the one closest to its routing data).</summary>
        public string Region { get; }

        /// <summary>Extra provider-specific hints (an adapter documents the keys it reads); never null.</summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return "match " + MatchId + " (" + MaxPlayers + " players" + (Region == null ? string.Empty : ", region " + Region) + ")";
        }
    }
}
