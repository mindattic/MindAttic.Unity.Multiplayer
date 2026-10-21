using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// <see cref="IMatchAllocator"/> for Gameye: starts one session per match from an image already
    /// registered with Gameye, waits for it to report ready with its ports mapped, and publishes the
    /// result through an <see cref="IJoinCodeService"/> so a player gets a short code instead of a raw
    /// address.
    /// </summary>
    public sealed class GameyeMatchAllocator : IMatchAllocator
    {
        private readonly IGameyeApiClient api;
        private readonly IJoinCodeService joinCodes;
        private readonly IReadOnlyList<GameyePortSpec> ports;
        private readonly IReadOnlyDictionary<string, string> environmentVariables;

        /// <summary>
        /// Starts sessions of image <paramref name="imageName"/> in <paramref name="location"/> through
        /// <paramref name="api"/>, publishing join codes through <paramref name="joinCodes"/>. Every
        /// session declares the same <paramref name="ports"/> and <paramref name="environmentVariables"/>
        /// (rendezvous URL/secret and the like); <paramref name="gamePortName"/> is the
        /// <see cref="GameyePortSpec.Name"/> the game port is declared under.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="api"/>, <paramref name="joinCodes"/> or <paramref name="ports"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="imageName"/>, <paramref name="location"/> or <paramref name="gamePortName"/> is empty, or <paramref name="ports"/> is empty.
        /// </exception>
        public GameyeMatchAllocator(
            IGameyeApiClient api,
            IJoinCodeService joinCodes,
            string imageName,
            string location,
            IReadOnlyList<GameyePortSpec> ports,
            IReadOnlyDictionary<string, string> environmentVariables = null,
            string gamePortName = "game")
        {
            if (api == null)
            {
                throw new ArgumentNullException(nameof(api));
            }

            if (joinCodes == null)
            {
                throw new ArgumentNullException(nameof(joinCodes));
            }

            if (string.IsNullOrWhiteSpace(imageName))
            {
                throw new ArgumentException("A Gameye image name is required.", nameof(imageName));
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("A Gameye location is required.", nameof(location));
            }

            if (ports == null)
            {
                throw new ArgumentNullException(nameof(ports));
            }

            if (ports.Count == 0)
            {
                throw new ArgumentException("At least one port is required.", nameof(ports));
            }

            if (string.IsNullOrWhiteSpace(gamePortName))
            {
                throw new ArgumentException("A game port name is required.", nameof(gamePortName));
            }

            this.api = api;
            this.joinCodes = joinCodes;
            this.ports = ports;
            this.environmentVariables = environmentVariables ?? EmptyEnvironment;
            ImageName = imageName.Trim();
            Location = location.Trim();
            GamePortName = gamePortName.Trim();
        }

        /// <summary>The image name <see cref="AllocateAsync"/> starts sessions from.</summary>
        public string ImageName { get; }

        /// <summary>The Gameye location/fleet sessions start in.</summary>
        public string Location { get; }

        /// <summary>The port name a session's game port is registered under.</summary>
        public string GamePortName { get; }

        /// <summary>How long <see cref="AllocateAsync"/> waits for Gameye to report the session ready before giving up. Default 2 minutes.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>How often <see cref="AllocateAsync"/> polls while waiting. Default 2 seconds.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <inheritdoc />
        public string DisplayName
        {
            get { return "Gameye"; }
        }

        /// <summary>
        /// Registers (or updates) <see cref="ImageName"/> with Gameye, pointing it at
        /// <paramref name="imageReference"/> (the same GHCR reference mp-host just pushed). Call this
        /// once per build, before the first match of that build is allocated.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="imageReference"/> is empty.</exception>
        /// <exception cref="MatchAllocationException">Gameye refused the request or could not be reached.</exception>
        public async Task RegisterImageAsync(
            string imageReference, string registryUsername = null, string registryPassword = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imageReference))
            {
                throw new ArgumentException("A GHCR image reference is required.", nameof(imageReference));
            }

            var registration = new GameyeImageRegistration(ImageName, imageReference.Trim(), registryUsername, registryPassword);
            try
            {
                await api.RegisterImageAsync(registration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("registering the image", ex);
            }
        }

        /// <inheritdoc />
        public async Task<MatchAllocation> AllocateAsync(MatchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            GameyeSession session;
            try
            {
                session = await api.StartSessionAsync(
                    new GameyeStartSessionRequest(ImageName, Location, request.MatchId, ports, environmentVariables), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("starting a session", ex);
            }

            session = await WaitUntilReady(session, cancellationToken).ConfigureAwait(false);

            GameyePortMapping mapping = null;
            foreach (GameyePortMapping candidate in session.Ports)
            {
                if (string.Equals(candidate.Name, GamePortName, StringComparison.OrdinalIgnoreCase))
                {
                    mapping = candidate;
                    break;
                }
            }

            if (mapping == null || string.IsNullOrWhiteSpace(session.Ip))
            {
                await TryStop(session.SessionId, cancellationToken).ConfigureAwait(false);
                throw new MatchAllocationException("Gameye session " + session.SessionId + " has no '" + GamePortName + "' port mapped.");
            }

            JoinTarget endpoint = JoinTarget.ForEndpoint(session.Ip, (ushort)mapping.Port, mapping.Transport);
            JoinCode code;
            try
            {
                code = await joinCodes.PublishAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                await TryStop(session.SessionId, cancellationToken).ConfigureAwait(false);
                throw Wrap("publishing a join code for the session", ex);
            }

            return new MatchAllocation(session.SessionId, endpoint, code);
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

        private async Task<GameyeSession> WaitUntilReady(GameyeSession session, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (!session.Ready)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    await TryStop(session.SessionId, cancellationToken).ConfigureAwait(false);
                    throw new MatchAllocationException("Gameye session " + session.SessionId + " did not start within " + Timeout + ".");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    session = await api.GetSessionAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is MatchAllocationException))
                {
                    throw Wrap("polling the session", ex);
                }
            }

            return session;
        }

        private async Task TryStop(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                await api.StopSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Releasing/cleaning up is best-effort and idempotent; the provider may already have torn it down.
            }
        }

        private static MatchAllocationException Wrap(string what, Exception ex)
        {
            return new MatchAllocationException("Gameye failed while " + what + ": " + ex.Message, false, ex);
        }

        private static readonly Dictionary<string, string> EmptyEnvironment = new Dictionary<string, string>();
    }
}
