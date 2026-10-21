using System;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class NetLaunchTests
    {
        private static NetCommandLineResult Line(params string[] args)
        {
            return NetCommandLine.Apply(new NetConfig(), args);
        }

        [Test]
        public void ModeOnTheCommandLine_AlwaysStartsAsThatMode()
        {
            var config = new NetConfig { Topology = Topology.Client };
            NetCommandLineResult line = NetCommandLine.Apply(config, new[] { "-batchmode", "-mode", "host" });

            Assert.That(NetLaunch.ShouldStart(config, line, headless: true, autoStart: false, serveWhenHeadless: true), Is.True);
            Assert.That(config.Topology, Is.EqualTo(Topology.Host), "an explicit -mode beats the headless default");
        }

        [Test]
        public void HeadlessWithoutMode_StartsAsDedicatedServer()
        {
            var config = new NetConfig { Topology = Topology.Host };

            Assert.That(NetLaunch.ShouldStart(config, Line("-batchmode"), headless: true, autoStart: false, serveWhenHeadless: true), Is.True);
            Assert.That(config.Topology, Is.EqualTo(Topology.Server));
        }

        [Test]
        public void HeadlessWithServingOff_FollowsAutoStart()
        {
            var config = new NetConfig { Topology = Topology.Client };

            Assert.That(NetLaunch.ShouldStart(config, Line(), headless: true, autoStart: false, serveWhenHeadless: false), Is.False);
            Assert.That(NetLaunch.ShouldStart(config, Line(), headless: true, autoStart: true, serveWhenHeadless: false), Is.True);
            Assert.That(config.Topology, Is.EqualTo(Topology.Client));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WindowedWithoutMode_StartsOnlyWithAutoStart(bool autoStart)
        {
            var config = new NetConfig { Topology = Topology.Host };

            Assert.That(NetLaunch.ShouldStart(config, Line(), headless: false, autoStart: autoStart, serveWhenHeadless: true), Is.EqualTo(autoStart));
            Assert.That(config.Topology, Is.EqualTo(Topology.Host));
        }

        [Test]
        public void NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => NetLaunch.ShouldStart(null, Line(), false, false, false));
            Assert.Throws<ArgumentNullException>(() => NetLaunch.ShouldStart(new NetConfig(), null, false, false, false));
        }
    }
}
