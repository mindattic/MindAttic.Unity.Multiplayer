using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// A scriptable <see cref="IEdgegapApiClient"/>: <see cref="EdgegapMatchAllocatorTests"/> drives
    /// <see cref="EdgegapMatchAllocator"/> against this instead of real HTTP.
    /// </summary>
    internal sealed class FakeEdgegapApiClient : IEdgegapApiClient
    {
        public readonly List<string> Calls = new List<string>();

        public EdgegapAppVersionRequest LastAppVersionRequest;
        public EdgegapDeployRequest LastDeployRequest;

        public string RequestId = "req-1";
        public string PublicIp = "203.0.113.50";
        public int ExternalPort = 31000;
        public TransportKind Transport = TransportKind.Udp;
        public string ReportedPortName = "game";

        /// <summary>How many <see cref="GetDeploymentAsync"/> calls it takes before the deployment reports running.</summary>
        public int RunningAfterPolls = 1;

        public bool FailEnsureWithAllocationException;
        public bool FailDeployWithAllocationException;
        public bool FailDeployWithGenericException;

        public string StoppedRequestId;

        private int polls;

        public Task EnsureAppVersionAsync(EdgegapAppVersionRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("EnsureAppVersion");
            LastAppVersionRequest = request;
            if (FailEnsureWithAllocationException)
            {
                throw new MatchAllocationException("Edgegap says no.");
            }

            return Task.CompletedTask;
        }

        public Task<EdgegapDeployment> DeployAsync(EdgegapDeployRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("Deploy");
            LastDeployRequest = request;
            if (FailDeployWithAllocationException)
            {
                throw new MatchAllocationException("Edgegap refused the deployment.");
            }

            if (FailDeployWithGenericException)
            {
                throw new InvalidOperationException("boom");
            }

            polls = 0;
            return Task.FromResult(new EdgegapDeployment(RequestId, null, false, Array.Empty<EdgegapPortMapping>()));
        }

        public Task<EdgegapDeployment> GetDeploymentAsync(string requestId, CancellationToken cancellationToken = default)
        {
            Calls.Add("GetDeployment");
            polls++;
            bool running = polls >= RunningAfterPolls;
            if (!running)
            {
                return Task.FromResult(new EdgegapDeployment(requestId, null, false, Array.Empty<EdgegapPortMapping>()));
            }

            var ports = new[] { new EdgegapPortMapping(ReportedPortName, ExternalPort, Transport) };
            return Task.FromResult(new EdgegapDeployment(requestId, PublicIp, true, ports));
        }

        public Task StopDeploymentAsync(string requestId, CancellationToken cancellationToken = default)
        {
            Calls.Add("Stop");
            StoppedRequestId = requestId;
            return Task.CompletedTask;
        }
    }
}
