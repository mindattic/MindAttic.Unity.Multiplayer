using System;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class RendezvousProtocolTests
    {
        [Test]
        public void Paths_UseTheCanonicalCode()
        {
            JoinCode code = JoinCode.Parse(JoinCodeFormat.Format(JoinCode.Generate().Value).ToLowerInvariant());

            Assert.That(RendezvousProtocol.ResolvePath(code), Is.EqualTo("/v1/resolve/" + code.Value));
            Assert.That(RendezvousProtocol.RegistrationPath(code), Is.EqualTo("/v1/register/" + code.Value));
        }

        [Test]
        public void Paths_RejectTheEmptyCode()
        {
            Assert.Throws<ArgumentException>(() => RendezvousProtocol.ResolvePath(JoinCode.Empty));
            Assert.Throws<ArgumentException>(() => RendezvousProtocol.RegistrationPath(JoinCode.Empty));
        }

        [TestCase(TransportKind.Udp, "udp")]
        [TestCase(TransportKind.WebSocket, "websocket")]
        public void Transport_RoundTrips(TransportKind kind, string wire)
        {
            Assert.That(RendezvousProtocol.ToWire(kind), Is.EqualTo(wire));
            Assert.That(RendezvousProtocol.TryParseTransport(wire.ToUpperInvariant(), out TransportKind parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(kind));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Transport_MissingMeansUdp(string wire)
        {
            Assert.That(RendezvousProtocol.TryParseTransport(wire, out TransportKind parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(TransportKind.Udp));
        }

        [Test]
        public void Transport_Unknown_IsRejected()
        {
            Assert.That(RendezvousProtocol.TryParseTransport("tcp", out _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => RendezvousProtocol.ToWire((TransportKind)99));
        }
    }
}
