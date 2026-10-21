using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using Unity.Netcode.Transports.UTP;
#if MINDATTIC_UGS
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
#endif

namespace Unity.Multiplayer
{
    /// <summary>
    /// The <see cref="IJoinCodeService"/> over Unity Relay (RFC 0001): the default for listen servers, because Relay gets
    /// through NAT and needs no port forwarding. Active only when com.unity.services.multiplayer is installed (it
    /// defines <c>MINDATTIC_UGS</c>); otherwise <see cref="IsAvailable"/> is false and every call throws
    /// <see cref="NotSupportedException"/> saying what to install.
    /// </summary>
    /// <remarks>
    /// <para>The join code players type is Relay's own code with the package's check character added
    /// (<see cref="RelayCodes"/>), so it gets the same typo tolerance as a rendezvous code.</para>
    /// <para>Relay needs Unity Gaming Services: the project must be linked to a Cloud project with Relay enabled. The
    /// first call initializes Unity Services and signs in anonymously if nothing has yet.</para>
    /// <para><see cref="NetSession"/> drives it: <see cref="NetSession.HostWithCode"/> allocates before the host starts,
    /// and <see cref="NetSession.JoinByCode"/> joins the allocation before the client connects. Call it directly only to
    /// resolve or publish codes yourself.</para>
    /// </remarks>
    public sealed class RelayJoinCodeService : IJoinCodeService
    {
        /// <summary>What to tell a developer who asks for Relay without the package.</summary>
        public const string MissingPackageMessage =
            "Unity Relay join codes need the com.unity.services.multiplayer package (it defines MINDATTIC_UGS). "
            + "Install it from the Package Manager and link the project to a Unity Cloud project with Relay enabled, "
            + "or use the rendezvous service (NetConfig.RendezvousUrl) with a dedicated server.";

        private readonly HashSet<JoinCode> published = new HashSet<JoinCode>();
        private readonly object gate = new object();
#if MINDATTIC_UGS
        private readonly Dictionary<string, JoinAllocation> joined = new Dictionary<string, JoinAllocation>(StringComparer.Ordinal);
        private Allocation hostAllocation;
#endif

        /// <summary>True when the Relay SDK is compiled in (com.unity.services.multiplayer is installed).</summary>
        public static bool IsAvailable
        {
            get
            {
#if MINDATTIC_UGS
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// The Relay region to allocate in (for example <c>us-east1</c>); null lets Relay pick the closest one.
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// Wraps an allocation's Relay code (<see cref="JoinTarget.ForRelay"/>) as the code players type. Relay itself
        /// created the allocation, so nothing is sent.
        /// </summary>
        /// <exception cref="NotSupportedException">The target is not a relay target, or Relay is not installed.</exception>
        public Task<JoinCode> PublishAsync(JoinTarget target, CancellationToken cancellationToken = default)
        {
            RequireAvailable();
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.Kind != JoinTargetKind.Relay)
            {
                throw new NotSupportedException("Relay publishes relay allocations; publish an address with the rendezvous service.");
            }

            JoinCode code = RelayCodes.ToJoinCode(target.RelayJoinCode);
            lock (gate)
            {
                published.Add(code);
            }

            return Task.FromResult(code);
        }

        /// <summary>
        /// Joins the Relay allocation behind <paramref name="code"/> and returns it as a relay target, or null when Relay
        /// does not know the code (or the code cannot be a Relay code). The joined allocation is kept for the connect
        /// that follows.
        /// </summary>
        /// <exception cref="NotSupportedException">Relay is not installed.</exception>
        public async Task<JoinTarget> ResolveAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            RequireAvailable();
            string relayCode;
            if (!RelayCodes.TryGetRelayCode(code, out relayCode))
            {
                return null;
            }

#if MINDATTIC_UGS
            await EnsureSignedInAsync().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            JoinAllocation allocation;
            try
            {
                allocation = await RelayService.Instance.JoinAllocationAsync(relayCode).ConfigureAwait(true);
            }
            catch (RelayServiceException ex) when (IsUnknownCode(ex))
            {
                return null;
            }

            lock (gate)
            {
                joined[relayCode] = allocation;
            }

            return JoinTarget.ForRelay(relayCode);
#else
            await Task.CompletedTask;
            return null;
#endif
        }

        /// <summary>
        /// Forgets a code this service published. Relay closes the allocation itself when the host leaves, so nothing is
        /// sent. False when this service did not publish it.
        /// </summary>
        public Task<bool> RevokeAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            bool removed;
            lock (gate)
            {
                removed = published.Remove(code);
            }

            return Task.FromResult(removed);
        }

        /// <summary>
        /// Host side: creates a Relay allocation for <paramref name="maxConnections"/> remote players and returns the relay
        /// target to publish. The allocation is kept until <see cref="ApplyHost"/> points the transport at it.
        /// </summary>
        /// <exception cref="NotSupportedException">Relay is not installed.</exception>
        internal async Task<JoinTarget> AllocateAsync(int maxConnections, TransportKind transportKind)
        {
            RequireAvailable();
#if MINDATTIC_UGS
            await EnsureSignedInAsync().ConfigureAwait(true);
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Math.Max(1, maxConnections), Region).ConfigureAwait(true);
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId).ConfigureAwait(true);
            lock (gate)
            {
                hostAllocation = allocation;
            }

            return JoinTarget.ForRelay(relayCode, transportKind);
#else
            await Task.CompletedTask;
            throw new NotSupportedException(MissingPackageMessage);
#endif
        }

        /// <summary>
        /// Host side: points <paramref name="transport"/> at the allocation <see cref="AllocateAsync"/> made (DTLS over
        /// UDP, or secure WebSockets). Call after the session has set the transport's connection data and before NGO
        /// starts. False when there is no allocation waiting.
        /// </summary>
        internal bool ApplyHost(TransportKind transportKind, UnityTransport transport)
        {
#if MINDATTIC_UGS
            Allocation allocation;
            lock (gate)
            {
                allocation = hostAllocation;
                hostAllocation = null;
            }

            if (allocation == null)
            {
                return false;
            }

            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType(transportKind)));
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Client side: points <paramref name="transport"/> at the allocation <see cref="ResolveAsync"/> joined for
        /// <paramref name="target"/>. False when there is none (resolve first).
        /// </summary>
        internal bool ApplyJoin(JoinTarget target, TransportKind transportKind, UnityTransport transport)
        {
            if (target == null || target.Kind != JoinTargetKind.Relay)
            {
                return false;
            }

#if MINDATTIC_UGS
            JoinAllocation allocation;
            lock (gate)
            {
                if (!joined.TryGetValue(target.RelayJoinCode, out allocation))
                {
                    return false;
                }

                joined.Remove(target.RelayJoinCode);
            }

            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType(transportKind)));
            return true;
#else
            return false;
#endif
        }

        private static void RequireAvailable()
        {
            if (!IsAvailable)
            {
                throw new NotSupportedException(MissingPackageMessage);
            }
        }

#if MINDATTIC_UGS
        private static string ConnectionType(TransportKind transportKind)
        {
            return transportKind == TransportKind.WebSocket ? "wss" : "dtls";
        }

        private static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync().ConfigureAwait(true);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync().ConfigureAwait(true);
            }
        }

        private static bool IsUnknownCode(RelayServiceException ex)
        {
            // JoinCodeNotFound for an unknown or expired code; InvalidRequest for one Relay will not parse.
            string reason = ex.Reason.ToString();
            return reason.IndexOf("NotFound", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("InvalidRequest", StringComparison.OrdinalIgnoreCase) >= 0;
        }
#endif
    }
}
