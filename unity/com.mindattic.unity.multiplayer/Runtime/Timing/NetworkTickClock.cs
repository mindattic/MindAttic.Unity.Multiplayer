using System;
using Unity.Multiplayer.Core;
using Unity.Netcode;

namespace Unity.Multiplayer
{
    /// <summary>
    /// The <see cref="ITickClock"/> of a running session, driven by NGO's <c>NetworkTickSystem</c>: one
    /// <see cref="Ticked"/> per network tick, at <see cref="NetConfig.TickRate"/>. The server simulation steps
    /// on it (for example <c>Physics2D.Simulate(clock.TickInterval)</c> in a <see cref="Ticked"/> handler).
    /// </summary>
    /// <remarks>
    /// <para><see cref="NetSession.Clock"/> hands out one per session: it is created when the session goes
    /// live (Running or Connected) and stops when the session ends, so a handler never runs after the
    /// session is over. <see cref="CurrentTick"/> counts this clock's own ticks from 0, keeping the
    /// <see cref="ITickClock"/> contract (up by exactly one per tick, never back) even when NGO resynchronises
    /// a client's network time; <see cref="NetworkTick"/> is NGO's server tick, for stamping messages.</para>
    /// <para>Ticks run on Unity's main thread, during NGO's network update.</para>
    /// </remarks>
    public sealed class NetworkTickClock : ITickClock, IDisposable
    {
        private NetworkTickSystem _system;
        private int _lastNetworkTick;

        /// <summary>Starts counting the ticks of <paramref name="system"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="system"/> is null.</exception>
        internal NetworkTickClock(NetworkTickSystem system)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            _system = system;
            TickRate = (int)system.TickRate;
            TickInterval = 1.0 / TickRate;
            _lastNetworkTick = system.ServerTime.Tick;
            _system.Tick += OnTick;
        }

        /// <inheritdoc />
        public event Action<long> Ticked;

        /// <inheritdoc />
        public int TickRate { get; }

        /// <inheritdoc />
        public double TickInterval { get; }

        /// <inheritdoc />
        public long CurrentTick { get; private set; }

        /// <inheritdoc />
        public double Time
        {
            get { return CurrentTick * TickInterval; }
        }

        /// <summary>
        /// NGO's server tick: the same number on the server and every client for the same moment, so it can
        /// stamp messages. Once the clock has stopped it keeps the last value seen.
        /// </summary>
        public int NetworkTick
        {
            get { return _system != null ? _system.ServerTime.Tick : _lastNetworkTick; }
        }

        /// <summary>True until the session ends (or <see cref="Dispose"/> is called); after that no tick is raised.</summary>
        public bool IsRunning
        {
            get { return _system != null; }
        }

        /// <summary>Stops the clock: <see cref="Ticked"/> is not raised again. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_system == null)
            {
                return;
            }

            _lastNetworkTick = _system.ServerTime.Tick;
            _system.Tick -= OnTick;
            _system = null;
        }

        private void OnTick()
        {
            if (_system == null)
            {
                return;
            }

            _lastNetworkTick = _system.ServerTime.Tick;
            CurrentTick++;
            Action<long> handler = Ticked;
            if (handler != null)
            {
                handler(CurrentTick);
            }
        }
    }
}
