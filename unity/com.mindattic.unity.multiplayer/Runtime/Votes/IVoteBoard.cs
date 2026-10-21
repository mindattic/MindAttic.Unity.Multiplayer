using System;
using System.Collections.Generic;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer
{
    /// <summary>
    /// A <see cref="VoteBoard"/> as a UI sees it, with no NGO type in sight (BIBLE UM-LAW-2): what is being voted on, where
    /// this peer stands, and the calls a player makes. The drop-in front end's vote overlay binds to this, through
    /// <see cref="VoteBoards"/>.
    /// </summary>
    public interface IVoteBoard
    {
        /// <summary>The session the board runs on (null once it has gone).</summary>
        NetSession Session { get; }

        /// <summary>True while the board is spawned on this peer.</summary>
        bool IsSpawned { get; }

        /// <summary>The vote in progress, or null when none runs.</summary>
        VoteStatus Status { get; }

        /// <summary>True while a vote runs.</summary>
        bool IsVoteOpen { get; }

        /// <summary>Where this peer's own player stands on the running vote.</summary>
        VoteChoice LocalChoice { get; }

        /// <summary>Where player <paramref name="clientId"/> stands on the running vote.</summary>
        VoteChoice ChoiceOf(ulong clientId);

        /// <summary>Raised when <see cref="Status"/> or a ballot changes.</summary>
        event Action<VoteStatus> VoteChanged;

        /// <summary>Raised once per vote when it ends.</summary>
        event Action<VoteResult> VoteEnded;

        /// <summary>Raised on the peer whose start the server refused.</summary>
        event Action<VoteRefusal, string> VoteRefused;

        /// <summary>Asks the server to start a vote of <paramref name="kind"/>.</summary>
        void StartVote(string kind, long number = 0, string text = null);

        /// <summary>Asks the server to start the built-in kick vote against <paramref name="clientId"/>.</summary>
        void StartKickVote(ulong clientId);

        /// <summary>Votes Yes or No on the running vote.</summary>
        void CastVote(bool yes);
    }

    /// <summary>
    /// Every <see cref="VoteBoard"/> spawned in this process, as <see cref="IVoteBoard"/>s, and the events that announce
    /// them: what code that must not see NGO types (the vote overlay) binds to. Game code can use
    /// <see cref="VoteBoard.For"/> and <see cref="VoteBoard.Spawned"/> instead.
    /// </summary>
    public static class VoteBoards
    {
        private static readonly List<IVoteBoard> s_All = new List<IVoteBoard>();

        /// <summary>Raised on every peer when a board has spawned there.</summary>
        public static event Action<IVoteBoard> Spawned;

        /// <summary>Raised on every peer when a board despawns or is destroyed there.</summary>
        public static event Action<IVoteBoard> Despawned;

        /// <summary>Every board spawned in this process (one per session; several in a test with many peers).</summary>
        public static IReadOnlyList<IVoteBoard> All => s_All;

        /// <summary>The board spawned on <paramref name="session"/>'s peer, or null.</summary>
        public static IVoteBoard For(NetSession session)
        {
            foreach (IVoteBoard board in s_All)
            {
                if (board != null && session != null && board.Session == session)
                {
                    return board;
                }
            }

            return null;
        }

        internal static void Add(IVoteBoard board)
        {
            if (s_All.Contains(board))
            {
                return;
            }

            s_All.Add(board);
            Spawned?.Invoke(board);
        }

        internal static void Remove(IVoteBoard board)
        {
            if (s_All.Remove(board))
            {
                Despawned?.Invoke(board);
            }
        }
    }
}
