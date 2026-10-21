using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The slice of Gameye's API <see cref="GameyeMatchAllocator"/> needs. The only seam between the
    /// allocator and the network, so NUnit mocks it directly instead of faking HTTP responses.
    /// </summary>
    public interface IGameyeApiClient
    {
        /// <summary>
        /// Registers (or updates) the image Gameye pulls sessions from. Idempotent: call it again
        /// whenever mp-host pushes a new image under the same name.
        /// </summary>
        Task RegisterImageAsync(GameyeImageRegistration registration, CancellationToken cancellationToken = default);

        /// <summary>Starts a session for one match.</summary>
        Task<GameyeSession> StartSessionAsync(GameyeStartSessionRequest request, CancellationToken cancellationToken = default);

        /// <summary>The session's current status and port mapping.</summary>
        Task<GameyeSession> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>Stops a session. Safe to call on one already gone.</summary>
        Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    }
}
