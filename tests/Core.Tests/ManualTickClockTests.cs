using System;
using System.Collections.Generic;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>
    /// The ITickClock contract, checked against ManualTickClock. The adapter's NetworkTickClock (UM-T8)
    /// must keep the same contract.
    /// </summary>
    [TestFixture]
    public class ManualTickClockTests
    {
        [Test]
        public void DefaultsTo30HzAtTickZero()
        {
            ITickClock clock = new ManualTickClock();

            Assert.That(clock.TickRate, Is.EqualTo(30));
            Assert.That(clock.TickInterval, Is.EqualTo(1.0 / 30).Within(1e-12));
            Assert.That(clock.CurrentTick, Is.EqualTo(0));
            Assert.That(clock.Time, Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void RejectsTickRateBelowOne(int tickRate)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ManualTickClock(tickRate));
        }

        [Test]
        public void Step_RaisesTickedOncePerTickInOrder_AfterCurrentTickUpdates()
        {
            var clock = new ManualTickClock(10);
            var seen = new List<(long arg, long current)>();
            clock.Ticked += t => seen.Add((t, clock.CurrentTick));

            clock.Step();
            clock.Step(3);

            Assert.That(seen, Is.EqualTo(new[] { (1L, 1L), (2L, 2L), (3L, 3L), (4L, 4L) }));
            Assert.That(clock.Time, Is.EqualTo(0.4).Within(1e-12));
        }

        [Test]
        public void Step_Zero_DoesNothing_Negative_Throws()
        {
            var clock = new ManualTickClock();
            clock.Step(0);
            Assert.That(clock.CurrentTick, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Step(-1));
        }

        [Test]
        public void Advance_OneSecondAt30Hz_Is30Ticks_InFrameSizedPieces()
        {
            var clock = new ManualTickClock(30);
            int raised = 0;
            clock.Ticked += _ => raised++;

            int total = 0;
            for (int i = 0; i < 30; i++)
            {
                total += clock.Advance(1.0 / 30);
            }

            Assert.That(total, Is.EqualTo(30));
            Assert.That(raised, Is.EqualTo(30));
            Assert.That(clock.CurrentTick, Is.EqualTo(30));
            Assert.That(clock.Time, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void Advance_CarriesRemainderAcrossCalls()
        {
            var clock = new ManualTickClock(10); // 0.1 s per tick

            Assert.That(clock.Advance(0.06), Is.EqualTo(0));
            Assert.That(clock.Pending, Is.EqualTo(0.06).Within(1e-9));
            Assert.That(clock.Advance(0.06), Is.EqualTo(1));
            Assert.That(clock.Pending, Is.EqualTo(0.02).Within(1e-9));
        }

        [Test]
        public void Advance_LongFrame_CatchesUpTickByTick()
        {
            var clock = new ManualTickClock(30);
            var ticks = new List<long>();
            clock.Ticked += ticks.Add;

            Assert.That(clock.Advance(0.25), Is.EqualTo(7)); // 7.5 intervals

            Assert.That(ticks, Is.EqualTo(new long[] { 1, 2, 3, 4, 5, 6, 7 }));
            Assert.That(clock.Pending, Is.EqualTo(0.25 - 7.0 / 30).Within(1e-9));
        }

        [Test]
        public void Advance_ManySmallFrames_NeverDrifts()
        {
            var clock = new ManualTickClock(30);
            for (int i = 0; i < 60 * 600; i++) // ten minutes at 60 fps
            {
                clock.Advance(1.0 / 60);
            }

            Assert.That(clock.CurrentTick, Is.EqualTo(30 * 600));
        }

        [TestCase(-0.1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Advance_RejectsBadElapsedTime(double seconds)
        {
            var clock = new ManualTickClock();
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(seconds));
            Assert.That(clock.CurrentTick, Is.EqualTo(0));
            Assert.That(clock.Pending, Is.EqualTo(0));
        }

        [Test]
        public void Advance_RejectsAbsurdElapsedTime_WithoutChangingState()
        {
            var clock = new ManualTickClock(NetConfig.MaxTickRate);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(1e12));
            Assert.That(clock.CurrentTick, Is.EqualTo(0));
            Assert.That(clock.Pending, Is.EqualTo(0));
        }

        [Test]
        public void Advance_Zero_DoesNothing()
        {
            var clock = new ManualTickClock();
            Assert.That(clock.Advance(0), Is.EqualTo(0));
            Assert.That(clock.CurrentTick, Is.EqualTo(0));
        }
    }
}
