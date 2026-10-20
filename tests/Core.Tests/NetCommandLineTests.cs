using System;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class NetCommandLineTests
    {
        [Test]
        public void Apply_HeadlessServerLine_SetsEverySetting()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[]
            {
                "/srv/server/Game.x86_64", "-batchmode", "-nographics", "-logFile", "-",
                "-mode", "server", "-port", "7790", "-transport", "websocket",
                "-tickrate", "60", "-maxplayers", "16", "-address", "10.0.0.5",
            });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Headless, Is.True);
            Assert.That(result.TopologySpecified, Is.True);
            Assert.That(config.Topology, Is.EqualTo(Topology.Server));
            Assert.That(config.Port, Is.EqualTo(7790));
            Assert.That(config.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(config.TickRate, Is.EqualTo(60));
            Assert.That(config.MaxPlayers, Is.EqualTo(16));
            Assert.That(config.Address, Is.EqualTo("10.0.0.5"));
            Assert.That(config.IsValid, Is.True);
        }

        [Test]
        public void Apply_NoArguments_LeavesDefaultsAndReportsNothing()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "Game.exe" });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.TopologySpecified, Is.False);
            Assert.That(result.Headless, Is.False);
            Assert.That(config.Topology, Is.EqualTo(Topology.Host));
            Assert.That(config.Port, Is.EqualTo(NetConfig.DefaultPort));
            Assert.That(config.Transport, Is.EqualTo(TransportKind.Udp));
            Assert.That(config.TickRate, Is.EqualTo(NetConfig.DefaultTickRate));
        }

        [Test]
        public void Apply_IgnoresCaseAndAcceptsInlineValues()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-MODE=Client", "-Port=9000", "-TRANSPORT=ws", "-ip=192.168.1.20" });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(config.Topology, Is.EqualTo(Topology.Client));
            Assert.That(config.Port, Is.EqualTo(9000));
            Assert.That(config.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(config.Address, Is.EqualTo("192.168.1.20"));
        }

        [Test]
        public void Apply_LaterRepeatWins()
        {
            var config = new NetConfig();
            NetCommandLine.Apply(config, new[] { "-port", "7000", "-port", "7001" });
            Assert.That(config.Port, Is.EqualTo(7001));
        }

        [Test]
        public void Apply_UnknownArgumentsAreIgnored()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-screen-width", "800", "-force-d3d11", "-port", "7100" });

            Assert.That(result.Errors, Is.Empty);
            Assert.That(config.Port, Is.EqualTo(7100));
        }

        [TestCase("-port", "0")]
        [TestCase("-port", "70000")]
        [TestCase("-port", "abc")]
        [TestCase("-transport", "tcp")]
        [TestCase("-mode", "dedicated")]
        [TestCase("-tickrate", "0")]
        [TestCase("-tickrate", "500")]
        [TestCase("-maxplayers", "0")]
        public void Apply_BadValue_IsReportedAndLeavesSettingUnchanged(string name, string value)
        {
            var config = new NetConfig();
            NetConfig before = config.Clone();

            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { name, value });

            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Does.StartWith(name));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.TopologySpecified, Is.False);
            Assert.That(config.ToString(), Is.EqualTo(before.ToString()));
        }

        [Test]
        public void Apply_MissingValueBeforeAnotherSwitch_IsReportedAndTheSwitchStillApplies()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-port", "-transport", "websocket" });

            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0], Does.Contain("-port"));
            Assert.That(config.Port, Is.EqualTo(NetConfig.DefaultPort));
            Assert.That(config.Transport, Is.EqualTo(TransportKind.WebSocket));
        }

        [Test]
        public void Apply_MissingValueAtEnd_IsReported()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-mode" });

            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.TopologySpecified, Is.False);
        }

        [Test]
        public void Apply_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => NetCommandLine.Apply(null, new string[0]));
            Assert.Throws<ArgumentNullException>(() => NetCommandLine.Apply(new NetConfig(), null));
        }

        [TestCase(new[] { "Game.exe", "-batchmode" }, true)]
        [TestCase(new[] { "Game.exe", "-NOGRAPHICS" }, true)]
        [TestCase(new[] { "Game.exe", "-port", "7777" }, false)]
        public void IsHeadless_LooksForUnitysSwitches(string[] args, bool expected)
        {
            Assert.That(NetCommandLine.IsHeadless(args), Is.EqualTo(expected));
        }

        [TestCase("host", Topology.Host)]
        [TestCase(" Server ", Topology.Server)]
        [TestCase("CLIENT", Topology.Client)]
        public void TryParseTopology_AcceptsTheThreeNames(string text, Topology expected)
        {
            Topology topology;
            Assert.That(NetCommandLine.TryParseTopology(text, out topology), Is.True);
            Assert.That(topology, Is.EqualTo(expected));
        }

        [TestCase("udp", TransportKind.Udp)]
        [TestCase("WebSocket", TransportKind.WebSocket)]
        [TestCase("ws", TransportKind.WebSocket)]
        public void TryParseTransport_AcceptsUdpAndWebSocket(string text, TransportKind expected)
        {
            TransportKind transport;
            Assert.That(NetCommandLine.TryParseTransport(text, out transport), Is.True);
            Assert.That(transport, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("quic")]
        public void TryParseTransport_RejectsAnythingElse(string text)
        {
            TransportKind transport;
            Assert.That(NetCommandLine.TryParseTransport(text, out transport), Is.False);
        }
    }
}
