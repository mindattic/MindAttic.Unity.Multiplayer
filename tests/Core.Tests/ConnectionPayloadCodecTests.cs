using System;
using System.Collections.Generic;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class ConnectionPayloadCodecTests
    {
        internal static ConnectionPayload Sample(string version = "1.2.0", string token = "3f2a9c1e-77b4-4d0e-9a51-0c6d2b8e4f10", string name = "Ryan")
        {
            return new ConnectionPayload(version, new ProtocolHash(0x0102030405060708UL), token, name, new PlayerColor(10, 20, 30, 40));
        }

        [Test]
        public void EncodeThenDecode_RoundTripsEveryField()
        {
            ConnectionPayload original = new ConnectionPayload("1.2.0", ProtocolHash.Compute("A.B()"), "tok-1", "Zoë 🎮", new PlayerColor(1, 2, 3, 4));

            PayloadDecodeResult result = ConnectionPayloadCodec.TryDecode(ConnectionPayloadCodec.Encode(original));

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.FormatVersion, Is.EqualTo(ConnectionPayloadCodec.CurrentFormatVersion));
            Assert.That(result.Payload.GameVersion, Is.EqualTo(original.GameVersion));
            Assert.That(result.Payload.ProtocolHash, Is.EqualTo(original.ProtocolHash));
            Assert.That(result.Payload.PlayerToken, Is.EqualTo(original.PlayerToken));
            Assert.That(result.Payload.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(result.Payload.Color, Is.EqualTo(original.Color));
            Assert.That(result.Message, Is.Empty);
        }

        [Test]
        public void Encode_WritesTheDocumentedV2Layout()
        {
            var payload = new ConnectionPayload("1", new ProtocolHash(0x0102030405060708UL), "t", "N", new PlayerColor(0xAA, 0xBB, 0xCC, 0xDD), "p");

            byte[] expected =
            {
                0x4D, 0x41, 0x02,                               // magic "MA", format 2
                0x01, (byte)'1',                                // game version
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // protocol hash, little-endian
                0x01, (byte)'t',                                // player token
                0x01, (byte)'N',                                // display name
                0xAA, 0xBB, 0xCC, 0xDD,                         // colour RGBA
                0x01, (byte)'p',                                // server password
            };

            Assert.That(ConnectionPayloadCodec.Encode(payload), Is.EqualTo(expected));
        }

        [Test]
        public void Encode_LargestValidPayload_FitsTheDecodeLimit()
        {
            var payload = new ConnectionPayload(
                new string('€', ConnectionPayload.MaxGameVersionLength),
                ProtocolHash.Compute("x"),
                new string('t', ConnectionPayload.MaxPlayerTokenLength),
                new string('€', ConnectionPayload.MaxDisplayNameLength),
                PlayerColor.White);

            byte[] bytes = ConnectionPayloadCodec.Encode(payload);

            Assert.That(bytes.Length, Is.LessThanOrEqualTo(ConnectionPayloadCodec.MaxEncodedLength));
            Assert.That(ConnectionPayloadCodec.TryDecode(bytes).Success, Is.True);
        }

        [Test]
        public void Encode_RefusesInvalidPayload()
        {
            Assert.Throws<ArgumentException>(() => ConnectionPayloadCodec.Encode(Sample(name: "  ")));
            Assert.Throws<ArgumentNullException>(() => ConnectionPayloadCodec.Encode(null));
        }

        [Test]
        public void TryDecode_NullOrEmpty_IsEmpty()
        {
            Assert.That(ConnectionPayloadCodec.TryDecode(null).Error, Is.EqualTo(PayloadDecodeError.Empty));
            Assert.That(ConnectionPayloadCodec.TryDecode(new byte[0]).Error, Is.EqualTo(PayloadDecodeError.Empty));
        }

        [Test]
        public void TryDecode_OverLimit_IsTooLarge()
        {
            PayloadDecodeResult result = ConnectionPayloadCodec.TryDecode(new byte[ConnectionPayloadCodec.MaxEncodedLength + 1]);
            Assert.That(result.Error, Is.EqualTo(PayloadDecodeError.TooLarge));
            Assert.That(result.Payload, Is.Null);
        }

        [Test]
        public void TryDecode_WrongMagic_IsBadMagic()
        {
            byte[] bytes = ConnectionPayloadCodec.Encode(Sample());
            bytes[0] = (byte)'X';
            Assert.That(ConnectionPayloadCodec.TryDecode(bytes).Error, Is.EqualTo(PayloadDecodeError.BadMagic));
        }

        [Test]
        public void TryDecode_NewerFormat_IsUnsupportedButStillReportsGameVersion()
        {
            byte[] bytes = ConnectionPayloadCodec.Encode(Sample(version: "2.0.0"));
            bytes[2] = 3;

            PayloadDecodeResult result = ConnectionPayloadCodec.TryDecode(bytes);

            Assert.That(result.Error, Is.EqualTo(PayloadDecodeError.UnsupportedFormatVersion));
            Assert.That(result.FormatVersion, Is.EqualTo(3));
            Assert.That(result.GameVersion, Is.EqualTo("2.0.0"));
        }

        [Test]
        public void TryDecode_EveryTruncation_FailsWithoutThrowing()
        {
            byte[] full = ConnectionPayloadCodec.Encode(Sample());
            for (int length = 1; length < full.Length; length++)
            {
                byte[] cut = new byte[length];
                Array.Copy(full, cut, length);

                PayloadDecodeResult result = ConnectionPayloadCodec.TryDecode(cut);

                Assert.That(result.Success, Is.False, "length " + length);
                Assert.That(result.Error, Is.EqualTo(PayloadDecodeError.Truncated), "length " + length);
                Assert.That(result.Message, Is.Not.Empty);
            }
        }

        [Test]
        public void TryDecode_ExtraBytes_IsTrailingBytes()
        {
            byte[] full = ConnectionPayloadCodec.Encode(Sample());
            byte[] longer = new byte[full.Length + 1];
            Array.Copy(full, longer, full.Length);
            Assert.That(ConnectionPayloadCodec.TryDecode(longer).Error, Is.EqualTo(PayloadDecodeError.TrailingBytes));
        }

        [Test]
        public void TryDecode_InvalidUtf8_IsInvalidText()
        {
            byte[] bytes = { 0x4D, 0x41, 0x01, 0x02, 0xC3, 0x28 };
            Assert.That(ConnectionPayloadCodec.TryDecode(bytes).Error, Is.EqualTo(PayloadDecodeError.InvalidText));
        }

        [Test]
        public void TryDecode_WellFormedButInvalidField_IsInvalidField()
        {
            // Hand-built: valid layout, but the player token contains a space.
            var bytes = new List<byte> { 0x4D, 0x41, 0x01, 0x01, (byte)'1' };
            bytes.AddRange(new byte[8]);
            bytes.AddRange(new byte[] { 0x03, (byte)'a', (byte)' ', (byte)'b' });
            bytes.AddRange(new byte[] { 0x01, (byte)'N' });
            bytes.AddRange(new byte[] { 1, 2, 3, 4 });

            PayloadDecodeResult result = ConnectionPayloadCodec.TryDecode(bytes.ToArray());

            Assert.That(result.Error, Is.EqualTo(PayloadDecodeError.InvalidField));
            Assert.That(result.Message, Does.Contain("PlayerToken"));
            Assert.That(result.GameVersion, Is.EqualTo("1"));
        }

        [Test]
        public void TryDecode_RandomBytes_NeverThrows()
        {
            var random = new Random(7);
            for (int i = 0; i < 2000; i++)
            {
                byte[] bytes = new byte[random.Next(0, 80)];
                random.NextBytes(bytes);
                if (bytes.Length > 2 && i % 2 == 0)
                {
                    bytes[0] = ConnectionPayloadCodec.Magic0;
                    bytes[1] = ConnectionPayloadCodec.Magic1;
                    bytes[2] = ConnectionPayloadCodec.CurrentFormatVersion;
                }

                Assert.DoesNotThrow(() => ConnectionPayloadCodec.TryDecode(bytes));
            }
        }
    }

    [TestFixture]
    public class ConnectionPayloadTests
    {
        [Test]
        public void Sample_IsValid()
        {
            Assert.That(ConnectionPayloadCodecTests.Sample().Validate(), Is.Empty);
        }

        [Test]
        public void Constructor_TurnsNullsIntoEmptyStrings()
        {
            var payload = new ConnectionPayload(null, ProtocolHash.None, null, null, PlayerColor.White);
            Assert.That(payload.GameVersion, Is.Empty);
            Assert.That(payload.PlayerToken, Is.Empty);
            Assert.That(payload.DisplayName, Is.Empty);
            Assert.That(payload.Validate(), Has.Count.EqualTo(3));
        }

        [TestCase("", "GameVersion")]
        [TestCase("   ", "GameVersion")]
        [TestCase("1.0\n", "GameVersion")]
        public void Validate_RejectsBadGameVersion(string version, string field)
        {
            Assert.That(ConnectionPayloadCodecTests.Sample(version: version).Validate(), Has.Some.Contains(field));
        }

        [Test]
        public void Validate_RejectsOverlongFields()
        {
            Assert.That(ConnectionPayloadCodecTests.Sample(version: new string('1', ConnectionPayload.MaxGameVersionLength + 1)).Validate(), Has.Some.Contains("GameVersion"));
            Assert.That(ConnectionPayloadCodecTests.Sample(token: new string('t', ConnectionPayload.MaxPlayerTokenLength + 1)).Validate(), Has.Some.Contains("PlayerToken"));
            Assert.That(ConnectionPayloadCodecTests.Sample(name: new string('n', ConnectionPayload.MaxDisplayNameLength + 1)).Validate(), Has.Some.Contains("DisplayName"));
        }

        [TestCase("")]
        [TestCase("has space")]
        [TestCase("té")]
        [TestCase("tab\t")]
        public void Validate_RejectsBadPlayerToken(string token)
        {
            Assert.That(ConnectionPayloadCodecTests.Sample(token: token).Validate(), Has.Some.Contains("PlayerToken"));
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase(" Ryan")]
        [TestCase("Ryan ")]
        [TestCase("Ry\u0000an")]
        public void Validate_RejectsBadDisplayName(string name)
        {
            Assert.That(ConnectionPayloadCodecTests.Sample(name: name).Validate(), Has.Some.Contains("DisplayName"));
        }

        [Test]
        public void PlayerColor_PacksAsRgba32()
        {
            var color = new PlayerColor(0x12, 0x34, 0x56, 0x78);
            Assert.That(color.ToRgba32(), Is.EqualTo(0x12345678u));
            Assert.That(PlayerColor.FromRgba32(0x12345678u), Is.EqualTo(color));
            Assert.That(color.ToString(), Is.EqualTo("#12345678"));
            Assert.That(new PlayerColor(1, 2, 3).A, Is.EqualTo(255));
        }
    }
}
