using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// <see cref="IMatchAllocator"/> for Edgegap: deploys one container per match from an app version
    /// already pointed at the pushed GHCR image, waits for Edgegap to report it running with its ports
    /// mapped, and publishes the result through an <see cref="IJoinCodeService"/> so a player gets a
    /// short code instead of a raw address.
    /// </summary>
    public sealed class EdgegapMatchAllocator : IMatchAllocator
    {
        private readonly IEdgegapApiClient api;
        private readonly IJoinCodeService joinCodes;

        /// <summary>
        /// Deploys <paramref name="versionName"/> of Edgegap app <paramref name="appName"/>
        /// through <paramref name="api"/>, publishing join codes through <paramref name="joinCodes"/>.
        /// <paramref name="gamePortName"/> is the <see cref="EdgegapPortSpec.Name"/> the game port was
        /// declared under when the app version was created (see <see cref="EnsureAppVersionAsync"/>).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="api"/> or <paramref name="joinCodes"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="appName"/>, <paramref name="versionName"/> or <paramref name="gamePortName"/> is empty.</exception>
        public EdgegapMatchAllocator(
            IEdgegapApiClient api, IJoinCodeService joinCodes, string appName, string versionName, string gamePortName = "game")
        {
            if (api == null)
            {
                throw new ArgumentNullException(nameof(api));
            }

            if (joinCodes == null)
            {
                throw new ArgumentNullException(nameof(joinCodes));
            }

            if (string.IsNullOrWhiteSpace(appName))
            {
                throw new ArgumentException("An Edgegap app name is required.", nameof(appName));
            }

            if (string.IsNullOrWhiteSpace(versionName))
            {
                throw new ArgumentException("An Edgegap app version name is required.", nameof(versionName));
            }

            if (string.IsNullOrWhiteSpace(gamePortName))
            {
                throw new ArgumentException("A game port name is required.", nameof(gamePortName));
            }

            this.api = api;
            this.joinCodes = joinCodes;
            AppName = appName.Trim();
            VersionName = versionName.Trim();
            GamePortName = gamePortName.Trim();
        }

        /// <summary>The Edgegap application name.</summary>
        public string AppName { get; }

        /// <summary>The app version <see cref="AllocateAsync"/> deploys.</summary>
        public string VersionName { get; }

        /// <summary>The port name a deployment's game port is registered under.</summary>
        public string GamePortName { get; }

        /// <summary>How long <see cref="AllocateAsync"/> waits for Edgegap to report the deployment running before giving up. Default 2 minutes.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>How often <see cref="AllocateAsync"/> polls while waiting. Default 2 seconds.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <inheritdoc />
        public string DisplayName
        {
            get { return "Edgegap"; }
        }

        /// <summary>
        /// Creates or updates the app version this allocator deploys, pointing it at
        /// <paramref name="imageReference"/> (the same GHCR reference mp-host just pushed). Call this
        /// once per build, before the first match of that build is allocated; it is safe to call again
        /// for the same version with a newer image.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="imageReference"/> is empty, or <paramref name="ports"/> is empty.</exception>
        /// <exception cref="MatchAllocationException">Edgegap refused the request or could not be reached.</exception>
        public async Task EnsureAppVersionAsync(
            string imageReference,
            IReadOnlyList<EdgegapPortSpec> ports,
            IReadOnlyDictionary<string, string> environmentVariables = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imageReference))
            {
                throw new ArgumentException("A GHCR image reference is required.", nameof(imageReference));
            }

            if (ports == null || ports.Count == 0)
            {
                throw new ArgumentException("At least one port is required.", nameof(ports));
            }

            var request = new EdgegapAppVersionRequest(
                AppName, VersionName, imageReference.Trim(), ports, environmentVariables ?? EmptyEnvironment);
            try
            {
                await api.EnsureAppVersionAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("ensuring the app version", ex);
            }
        }

        /// <inheritdoc />
        public async Task<MatchAllocation> AllocateAsync(MatchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            EdgegapDeployment deployment;
            try
            {
                deployment = await api.DeployAsync(new EdgegapDeployRequest(AppName, VersionName, request.Region), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("requesting a deployment", ex);
            }

            deployment = await WaitUntilRunning(deployment, cancellationToken).ConfigureAwait(false);

            EdgegapPortMapping mapping = null;
            foreach (EdgegapPortMapping candidate in deployment.Ports)
            {
                if (string.Equals(candidate.Name, GamePortName, StringComparison.OrdinalIgnoreCase))
                {
                    mapping = candidate;
                    break;
                }
            }

            if (mapping == null || string.IsNullOrWhiteSpace(deployment.PublicIp))
            {
                await TryStop(deployment.RequestId, cancellationToken).ConfigureAwait(false);
                throw new MatchAllocationException(
                    "Edgegap deployment " + deployment.RequestId + " has no '" + GamePortName + "' port mapped.");
            }

            JoinTarget endpoint = JoinTarget.ForEndpoint(deployment.PublicIp, (ushort)mapping.ExternalPort, mapping.Transport);
            JoinCode code;
            try
            {
                code = await joinCodes.PublishAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                await TryStop(deployment.RequestId, cancellationToken).ConfigureAwait(false);
                throw Wrap("publishing a join code for the deployment", ex);
            }

            return new MatchAllocation(deployment.RequestId, endpoint, code);
        }

        /// <inheritdoc />
        public async Task ReleaseAsync(MatchAllocation allocation, CancellationToken cancellationToken = default)
        {
            if (allocation == null)
            {
                throw new ArgumentNullException(nameof(allocation));
            }

            try
            {
                await joinCodes.RevokeAsync(allocation.Code, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("revoking the join code", ex);
            }

            await TryStop(allocation.AllocationId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<EdgegapDeployment> WaitUntilRunning(EdgegapDeployment deployment, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (!deployment.Running)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    await TryStop(deployment.RequestId, cancellationToken).ConfigureAwait(false);
                    throw new MatchAllocationException("Edgegap deployment " + deployment.RequestId + " did not start within " + Timeout + ".");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    deployment = await api.GetDeploymentAsync(deployment.RequestId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is MatchAllocationException))
                {
                    throw Wrap("polling the deployment", ex);
                }
            }

            return deployment;
        }

        private async Task TryStop(string requestId, CancellationToken cancellationToken)
        {
            try
            {
                await api.StopDeploymentAsync(requestId, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Releasing/cleaning up is best-effort and idempotent; the provider may already have torn it down.
            }
        }

        private static MatchAllocationException Wrap(string what, Exception ex)
        {
            return new MatchAllocationException("Edgegap failed while " + what + ": " + ex.Message, false, ex);
        }

        private static readonly Dictionary<string, string> EmptyEnvironment = new Dictionary<string, string>();
    }
}
