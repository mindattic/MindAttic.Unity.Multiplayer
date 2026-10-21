using System;
using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>Answers whether a player token is banned. Consulted by <see cref="ConnectionApprovalValidator"/>.</summary>
    public interface IBanList
    {
        /// <summary>True when the player holding <paramref name="playerToken"/> may not join.</summary>
        bool IsBanned(string playerToken);
    }

    /// <summary>A thread-safe in-memory <see cref="IBanList"/>; tokens compare ordinally.</summary>
    public sealed class InMemoryBanList : IBanList
    {
        private readonly HashSet<string> tokens = new HashSet<string>(StringComparer.Ordinal);
        private readonly object gate = new object();

        /// <summary>An empty list.</summary>
        public InMemoryBanList()
        {
        }

        /// <summary>A list holding the given tokens.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="playerTokens"/> is null.</exception>
        public InMemoryBanList(IEnumerable<string> playerTokens)
        {
            if (playerTokens == null)
            {
                throw new ArgumentNullException(nameof(playerTokens));
            }

            foreach (string token in playerTokens)
            {
                Ban(token);
            }
        }

        /// <summary>The number of banned tokens.</summary>
        public int Count
        {
            get
            {
                lock (gate)
                {
                    return tokens.Count;
                }
            }
        }

        /// <summary>Bans a token; true if it was not already banned. Null or empty tokens are ignored.</summary>
        public bool Ban(string playerToken)
        {
            if (string.IsNullOrEmpty(playerToken))
            {
                return false;
            }

            lock (gate)
            {
                return tokens.Add(playerToken);
            }
        }

        /// <summary>Lifts a ban; true if the token was banned.</summary>
        public bool Unban(string playerToken)
        {
            if (string.IsNullOrEmpty(playerToken))
            {
                return false;
            }

            lock (gate)
            {
                return tokens.Remove(playerToken);
            }
        }

        /// <inheritdoc />
        public bool IsBanned(string playerToken)
        {
            if (string.IsNullOrEmpty(playerToken))
            {
                return false;
            }

            lock (gate)
            {
                return tokens.Contains(playerToken);
            }
        }
    }
}
