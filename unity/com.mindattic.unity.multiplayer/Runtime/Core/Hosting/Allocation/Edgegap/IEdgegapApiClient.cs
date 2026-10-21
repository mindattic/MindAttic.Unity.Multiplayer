using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The slice of Edgegap's v1 API <see cref="EdgegapMatchAllocator"/> needs. The only seam between
    /// the allocator and the network, so NUnit mocks it directly instead of faking HTTP responses.
    /// </summary>
    public interface IEdgegapApiClient
    {
        /// <summary>
        /// Creates or updates the app version that points at a GHCR image, so a later
        /// <see cref="DeployAsync"/> has something to deploy. Idempotent: calling it again with the
        /// same version name and image updates that version in place rather than creating a new one.
        /// </summary>
        Task EnsureAppVersionAsync(EdgegapAppVersionRequest request, CancellationToken cancellationToken = default);

        /// <summary>Requests a deployment of an app version, for one match.</summary>
        Task<EdgegapDeployment> DeployAsync(EdgegapDeployRequest request, CancellationToken cancellationToken = default);

        /// <summary>The deployment's current status and port mapping.</summary>
        Task<EdgegapDeployment> GetDeploymentAsync(string requestId, CancellationToken cancellationToken = default);

        /// <summary>Stops and tears down a deployment. Safe to call on one already gone.</summary>
        Task StopDeploymentAsync(string requestId, CancellationToken cancellationToken = default);
    }
}
