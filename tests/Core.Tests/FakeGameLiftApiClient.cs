using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// A scriptable <see cref="IGameLiftApiClient"/>: <see cref="GameLiftMatchAllocatorTests"/> drives
    /// <see cref="GameLiftMatchAllocator"/> against this instead of real HTTP.
    /// </summary>
    internal sealed class FakeGameLiftApiClient : IGameLiftApiClient
    {
        public readonly List<string> Calls = new List<string>();

        public GameLiftCreateGameSessionRequest LastCreateRequest;
        public string LastPlayerId;
        public string LastPlayerData;

        public string GameSessionId = "gsess-1";
        public string IpAddress = "203.0.113.60";
        public int Port = 7777;

        /// <summary>How many <see cref="DescribeGameSessionAsync"/> calls it takes before the session reports ACTIVE.</summary>
        public int ActiveAfterPolls = 1;

        public bool FailCreateWithAllocationException;
        public bool FailCreateWithGenericException;
        public string TerminalStatus;

        public string CreatedPlayerSessionId = "psess-1";
        public bool FailCreatePlayerSessionWithGenericException;

        private int polls;

        public Task<GameLiftGameSession> CreateGameSessionAsync(GameLiftCreateGameSessionRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("CreateGameSession");
            LastCreateRequest = request;
            if (FailCreateWithAllocationException)
            {
                throw new MatchAllocationException("GameLift says no.");
            }

            if (FailCreateWithGenericException)
            {
                throw new InvalidOperationException("boom");
            }

            polls = 0;
            return Task.FromResult(new GameLiftGameSession(GameSessionId, "ACTIVATING", null, 0, null));
        }

        public Task<GameLiftGameSession> DescribeGameSessionAsync(string gameSessionId, CancellationToken cancellationToken = default)
        {
            Calls.Add("DescribeGameSession");
            polls++;
            if (TerminalStatus != null)
            {
                return Task.FromResult(new GameLiftGameSession(gameSessionId, TerminalStatus, null, 0, null));
            }

            bool active = polls >= ActiveAfterPolls;
            return Task.FromResult(active
                ? new GameLiftGameSession(gameSessionId, "ACTIVE", IpAddress, Port, null)
                : new GameLiftGameSession(gameSessionId, "ACTIVATING", null, 0, null));
        }

        public Task<GameLiftPlayerSession> CreatePlayerSessionAsync(string gameSessionId, string playerId, string playerData, CancellationToken cancellationToken = default)
        {
            Calls.Add("CreatePlayerSession");
            LastPlayerId = playerId;
            LastPlayerData = playerData;
            if (FailCreatePlayerSessionWithGenericException)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(new GameLiftPlayerSession(CreatedPlayerSessionId, "RESERVED"));
        }
    }
}
