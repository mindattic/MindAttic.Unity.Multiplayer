using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
#if MINDATTIC_UGS
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
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
    /// <para>Relay needs Unity Gaming Services: the project must be linked to a Cloud project with Relay enabled
    /// (<see cref="Readiness"/>). <see cref="SignInAsync"/> initializes Unity Services in <see cref="Environment"/> and signs
    /// in anonymously, once per process; <see cref="NetBootstrap"/> calls it as the scene starts (Sign In At Startup), and
    /// the first allocation or lookup does if nothing has yet. A project that is not linked fails before anything is sent,
    /// with <see cref="RelaySetup.NotLinkedMessage"/>; other Unity Services failures become a <see cref="RelaySetupException"/>
    /// whose message says what to fix (<see cref="RelaySetup.ExplainFailure"/>).</para>
    /// <para><see cref="NetSession"/> drives it: <see cref="NetSession.HostWithCode"/> allocates before the host starts,
    /// and <see cref="NetSession.JoinByCode"/> joins the allocation before the client connects. Call it directly only to
    /// resolve or publish codes yourself.</para>
    /// </remarks>
    public sealed class RelayJoinCodeService : IJoinCodeService
    {
        /// <summary>What to tell a developer who asks for Relay without the package (<see cref="RelaySetup.NotInstalledMessage"/>).</summary>
        public const string MissingPackageMessage = RelaySetup.NotInstalledMessage;

        private static readonly object SignInGate = new object();
        private static Task _signIn;
        private static string _signedInEnvironment;

        private readonly HashSet<JoinCode> published = new HashSet<JoinCode>();
        private readonly object gate = new object();
        private string environment = RelaySetup.DefaultEnvironment;
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
        /// Where this build stands (<see cref="RelaySetup.Check"/>): the package compiled in, and the project linked to a
        /// Unity Cloud project (<c>Application.cloudProjectId</c>, which Unity bakes into every build).
        /// </summary>
        public static RelayReadiness Readiness
        {
            get { return ReadinessOverride ?? RelaySetup.Check(IsAvailable, Application.cloudProjectId); }
        }

        /// <summary>
        /// True when Relay is worth trying: the package is installed (linked or not; a project that is not linked fails with
        /// a message saying how to link it). The join-code defaults use this.
        /// </summary>
        public static bool IsInstalled
        {
            get { return Readiness != RelayReadiness.NotInstalled; }
        }

        /// <summary>Tests: pretend the project stands here (null reads the real state).</summary>
        internal static RelayReadiness? ReadinessOverride { get; set; }

        /// <summary>
        /// The Relay region to allocate in (for example <c>us-east1</c>); null lets Relay pick the closest one.
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// The Unity Services environment of the linked Cloud project to use (<c>production</c> when empty). Unity Services
        /// is initialized once per process, so set it before the first sign-in (NetBootstrap's Relay Environment does).
        /// </summary>
        public string Environment
        {
            get { return environment; }
            set { environment = RelaySetup.EnvironmentOrDefault(value); }
        }

        /// <summary>True once this process has signed in to Unity Authentication.</summary>
        public static bool IsSignedIn
        {
            get
            {
#if MINDATTIC_UGS
                return UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Initializes Unity Services in <see cref="Environment"/> and signs in anonymously (players need no account). One
        /// sign-in is shared by every caller in the process; a failed one is tried again on the next call.
        /// </summary>
        /// <exception cref="NotSupportedException">Relay is not installed.</exception>
        /// <exception cref="RelaySetupException">The project is not linked, or Unity Services refused or could not be reached.</exception>
        public Task SignInAsync()
        {
            RequireReady();
            lock (SignInGate)
            {
                if (_signIn == null || _signIn.IsFaulted || _signIn.IsCanceled)
                {
                    _signedInEnvironment = Environment;
                    _signIn = SignInCoreAsync(Environment);
                }
                else if (!string.Equals(_signedInEnvironment, Environment, StringComparison.Ordinal))
                {
                    Debug.LogWarning("[MindAttic.Multiplayer] Unity Services is already initialized in environment \"" + _signedInEnvironment
                        + "\"; Relay Environment \"" + Environment + "\" takes effect after a restart.");
                }

                return _signIn;
            }
        }

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
        /// <exception cref="RelaySetupException">The project is not linked, or Unity Services refused or could not be reached.</exception>
        public async Task<JoinTarget> ResolveAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            RequireAvailable();
            string relayCode;
            if (!RelayCodes.TryGetRelayCode(code, out relayCode))
            {
                return null;
            }

            await SignInAsync().ConfigureAwait(true);
#if MINDATTIC_UGS
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
            catch (Exception ex) when (!(ex is RelaySetupException) && !(ex is OperationCanceledException))
            {
                throw Explained(ex, Environment);
            }

            lock (gate)
            {
                joined[relayCode] = allocation;
            }

            return JoinTarget.ForRelay(relayCode);
#else
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
        /// <exception cref="RelaySetupException">The project is not linked, or Unity Services refused or could not be reached.</exception>
        internal async Task<JoinTarget> AllocateAsync(int maxConnections, TransportKind transportKind)
        {
            RequireAvailable();
            await SignInAsync().ConfigureAwait(true);
#if MINDATTIC_UGS
            Allocation allocation;
            string relayCode;
            try
            {
                allocation = await RelayService.Instance.CreateAllocationAsync(Math.Max(1, maxConnections), Region).ConfigureAwait(true);
                relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId).ConfigureAwait(true);
            }
            catch (Exception ex) when (!(ex is RelaySetupException) && !(ex is OperationCanceledException))
            {
                throw Explained(ex, Environment);
            }

            lock (gate)
            {
                hostAllocation = allocation;
            }

            return JoinTarget.ForRelay(relayCode, transportKind);
#else
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

        /// <summary>The message to show for a failed Relay call: a <see cref="RelaySetupException"/>'s own, else a sentence naming the cause.</summary>
        internal static string Describe(Exception ex, string what)
        {
            if (ex is RelaySetupException || ex is NotSupportedException)
            {
                return ex.Message;
            }

            return what + ": " + ex.Message;
        }

        /// <summary>Throws when the package is missing (<see cref="NotSupportedException"/>) or the project is not linked.</summary>
        private static void RequireReady()
        {
            RequireAvailable();
            if (Readiness == RelayReadiness.NotLinked)
            {
                throw new RelaySetupException(RelayReadiness.NotLinked, RelaySetup.NotLinkedMessage);
            }
        }

        private static void RequireAvailable()
        {
            if (Readiness == RelayReadiness.NotInstalled)
            {
                throw new NotSupportedException(MissingPackageMessage);
            }
        }

#pragma warning disable 1998 // without the package there is nothing to await
        private static async Task SignInCoreAsync(string environmentName)
        {
#if MINDATTIC_UGS
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(environmentName)).ConfigureAwait(true);
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync().ConfigureAwait(true);
                }

                Debug.Log("[MindAttic.Multiplayer] Signed in to Unity Services (environment " + environmentName + ") for Relay join codes.");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw Explained(ex, environmentName);
            }
#else
            throw new NotSupportedException(MissingPackageMessage);
#endif
        }
#pragma warning restore 1998

#if MINDATTIC_UGS
        private static RelaySetupException Explained(Exception ex, string environmentName)
        {
            string reason = null;
            if (ex is RelayServiceException relay)
            {
                reason = relay.Reason.ToString();
            }
            else if (ex is RequestFailedException request)
            {
                reason = request.ErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            string message = RelaySetup.ExplainFailure(ex.GetType().Name, reason, ex.Message, environmentName);
            RelayReadiness readiness = message == RelaySetup.NotLinkedMessage ? RelayReadiness.NotLinked : RelayReadiness.Ready;
            return new RelaySetupException(readiness, message, ex);
        }

        private static string ConnectionType(TransportKind transportKind)
        {
            return transportKind == TransportKind.WebSocket ? "wss" : "dtls";
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
