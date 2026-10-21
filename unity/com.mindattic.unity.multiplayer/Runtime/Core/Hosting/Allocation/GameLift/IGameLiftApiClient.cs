using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The slice of the GameLift service API <see cref="GameLiftMatchAllocator"/> needs: start a game
    /// session on an already-created fleet (mp-host's job -- build upload + fleet create, see
    /// docs/articles/hosting/gamelift.md), wait for it to activate, and reserve a player session once
    /// a seat is needed. The only seam between the allocator and the network, so NUnit mocks it
    /// directly instead of faking signed HTTP/JSON.
    /// </summary>
    public interface IGameLiftApiClient
    {
        /// <summary>Starts a game session on a fleet (or through an alias), for one match.</summary>
        Task<GameLiftGameSession> CreateGameSessionAsync(GameLiftCreateGameSessionRequest request, CancellationToken cancellationToken = default);

        /// <summary>The session's current status and connection info.</summary>
        Task<GameLiftGameSession> DescribeGameSessionAsync(string gameSessionId, CancellationToken cancellationToken = default);

        /// <summary>Reserves a seat on a session for one player. <paramref name="playerData"/> is an optional, arbitrary string GameLift stores with the reservation.</summary>
        Task<GameLiftPlayerSession> CreatePlayerSessionAsync(string gameSessionId, string playerId, string playerData, CancellationToken cancellationToken = default);
    }
}
