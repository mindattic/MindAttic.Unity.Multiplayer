using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// A scriptable <see cref="IGameyeApiClient"/>: <see cref="GameyeMatchAllocatorTests"/> drives
    /// <see cref="GameyeMatchAllocator"/> against this instead of real HTTP.
    /// </summary>
    internal sealed class FakeGameyeApiClient : IGameyeApiClient
    {
        public readonly List<string> Calls = new List<string>();

        public GameyeImageRegistration LastRegistration;
        public GameyeStartSessionRequest LastStartRequest;

        public string SessionId = "session-1";
        public string Ip = "203.0.113.60";
        public int Port = 32000;
        public TransportKind Transport = TransportKind.Udp;
        public string ReportedPortName = "game";

        /// <summary>How many <see cref="GetSessionAsync"/> calls it takes before the session reports ready.</summary>
        public int ReadyAfterPolls = 1;

        public bool FailRegisterWithAllocationException;
        public bool FailStartWithAllocationException;
        public bool FailStartWithGenericException;

        public string StoppedSessionId;

        private int polls;

        public Task RegisterImageAsync(GameyeImageRegistration registration, CancellationToken cancellationToken = default)
        {
            Calls.Add("RegisterImage");
            LastRegistration = registration;
            if (FailRegisterWithAllocationException)
            {
                throw new MatchAllocationException("Gameye says no.");
            }

            return Task.CompletedTask;
        }

        public Task<GameyeSession> StartSessionAsync(GameyeStartSessionRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("StartSession");
            LastStartRequest = request;
            if (FailStartWithAllocationException)
            {
                throw new MatchAllocationException("Gameye refused the session.");
            }

            if (FailStartWithGenericException)
            {
                throw new InvalidOperationException("boom");
            }

            polls = 0;
            return Task.FromResult(new GameyeSession(SessionId, null, false, Array.Empty<GameyePortMapping>()));
        }

        public Task<GameyeSession> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Calls.Add("GetSession");
            polls++;
            bool ready = polls >= ReadyAfterPolls;
            if (!ready)
            {
                return Task.FromResult(new GameyeSession(sessionId, null, false, Array.Empty<GameyePortMapping>()));
            }

            var ports = new[] { new GameyePortMapping(ReportedPortName, Port, Transport) };
            return Task.FromResult(new GameyeSession(sessionId, Ip, true, ports));
        }

        public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Calls.Add("Stop");
            StoppedSessionId = sessionId;
            return Task.CompletedTask;
        }
    }
}
