using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>What to ask GameLift's <c>CreateGameSession</c> to start, on a fleet mp-host already created.</summary>
    public sealed class GameLiftCreateGameSessionRequest
    {
        /// <summary>Describes a game session request.</summary>
        public GameLiftCreateGameSessionRequest(
            string fleetId, string aliasId, int maximumPlayerSessionCount, string name,
            IReadOnlyDictionary<string, string> gameProperties, string idempotencyToken)
        {
            FleetId = fleetId;
            AliasId = aliasId;
            MaximumPlayerSessionCount = maximumPlayerSessionCount;
            Name = name;
            GameProperties = gameProperties;
            IdempotencyToken = idempotencyToken;
        }

        /// <summary>The fleet to start the session on (from mp-host's GameLiftProvisioner). Ignored when <see cref="AliasId"/> is set.</summary>
        public string FleetId { get; }

        /// <summary>An alias to resolve to a fleet instead, letting mp-host swap fleets (an update) without this allocator changing. May be null.</summary>
        public string AliasId { get; }

        /// <summary>How many players the session must have room for (<see cref="MatchRequest.MaxPlayers"/>).</summary>
        public int MaximumPlayerSessionCount { get; }

        /// <summary>A name for the session, shown in the GameLift console; the match id is a natural choice. May be null.</summary>
        public string Name { get; }

        /// <summary>Free-form key/value hints GameLift stores with the session and hands back to the server process. May be null or empty.</summary>
        public IReadOnlyDictionary<string, string> GameProperties { get; }

        /// <summary>A caller-chosen id GameLift uses to de-duplicate a retried request; the match id is a natural choice. May be null.</summary>
        public string IdempotencyToken { get; }
    }

    /// <summary>A GameLift game session, as reported by <c>CreateGameSession</c>/<c>DescribeGameSessions</c>.</summary>
    public sealed class GameLiftGameSession
    {
        /// <summary>Describes a game session.</summary>
        public GameLiftGameSession(string gameSessionId, string status, string ipAddress, int port, string dnsName)
        {
            GameSessionId = gameSessionId;
            Status = status;
            IpAddress = ipAddress;
            Port = port;
            DnsName = dnsName;
        }

        /// <summary>GameLift's id for this session; pass it back to <see cref="IGameLiftApiClient.DescribeGameSessionAsync"/> and <see cref="IGameLiftApiClient.CreatePlayerSessionAsync"/>.</summary>
        public string GameSessionId { get; }

        /// <summary>ACTIVATING, ACTIVE, ERROR, TERMINATING or TERMINATED.</summary>
        public string Status { get; }

        /// <summary>The public IP players connect to; null while still activating.</summary>
        public string IpAddress { get; }

        /// <summary>The port players connect to; 0 while still activating.</summary>
        public int Port { get; }

        /// <summary>The fleet instance's DNS name, when GameLift reports one. May be null.</summary>
        public string DnsName { get; }
    }

    /// <summary>A GameLift player session, as reported by <c>CreatePlayerSession</c>.</summary>
    public sealed class GameLiftPlayerSession
    {
        /// <summary>Describes a player session.</summary>
        public GameLiftPlayerSession(string playerSessionId, string status)
        {
            PlayerSessionId = playerSessionId;
            Status = status;
        }

        /// <summary>
        /// The id the matchmaker hands the player to send as their <see cref="ConnectionPayload.PlayerToken"/>;
        /// the server's GameLiftServerHost reads it back to call <c>AcceptPlayerSession</c>.
        /// </summary>
        public string PlayerSessionId { get; }

        /// <summary>RESERVED, ACTIVE, COMPLETED or TIMEDOUT.</summary>
        public string Status { get; }
    }
}
