using System;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class ServerPasswordTests
    {
        private static readonly ProtocolHash Hash = new ProtocolHash(42UL);

        private static ConnectionPayload Payload(string password)
        {
            return new ConnectionPayload("1.0", Hash, "tok-1", "Ryan", PlayerColor.White, password);
        }

        private static ConnectionApprovalValidator Server(string password)
        {
            var config = new NetConfig { Topology = Topology.Server, GameVersion = "1.0", MaxPlayers = 4, Password = password };
            return ConnectionApprovalValidator.FromConfig(config, Hash);
        }

        [Test]
        public void Codec_RoundTripsThePassword()
        {
            PayloadDecodeResult result = ConnectionPayloadCodec.TryDecode(ConnectionPayloadCodec.Encode(Payload("hunter2")));

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.FormatVersion, Is.EqualTo(2));
            Assert.That(result.Payload.Password, Is.EqualTo("hunter2"));
        }

        [Test]
        public void Codec_StillReadsFormat1AsNoPassword()
        {
            byte[] v2 = ConnectionPayloadCodec.Encode(Payload(null));
            // Format 1 is format 2 without the trailing password string (here one zero length byte).
            byte[] v1 = new byte[v2.Length - 1];
            Array.Copy(v2, v1, v1.Length);
            v1[2] = 1;

            PayloadDecodeResult result = ConnectionPayloadCodec.TryDecode(v1);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.FormatVersion, Is.EqualTo(1));
            Assert.That(result.Payload.Password, Is.Empty);
        }

        [Test]
        public void Payload_RejectsAnOverlongPassword_AndNeverPrintsIt()
        {
            var payload = Payload(new string('p', ConnectionPayload.MaxPasswordLength + 1));
            Assert.That(payload.Validate(), Has.Some.Contains("Password"));
            Assert.That(Payload("hunter2").ToString(), Does.Not.Contain("hunter2"));
        }

        [Test]
        public void ServerWithoutPassword_LetsAnyoneIn()
        {
            Assert.That(Server(null).Validate(Payload("whatever"), 0).Approved, Is.True);
            Assert.That(Server(string.Empty).Validate(Payload(null), 0).Approved, Is.True);
        }

        [Test]
        public void ServerWithPassword_NeedsTheExactOne()
        {
            ConnectionApprovalValidator server = Server("hunter2");

            Assert.That(server.Validate(Payload("hunter2"), 0).Approved, Is.True);

            ApprovalDecision none = server.Validate(Payload(null), 0);
            Assert.That(none.Rejection, Is.EqualTo(ApprovalRejection.WrongPassword));
            Assert.That(none.Message, Does.Contain("needs a password"));

            ApprovalDecision wrong = server.Validate(Payload("Hunter2"), 0);
            Assert.That(wrong.Rejection, Is.EqualTo(ApprovalRejection.WrongPassword));
            Assert.That(wrong.Message, Does.Contain("Wrong"));
            Assert.That(server.Validate(Payload("hunter22"), 0).Rejection, Is.EqualTo(ApprovalRejection.WrongPassword));
            Assert.That(server.Validate(Payload("hunter"), 0).Rejection, Is.EqualTo(ApprovalRejection.WrongPassword));
        }

        [Test]
        public void WrongPassword_TravelsInTheWireReason()
        {
            ApprovalDecision decision = Server("pw").Validate(ConnectionPayloadCodec.Encode(Payload("nope")), 0);

            Assert.That(ApprovalDecision.TryParseWireReason(decision.ToWireReason(), out ApprovalRejection rejection, out string message), Is.True);
            Assert.That(rejection, Is.EqualTo(ApprovalRejection.WrongPassword));
            Assert.That(message, Is.EqualTo("Wrong server password."));
        }

        [Test]
        public void PasswordCheck_ComesAfterTheProtocolAndBeforeFull()
        {
            ConnectionApprovalValidator server = Server("pw");
            var otherProtocol = new ConnectionPayload("1.0", new ProtocolHash(7UL), "tok-1", "Ryan", PlayerColor.White, "nope");

            Assert.That(server.Validate(otherProtocol, 0).Rejection, Is.EqualTo(ApprovalRejection.ProtocolMismatch));
            Assert.That(server.Validate(Payload("nope"), 4).Rejection, Is.EqualTo(ApprovalRejection.WrongPassword));
        }

        [Test]
        public void IdentityPayload_CarriesThePassword()
        {
            PlayerIdentity identity = PlayerIdentity.CreateNew("Ryan", PlayerColor.White);
            Assert.That(identity.ToPayload("1.0", Hash, "pw").Password, Is.EqualTo("pw"));
            Assert.That(identity.ToPayload("1.0", Hash).Password, Is.Empty);
        }

        [Test]
        public void NetConfig_NeverPrintsThePassword_AndValidatesNameAndPassword()
        {
            var config = new NetConfig { ServerName = "Friday", Password = "hunter2" };
            Assert.That(config.ToString(), Does.Contain("'Friday'"));
            Assert.That(config.ToString(), Does.Not.Contain("hunter2"));
            Assert.That(config.IsValid, Is.True);

            config.ServerName = new string('n', NetConfig.MaxServerNameLength + 1);
            Assert.That(config.Validate(), Has.Some.Contains("ServerName"));
            config.ServerName = "ok";
            config.Password = "a\nb";
            Assert.That(config.Validate(), Has.Some.Contains("Password"));
        }
    }
}
