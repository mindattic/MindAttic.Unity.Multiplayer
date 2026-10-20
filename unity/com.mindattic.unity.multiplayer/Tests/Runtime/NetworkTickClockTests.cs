using System.Collections.Generic;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T8: <see cref="NetworkTickClock"/> keeps the <see cref="ITickClock"/> contract on top of a
    /// NetworkTickSystem driven by hand (no NetworkManager needed).
    /// </summary>
    public class NetworkTickClockTests
    {
        [Test]
        public void StartsAtZeroWithTheSystemsRate()
        {
            var system = new NetworkTickSystem(30, 0, 0);
            using (var clock = new NetworkTickClock(system))
            {
                Assert.That(clock.TickRate, Is.EqualTo(30));
                Assert.That(clock.TickInterval, Is.EqualTo(1.0 / 30).Within(1e-12));
                Assert.That(clock.CurrentTick, Is.EqualTo(0));
                Assert.That(clock.Time, Is.EqualTo(0));
                Assert.That(clock.IsRunning, Is.True);
            }
        }

        [Test]
        public void RaisesOncePerTick_InOrder_EvenWhenOneUpdateCoversSeveral()
        {
            var system = new NetworkTickSystem(30, 0, 0);
            using (var clock = new NetworkTickClock(system))
            {
                var seen = new List<long>();
                clock.Ticked += tick =>
                {
                    Assert.That(clock.CurrentTick, Is.EqualTo(tick), "CurrentTick holds the new tick when Ticked runs");
                    seen.Add(tick);
                };

                system.UpdateTick(1.0 / 30 + 1e-6, 1.0 / 30 + 1e-6);   // one tick
                system.UpdateTick(4.0 / 30 + 1e-6, 4.0 / 30 + 1e-6);   // three more in one update

                Assert.That(seen, Is.EqualTo(new long[] { 1, 2, 3, 4 }));
                Assert.That(clock.Time, Is.EqualTo(4 * clock.TickInterval).Within(1e-12));
                Assert.That(clock.NetworkTick, Is.EqualTo(system.ServerTime.Tick));
            }
        }

        [Test]
        public void NeverGoesBack_WhenNetworkTimeIsResynchronised()
        {
            var system = new NetworkTickSystem(30, 0, 0);
            using (var clock = new NetworkTickClock(system))
            {
                system.UpdateTick(5.0 / 30 + 1e-6, 5.0 / 30 + 1e-6);
                long before = clock.CurrentTick;

                system.Reset(1.0 / 30, 1.0 / 30);             // a client resync moves NGO's time back
                system.UpdateTick(2.0 / 30 + 1e-6, 2.0 / 30 + 1e-6);

                Assert.That(clock.CurrentTick, Is.EqualTo(before + 1), "our count keeps going up by one per tick");
            }
        }

        [Test]
        public void Dispose_StopsTicking()
        {
            var system = new NetworkTickSystem(30, 0, 0);
            var clock = new NetworkTickClock(system);
            int ticks = 0;
            clock.Ticked += tick => ticks++;

            system.UpdateTick(1.0 / 30 + 1e-6, 1.0 / 30 + 1e-6);
            clock.Dispose();
            clock.Dispose();
            system.UpdateTick(5.0 / 30 + 1e-6, 5.0 / 30 + 1e-6);

            Assert.That(ticks, Is.EqualTo(1));
            Assert.That(clock.IsRunning, Is.False);
            Assert.That(clock.CurrentTick, Is.EqualTo(1));
        }

        [Test]
        public void NullSystem_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new NetworkTickClock(null));
        }
    }
}
