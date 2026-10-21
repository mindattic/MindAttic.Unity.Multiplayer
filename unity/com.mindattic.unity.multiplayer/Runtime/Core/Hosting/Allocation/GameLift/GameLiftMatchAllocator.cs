using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// <see cref="IMatchAllocator"/> for AWS GameLift: starts a game session on a fleet mp-host already
    /// created (build upload + fleet create -- see docs/articles/hosting/gamelift.md), waits for
    /// GameLift to report it ACTIVE with its connection info, and publishes the result through an
    /// <see cref="IJoinCodeService"/> so a player gets a short code instead of a raw address.
    /// </summary>
    /// <remarks>
    /// <para>Unlike Edgegap/Gameye, GameLift also needs a reservation per player before they connect:
    /// <see cref="CreatePlayerSessionAsync"/> returns a player session id the matchmaker must hand
    /// that player to send as their <see cref="ConnectionPayload.PlayerToken"/> (RFC 0001's existing
    /// reconnect-token field, reused rather than adding a new wire field). The server's
    /// <c>GameLiftServerHost</c> (<c>unity/.../Runtime/Hosting/GameLift</c>, compiled in only under
    /// the <c>MINDATTIC_GAMELIFT</c> version define) reads that token back off the connected client's
    /// seat and calls the GameLift Server SDK's <c>AcceptPlayerSession</c>/<c>RemovePlayerSession</c>.</para>
    /// <para><see cref="ReleaseAsync"/> only revokes the join code: GameLift has no API to force a
    /// game session to end from the outside -- only the server process itself, by calling
    /// <c>ProcessEnding</c> (again, <c>GameLiftServerHost</c>), actually ends one. A matchmaker that
    /// wants a match to stop should tell the game to wrap up (its own RPC or admin command), not
    /// expect <see cref="ReleaseAsync"/> to kill the process.</para>
    /// </remarks>
    public sealed class GameLiftMatchAllocator : IMatchAllocator
    {
        private readonly IGameLiftApiClient api;
        private readonly IJoinCodeService joinCodes;

        /// <summary>
        /// Starts game sessions on fleet <paramref name="fleetId"/> (or, when <paramref name="aliasId"/>
        /// is given, through that alias instead -- so mp-host can swap fleets on an update without this
        /// allocator changing) through <paramref name="api"/>, publishing join codes through
        /// <paramref name="joinCodes"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="api"/> or <paramref name="joinCodes"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="fleetId"/> is empty.</exception>
        public GameLiftMatchAllocator(IGameLiftApiClient api, IJoinCodeService joinCodes, string fleetId, string aliasId = null)
        {
            if (api == null)
            {
                throw new ArgumentNullException(nameof(api));
            }

            if (joinCodes == null)
            {
                throw new ArgumentNullException(nameof(joinCodes));
            }

            if (string.IsNullOrWhiteSpace(fleetId))
            {
                throw new ArgumentException("A GameLift fleet id is required.", nameof(fleetId));
            }

            this.api = api;
            this.joinCodes = joinCodes;
            FleetId = fleetId.Trim();
            AliasId = string.IsNullOrWhiteSpace(aliasId) ? null : aliasId.Trim();
        }

        /// <summary>The fleet <see cref="AllocateAsync"/> starts sessions on.</summary>
        public string FleetId { get; }

        /// <summary>The alias <see cref="AllocateAsync"/> resolves instead of <see cref="FleetId"/>, or null.</summary>
        public string AliasId { get; }

        /// <summary>How long <see cref="AllocateAsync"/> waits for GameLift to report the session ACTIVE before giving up. Default 2 minutes.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>How often <see cref="AllocateAsync"/> polls while waiting. Default 2 seconds.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <inheritdoc />
        public string DisplayName
        {
            get { return "AWS GameLift"; }
        }

        /// <inheritdoc />
        public async Task<MatchAllocation> AllocateAsync(MatchRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            GameLiftGameSession session;
            try
            {
                session = await api.CreateGameSessionAsync(
                    new GameLiftCreateGameSessionRequest(FleetId, AliasId, request.MaxPlayers, request.MatchId, request.Metadata, request.MatchId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("requesting a game session", ex);
            }

            session = await WaitUntilActive(session, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(session.IpAddress) || session.Port <= 0)
            {
                throw new MatchAllocationException("GameLift game session " + session.GameSessionId + " reports ACTIVE but has no address.");
            }

            JoinTarget endpoint = JoinTarget.ForEndpoint(session.IpAddress, (ushort)session.Port, TransportKind.Udp);
            JoinCode code;
            try
            {
                code = await joinCodes.PublishAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("publishing a join code for the game session", ex);
            }

            return new MatchAllocation(session.GameSessionId, endpoint, code);
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

            // GameLift has no API call to force a game session to end; only the server process itself,
            // through ProcessEnding (GameLiftServerHost), actually does that. Nothing further to release.
        }

        /// <summary>
        /// Reserves a seat on <paramref name="allocation"/>'s game session for <paramref name="playerId"/>.
        /// The matchmaker must hand the returned player session id to that player to send as their
        /// <see cref="ConnectionPayload.PlayerToken"/> before they connect; the server's
        /// <c>GameLiftServerHost</c> reads it back off the connected client's seat to call
        /// <c>AcceptPlayerSession</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="allocation"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="playerId"/> is empty.</exception>
        /// <exception cref="MatchAllocationException">GameLift refused the request or could not be reached.</exception>
        public async Task<string> CreatePlayerSessionAsync(
            MatchAllocation allocation, string playerId, string playerData = null, CancellationToken cancellationToken = default)
        {
            if (allocation == null)
            {
                throw new ArgumentNullException(nameof(allocation));
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException("A player id is required.", nameof(playerId));
            }

            try
            {
                GameLiftPlayerSession session =
                    await api.CreatePlayerSessionAsync(allocation.AllocationId, playerId, playerData, cancellationToken).ConfigureAwait(false);
                return session.PlayerSessionId;
            }
            catch (Exception ex) when (!(ex is MatchAllocationException))
            {
                throw Wrap("creating a player session", ex);
            }
        }

        private async Task<GameLiftGameSession> WaitUntilActive(GameLiftGameSession session, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (!string.Equals(session.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                if (IsTerminal(session.Status))
                {
                    throw new MatchAllocationException("GameLift game session " + session.GameSessionId + " ended up " + session.Status + ".");
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new MatchAllocationException("GameLift game session " + session.GameSessionId + " did not activate within " + Timeout + ".");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    session = await api.DescribeGameSessionAsync(session.GameSessionId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is MatchAllocationException))
                {
                    throw Wrap("polling the game session", ex);
                }
            }

            return session;
        }

        private static bool IsTerminal(string status)
        {
            return string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "TERMINATING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "TERMINATED", StringComparison.OrdinalIgnoreCase);
        }

        private static MatchAllocationException Wrap(string what, Exception ex)
        {
            return new MatchAllocationException("GameLift failed while " + what + ": " + ex.Message, false, ex);
        }
    }
}
