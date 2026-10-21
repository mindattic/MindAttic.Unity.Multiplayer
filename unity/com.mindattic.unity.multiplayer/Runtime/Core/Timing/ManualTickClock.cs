using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// An <see cref="ITickClock"/> driven by hand: <see cref="Step"/> ticks once, <see cref="Advance"/>
    /// turns elapsed seconds into whole ticks and carries the remainder. For tests and engine-free
    /// simulations; in a running game the adapter's NetworkTickClock is used instead.
    /// </summary>
    public sealed class ManualTickClock : ITickClock
    {
        private double _accumulator;

        /// <summary>Creates a clock at tick 0.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is below 1.</exception>
        public ManualTickClock(int tickRate = NetConfig.DefaultTickRate)
        {
            if (tickRate < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "Tick rate must be at least 1.");
            }

            TickRate = tickRate;
            TickInterval = 1.0 / tickRate;
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

        /// <summary>Seconds accumulated towards the next tick, in [0, <see cref="TickInterval"/>).</summary>
        public double Pending
        {
            get { return _accumulator; }
        }

        /// <summary>Ticks <paramref name="ticks"/> times, raising <see cref="Ticked"/> for each.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> is negative.</exception>
        public void Step(int ticks = 1)
        {
            if (ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Cannot step a negative number of ticks.");
            }

            for (int i = 0; i < ticks; i++)
            {
                CurrentTick++;
                Action<long> handler = Ticked;
                if (handler != null)
                {
                    handler(CurrentTick);
                }
            }
        }

        /// <summary>
        /// Adds <paramref name="seconds"/> of elapsed time and ticks once per whole interval it completes.
        /// Returns the number of ticks run.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is negative, NaN or infinite.</exception>
        public int Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Elapsed time must be a finite, non-negative number.");
            }

            // Count whole intervals with a small tolerance so 1/30 s added 30 times makes exactly 30 ticks
            // despite floating-point rounding.
            const double epsilon = 1e-9;
            double whole = Math.Floor((_accumulator + seconds + epsilon) / TickInterval);
            if (whole > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Too much time for one Advance call.");
            }

            _accumulator += seconds;
            int ticks = (int)whole;
            _accumulator -= ticks * TickInterval;
            if (_accumulator < 0)
            {
                _accumulator = 0;
            }

            Step(ticks);
            return ticks;
        }
    }
}
