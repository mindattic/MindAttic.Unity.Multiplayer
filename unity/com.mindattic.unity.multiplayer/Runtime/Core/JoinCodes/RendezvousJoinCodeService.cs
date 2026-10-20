using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The <see cref="IJoinCodeService"/> over the self-hosted rendezvous service (RFC 0001): the default for
    /// dedicated servers. A server registers its public endpoint (<see cref="RegisterAsync"/>) and this service
    /// keeps the registration alive with heartbeats until it is revoked or the service is disposed; a client
    /// resolves a code to the server's address and port.
    /// </summary>
    /// <remarks>
    /// <para>The rendezvous service maps codes to addresses; it does not get through NAT, so it suits servers with a
    /// reachable port (a VPS, or a port-mapping host such as RunPod), not a listen server at home.</para>
    /// <para>Heartbeats run on the thread pool every third of the registration's TTL. A transient failure
    /// (unreachable, 429, 5xx) is retried at the next beat; a registration the service no longer knows (it expired
    /// while the service was unreachable, or the service restarted) raises <see cref="RegistrationLost"/> and stops
    /// beating. Events are raised on the thread pool.</para>
    /// </remarks>
    public sealed class RendezvousJoinCodeService : IJoinCodeService, IDisposable
    {
        /// <summary>The shortest gap between heartbeats, whatever the TTL.</summary>
        public static readonly TimeSpan MinHeartbeatInterval = TimeSpan.FromMilliseconds(200);

        private readonly RendezvousClient client;
        private readonly bool ownsClient;
        private readonly string secret;
        private readonly Dictionary<JoinCode, Live> live = new Dictionary<JoinCode, Live>();
        private readonly object gate = new object();
        private bool disposed;

        /// <summary>Uses the service at <paramref name="baseUrl"/>; <paramref name="secret"/> is the operator's registration secret (servers only).</summary>
        /// <exception cref="ArgumentException"><paramref name="baseUrl"/> is not an absolute http(s) URL.</exception>
        public RendezvousJoinCodeService(string baseUrl, string secret = null)
            : this(new RendezvousClient(baseUrl), secret, true)
        {
        }

        /// <summary>Uses <paramref name="client"/>; disposes it only when <paramref name="ownsClient"/> is true.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
        public RendezvousJoinCodeService(RendezvousClient client, string secret = null, bool ownsClient = false)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.secret = secret;
            this.ownsClient = ownsClient;
        }

        /// <summary>A registration this service was keeping alive is gone (the code no longer resolves); with why.</summary>
        public event Action<JoinCode, string> RegistrationLost;

        /// <summary>A heartbeat renewed a registration.</summary>
        public event Action<JoinCode> HeartbeatSent;

        /// <summary>The client this service talks through.</summary>
        public RendezvousClient Client
        {
            get { return client; }
        }

        /// <summary>How many registrations this service is keeping alive.</summary>
        public int LiveCount
        {
            get
            {
                lock (gate)
                {
                    return live.Count;
                }
            }
        }

        /// <summary>
        /// Registers a dedicated server's public endpoint and keeps it alive. A null <see cref="ServerAdvertisement.Address"/>
        /// lets the service record the address the request came from.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="advertisement"/> is null.</exception>
        /// <exception cref="RendezvousException">The service refused (wrong secret, bad endpoint, full) or could not be reached.</exception>
        /// <exception cref="ObjectDisposedException">The service was disposed.</exception>
        public async Task<JoinCode> RegisterAsync(ServerAdvertisement advertisement, CancellationToken cancellationToken = default)
        {
            if (advertisement == null)
            {
                throw new ArgumentNullException(nameof(advertisement));
            }

            ThrowIfDisposed();
            RendezvousRegistration registration = await client
                .RegisterAsync(secret, advertisement.Address, advertisement.Port, advertisement.Transport, cancellationToken)
                .ConfigureAwait(false);

            var entry = new Live(registration);
            lock (gate)
            {
                if (disposed)
                {
                    entry.Stop.Cancel();
                }
                else
                {
                    live[registration.Code] = entry;
                }
            }

            if (entry.Stop.IsCancellationRequested)
            {
                await TryUnregister(registration).ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(RendezvousJoinCodeService));
            }

            entry.Loop = Task.Run(() => HeartbeatLoop(entry));
            return registration.Code;
        }

        /// <summary>Registers an endpoint target (<see cref="JoinTargetKind.Endpoint"/>) and keeps it alive.</summary>
        /// <exception cref="NotSupportedException"><paramref name="target"/> is a relay target.</exception>
        /// <inheritdoc />
        public Task<JoinCode> PublishAsync(JoinTarget target, CancellationToken cancellationToken = default)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.Kind != JoinTargetKind.Endpoint)
            {
                throw new NotSupportedException("The rendezvous service maps codes to addresses; publish a relay target with RelayJoinCodeService.");
            }

            return RegisterAsync(ServerAdvertisement.Explicit(target.Address, target.Port, target.Transport), cancellationToken);
        }

        /// <inheritdoc />
        public Task<JoinTarget> ResolveAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return client.ResolveAsync(code, cancellationToken);
        }

        /// <summary>Stops keeping <paramref name="code"/> alive and deletes it. False when this service did not publish it or it was already gone.</summary>
        /// <exception cref="RendezvousException">The service could not be reached (the registration then expires by itself).</exception>
        public async Task<bool> RevokeAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            Live entry;
            lock (gate)
            {
                if (!live.TryGetValue(code, out entry))
                {
                    return false;
                }

                live.Remove(code);
            }

            entry.Stop.Cancel();
            return await client.UnregisterAsync(code, entry.Registration.Token, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops every heartbeat and makes a best-effort attempt (up to 2 s) to delete every live registration; anything
        /// not deleted expires by itself after its TTL.
        /// </summary>
        public void Dispose()
        {
            List<Live> entries;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                entries = new List<Live>(live.Values);
                live.Clear();
            }

            var deletes = new List<Task>();
            foreach (Live entry in entries)
            {
                entry.Stop.Cancel();
                deletes.Add(TryUnregister(entry.Registration));
            }

            try
            {
                Task.WaitAll(deletes.ToArray(), TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // TryUnregister swallows its own failures; nothing to report here.
            }

            if (ownsClient)
            {
                client.Dispose();
            }
        }

        /// <summary>The gap between heartbeats for a <paramref name="ttlSeconds"/> TTL: a third of it, at least <see cref="MinHeartbeatInterval"/>.</summary>
        public static TimeSpan HeartbeatInterval(int ttlSeconds)
        {
            TimeSpan third = TimeSpan.FromSeconds(Math.Max(0, ttlSeconds) / 3.0);
            return third < MinHeartbeatInterval ? MinHeartbeatInterval : third;
        }

        private async Task HeartbeatLoop(Live entry)
        {
            TimeSpan interval = HeartbeatInterval(entry.Registration.TtlSeconds);
            CancellationToken stop = entry.Stop.Token;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stop).ConfigureAwait(false);
                    bool renewed = await client.HeartbeatAsync(entry.Registration.Code, entry.Registration.Token, stop).ConfigureAwait(false);
                    if (renewed)
                    {
                        Raise(HeartbeatSent, entry.Registration.Code);
                        continue;
                    }

                    Lose(entry, "The rendezvous service no longer knows code " + entry.Registration.Code + " (it expired or the service restarted).");
                    return;
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (RendezvousException ex) when (ex.IsTransient)
                {
                    // Unreachable, rate limited or a server error: try again at the next beat, before the TTL runs out.
                }
                catch (Exception ex)
                {
                    Lose(entry, "Heartbeat refused: " + ex.Message);
                    return;
                }
            }
        }

        private void Lose(Live entry, string reason)
        {
            lock (gate)
            {
                Live current;
                if (!live.TryGetValue(entry.Registration.Code, out current) || current != entry)
                {
                    return;
                }

                live.Remove(entry.Registration.Code);
            }

            entry.Stop.Cancel();
            Action<JoinCode, string> handler = RegistrationLost;
            if (handler != null)
            {
                try
                {
                    handler(entry.Registration.Code, reason);
                }
                catch
                {
                    // A handler's failure must not kill the heartbeat thread's caller.
                }
            }
        }

        private async Task TryUnregister(RendezvousRegistration registration)
        {
            try
            {
                await client.UnregisterAsync(registration.Code, registration.Token).ConfigureAwait(false);
            }
            catch
            {
                // Best effort: an unreachable service lets the registration expire by itself.
            }
        }

        private static void Raise(Action<JoinCode> handler, JoinCode code)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(code);
            }
            catch
            {
                // See Lose.
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RendezvousJoinCodeService));
            }
        }

        private sealed class Live
        {
            public Live(RendezvousRegistration registration)
            {
                Registration = registration;
            }

            public RendezvousRegistration Registration { get; }

            public CancellationTokenSource Stop { get; } = new CancellationTokenSource();

            public Task Loop { get; set; }
        }
    }
}
