using System;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The fixed-rate clock the server simulation steps on. The NGO adapter implements it off
    /// NetworkTickSystem (NetworkTickClock); <see cref="ManualTickClock"/> implements it for tests and
    /// engine-free simulations.
    /// </summary>
    /// <remarks>
    /// Contract every implementation keeps:
    /// <list type="bullet">
    /// <item><see cref="TickRate"/> is at least 1 and does not change while the clock runs.</item>
    /// <item><see cref="CurrentTick"/> starts at 0 and goes up by exactly 1 per tick; it never skips or goes back.</item>
    /// <item><see cref="Ticked"/> is raised once per tick, in order, after <see cref="CurrentTick"/> holds the new tick.
    /// When a frame covers several ticks, it is raised once for each of them.</item>
    /// <item><see cref="Time"/> equals <see cref="CurrentTick"/> × <see cref="TickInterval"/>.</item>
    /// </list>
    /// </remarks>
    public interface ITickClock
    {
        /// <summary>Ticks per second.</summary>
        int TickRate { get; }

        /// <summary>Seconds per tick: 1 / <see cref="TickRate"/>.</summary>
        double TickInterval { get; }

        /// <summary>Ticks elapsed since the clock started; 0 before the first tick.</summary>
        long CurrentTick { get; }

        /// <summary>Simulation time in seconds: <see cref="CurrentTick"/> × <see cref="TickInterval"/>.</summary>
        double Time { get; }

        /// <summary>Raised once per tick with the new <see cref="CurrentTick"/>.</summary>
        event Action<long> Ticked;
    }
}
