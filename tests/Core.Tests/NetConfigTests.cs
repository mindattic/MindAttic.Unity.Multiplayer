using System;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class NetConfigTests
    {
        [Test]
        public void Defaults_AreAValidHostAt30Hz()
        {
            var config = new NetConfig();

            Assert.That(config.Topology, Is.EqualTo(Topology.Host));
            Assert.That(config.Transport, Is.EqualTo(TransportKind.Udp));
            Assert.That(config.TickRate, Is.EqualTo(30));
            Assert.That(config.Port, Is.EqualTo(7777));
            Assert.That(config.MaxPlayers, Is.EqualTo(NetConfig.DefaultMaxPlayers));
            Assert.That(config.Validate(), Is.Empty);
            Assert.That(config.IsValid, Is.True);
        }

        [Test]
        public void TickInterval_IsOneOverTickRate()
        {
            var config = new NetConfig { TickRate = 60 };
            Assert.That(config.TickInterval, Is.EqualTo(1.0 / 60).Within(1e-12));
        }

        [TestCase(Topology.Host, NetConfig.AnyAddress)]
        [TestCase(Topology.Server, NetConfig.AnyAddress)]
        [TestCase(Topology.Client, NetConfig.LoopbackAddress)]
        public void EffectiveAddress_DefaultsPerTopology(Topology topology, string expected)
        {
            var config = new NetConfig { Topology = topology, Address = "  " };
            Assert.That(config.EffectiveAddress, Is.EqualTo(expected));
        }

        [Test]
        public void EffectiveAddress_UsesTrimmedAddressWhenGiven()
        {
            var config = new NetConfig { Topology = Topology.Client, Address = " 10.0.0.5 " };
            Assert.That(config.EffectiveAddress, Is.EqualTo("10.0.0.5"));
        }

        [TestCase(-0.5)]
        [TestCase(double.NaN)]
        [TestCase(NetConfig.MaxReconnectGraceSeconds + 1)]
        public void Validate_RejectsReconnectGraceOutOfRange(double grace)
        {
            var config = new NetConfig { ReconnectGraceSeconds = grace };
            Assert.That(config.Validate(), Has.Some.Contains("ReconnectGraceSeconds"));
        }

        [Test]
        public void ReconnectGrace_DefaultsToAMinute_AndZeroIsAllowed()
        {
            Assert.That(new NetConfig().ReconnectGraceSeconds, Is.EqualTo(NetConfig.DefaultReconnectGraceSeconds));
            Assert.That(new NetConfig { ReconnectGraceSeconds = 0 }.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_RejectsPortZero()
        {
            var config = new NetConfig { Port = 0 };
            Assert.That(config.Validate(), Has.Some.Contains("Port"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(NetConfig.MaxTickRate + 1)]
        public void Validate_RejectsTickRateOutOfRange(int tickRate)
        {
            var config = new NetConfig { TickRate = tickRate };
            Assert.That(config.Validate(), Has.Some.Contains("TickRate"));
        }

        [TestCase(1)]
        [TestCase(NetConfig.MaxTickRate)]
        public void Validate_AcceptsTickRateBounds(int tickRate)
        {
            Assert.That(new NetConfig { TickRate = tickRate }.Validate(), Is.Empty);
        }

        [TestCase(0)]
        [TestCase(NetConfig.MaxMaxPlayers + 1)]
        public void Validate_RejectsMaxPlayersOutOfRange(int maxPlayers)
        {
            var config = new NetConfig { MaxPlayers = maxPlayers };
            Assert.That(config.Validate(), Has.Some.Contains("MaxPlayers"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Validate_RejectsEmptyGameVersion(string version)
        {
            var config = new NetConfig { GameVersion = version };
            Assert.That(config.Validate(), Has.Some.Contains("GameVersion"));
        }

        [TestCase("0.0.0.0")]
        [TestCase("::")]
        public void Validate_RejectsClientConnectingToWildcard(string address)
        {
            var config = new NetConfig { Topology = Topology.Client, Address = address };
            Assert.That(config.Validate(), Has.Some.Contains("wildcard"));
        }

        [Test]
        public void Validate_AllowsServerBindingWildcard()
        {
            var config = new NetConfig { Topology = Topology.Server, Address = "0.0.0.0" };
            Assert.That(config.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_RejectsAddressWithSpaces()
        {
            var config = new NetConfig { Topology = Topology.Client, Address = "my host" };
            Assert.That(config.Validate(), Has.Some.Contains("spaces"));
        }

        [Test]
        public void Validate_RejectsUndefinedEnums()
        {
            var config = new NetConfig { Topology = (Topology)42, Transport = (TransportKind)9 };
            var errors = config.Validate();
            Assert.That(errors, Has.Some.Contains("Topology"));
            Assert.That(errors, Has.Some.Contains("Transport"));
        }

        [Test]
        public void Validate_ReportsEveryProblemAtOnce()
        {
            var config = new NetConfig { Port = 0, TickRate = 0, MaxPlayers = 0, GameVersion = "" };
            Assert.That(config.Validate().Count, Is.EqualTo(4));
        }

        [Test]
        public void EnsureValid_ThrowsWithAllProblems()
        {
            var config = new NetConfig { Port = 0, TickRate = 0 };
            var ex = Assert.Throws<ArgumentException>(() => config.EnsureValid());
            Assert.That(ex.Message, Does.Contain("Port").And.Contain("TickRate"));
        }

        [Test]
        public void EnsureValid_DoesNotThrowWhenValid()
        {
            Assert.DoesNotThrow(() => new NetConfig { Topology = Topology.Client, Address = "192.168.1.20", Transport = TransportKind.WebSocket }.EnsureValid());
        }

        [Test]
        public void Clone_IsIndependentCopy()
        {
            var original = new NetConfig { Topology = Topology.Server, Port = 9000, GameVersion = "1.2", MaxPlayers = 4 };
            var copy = original.Clone();
            copy.Port = 9001;

            Assert.That(copy, Is.Not.SameAs(original));
            Assert.That(original.Port, Is.EqualTo(9000));
            Assert.That(copy.Topology, Is.EqualTo(Topology.Server));
            Assert.That(copy.GameVersion, Is.EqualTo("1.2"));
            Assert.That(copy.MaxPlayers, Is.EqualTo(4));
        }

        [Test]
        public void ToString_NamesTheEssentials()
        {
            var text = new NetConfig { Topology = Topology.Client, Address = "10.1.1.1", Port = 7778, GameVersion = "3" }.ToString();
            Assert.That(text, Does.Contain("Client").And.Contain("10.1.1.1:7778").And.Contain("30 Hz").And.Contain("version 3"));
        }

        [TestCase(Topology.Host, true, true)]
        [TestCase(Topology.Server, true, false)]
        [TestCase(Topology.Client, false, true)]
        public void TopologyExtensions_DescribeRoles(Topology topology, bool runsServer, bool hasLocalPlayer)
        {
            Assert.That(topology.RunsServer(), Is.EqualTo(runsServer));
            Assert.That(topology.HasLocalPlayer(), Is.EqualTo(hasLocalPlayer));
        }
    }
}
