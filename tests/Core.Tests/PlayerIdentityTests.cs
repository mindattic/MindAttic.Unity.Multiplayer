using System;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class PlayerIdentityTests
    {
        [Test]
        public void CreateNew_MakesAFreshValidToken()
        {
            PlayerIdentity a = PlayerIdentity.CreateNew("Ryan", new PlayerColor(10, 20, 30));
            PlayerIdentity b = PlayerIdentity.CreateNew();

            Assert.That(a.PlayerToken, Has.Length.EqualTo(32));
            Assert.That(a.PlayerToken, Is.Not.EqualTo(b.PlayerToken));
            Assert.That(a.DisplayName, Is.EqualTo("Ryan"));
            Assert.That(b.DisplayName, Is.EqualTo(PlayerIdentity.DefaultDisplayName));
            Assert.That(b.Color, Is.EqualTo(PlayerColor.White));
        }

        [Test]
        public void ToPayload_CarriesTheIdentityAndRoundTrips()
        {
            var me = new PlayerIdentity("tok-1", "Ryan", new PlayerColor(1, 2, 3));
            ConnectionPayload payload = me.ToPayload("2.0", new ProtocolHash(7));

            PayloadDecodeResult decoded = ConnectionPayloadCodec.TryDecode(ConnectionPayloadCodec.Encode(payload));
            Assert.That(decoded.Success, Is.True, decoded.Message);
            Assert.That(decoded.Payload.PlayerToken, Is.EqualTo("tok-1"));
            Assert.That(decoded.Payload.GameVersion, Is.EqualTo("2.0"));
            Assert.That(decoded.Payload.ProtocolHash, Is.EqualTo(new ProtocolHash(7)));
        }

        [Test]
        public void With_KeepsTheToken()
        {
            var me = new PlayerIdentity("tok-1", "Ryan", PlayerColor.White);
            PlayerIdentity renamed = me.With(displayName: "R");
            Assert.That(renamed.PlayerToken, Is.EqualTo("tok-1"));
            Assert.That(renamed.DisplayName, Is.EqualTo("R"));
        }

        [TestCase("", "Ryan")]
        [TestCase("has space", "Ryan")]
        [TestCase("tok", "")]
        [TestCase("tok", " padded ")]
        public void InvalidFields_Throw(string token, string name)
        {
            Assert.Throws<ArgumentException>(() => new PlayerIdentity(token, name, PlayerColor.White));
        }
    }
}
