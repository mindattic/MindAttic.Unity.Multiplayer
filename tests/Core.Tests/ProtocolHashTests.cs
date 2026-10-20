using System;
using System.Reflection;
using System.Text;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

// Stands in for the attribute the generator emits into a game assembly.
[assembly: NetProtocol(MindAttic.Unity.Multiplayer.Core.Tests.TestProtocol.Hash, MindAttic.Unity.Multiplayer.Core.Tests.TestProtocol.SignatureCount)]

namespace MindAttic.Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class ProtocolHashTests
    {
        [Test]
        public void Fnv1a64_MatchesPublishedVectors()
        {
            // The generator reimplements this; pin it to the reference FNV-1a 64 test vectors.
            Assert.That(ProtocolHash.Fnv1a64(new byte[0]), Is.EqualTo(0xcbf29ce484222325UL));
            Assert.That(ProtocolHash.Fnv1a64(Encoding.UTF8.GetBytes("a")), Is.EqualTo(0xaf63dc4c8601ec8cUL));
            Assert.That(ProtocolHash.Fnv1a64(Encoding.UTF8.GetBytes("foobar")), Is.EqualTo(0x85944171f73967e8UL));
        }

        [Test]
        public void Compute_IsFnvOfDomainThenSortedSignatures()
        {
            ProtocolHash hash = ProtocolHash.Compute("b.Move(Vector2)", "a.Jump()");
            ulong expected = ProtocolHash.Fnv1a64(Encoding.UTF8.GetBytes(ProtocolHash.Domain + "\na.Jump()\nb.Move(Vector2)"));
            Assert.That(hash.Value, Is.EqualTo(expected));
        }

        [Test]
        public void Compute_IgnoresOrderDuplicatesWhitespaceAndEmpties()
        {
            ProtocolHash a = ProtocolHash.Compute("X.A(int)", "X.B(float)");
            ProtocolHash b = ProtocolHash.Compute(" X.B(float) ", "", null, "X.A(int)", "X.A(int)");
            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void Compute_ChangesWhenASignatureChanges()
        {
            Assert.That(ProtocolHash.Compute("X.A(int)"), Is.Not.EqualTo(ProtocolHash.Compute("X.A(long)")));
        }

        [Test]
        public void Compute_OfNothing_IsNotNone()
        {
            ProtocolHash hash = ProtocolHash.Compute(new string[0]);
            Assert.That(hash.IsNone, Is.False);
            Assert.That(ProtocolHash.None.IsNone, Is.True);
            Assert.That(default(ProtocolHash), Is.EqualTo(ProtocolHash.None));
        }

        [Test]
        public void Compute_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => ProtocolHash.Compute((string[])null));
        }

        [Test]
        public void ToString_RoundTripsThroughParse()
        {
            ProtocolHash hash = new ProtocolHash(0x00ab12cd34ef5678UL);
            Assert.That(hash.ToString(), Is.EqualTo("00ab12cd34ef5678"));
            Assert.That(ProtocolHash.Parse("00AB12CD34EF5678"), Is.EqualTo(hash));
            Assert.That(ProtocolHash.Parse("0x00ab12cd34ef5678"), Is.EqualTo(hash));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("00ab12cd34ef567")]
        [TestCase("00ab12cd34ef56789")]
        [TestCase("00ab12cd34ef567g")]
        public void TryParse_RejectsAnythingButSixteenHexDigits(string text)
        {
            ProtocolHash hash;
            Assert.That(ProtocolHash.TryParse(text, out hash), Is.False);
            Assert.That(hash, Is.EqualTo(ProtocolHash.None));
        }

        [Test]
        public void Combine_SkipsNoneAndReturnsSinglePartUnchanged()
        {
            ProtocolHash a = ProtocolHash.Compute("A");
            Assert.That(ProtocolHash.Combine(new ProtocolHash[0]), Is.EqualTo(ProtocolHash.None));
            Assert.That(ProtocolHash.Combine(new[] { ProtocolHash.None, a }), Is.EqualTo(a));
        }

        [Test]
        public void Combine_IsOrderIndependentAndDistinctFromParts()
        {
            ProtocolHash a = ProtocolHash.Compute("A");
            ProtocolHash b = ProtocolHash.Compute("B");
            ProtocolHash ab = ProtocolHash.Combine(new[] { a, b });
            Assert.That(ProtocolHash.Combine(new[] { b, a }), Is.EqualTo(ab));
            Assert.That(ab, Is.Not.EqualTo(a));
            Assert.That(ab, Is.Not.EqualTo(b));
        }

        [Test]
        public void FromAssemblies_ReadsTheGeneratorAttribute()
        {
            // This test assembly carries [assembly: NetProtocol(...)] below, standing in for generated code.
            ProtocolHash hash = ProtocolHash.FromAssemblies(typeof(ProtocolHashTests).Assembly);
            Assert.That(hash, Is.EqualTo(ProtocolHash.Parse(TestProtocol.Hash)));
        }

        [Test]
        public void FromAssemblies_WithoutAttribute_IsNone()
        {
            Assert.That(ProtocolHash.FromAssemblies(typeof(object).Assembly), Is.EqualTo(ProtocolHash.None));
        }

        [Test]
        public void FromAssemblies_CombinesAndSkipsAssembliesWithoutAttribute()
        {
            Assembly tests = typeof(ProtocolHashTests).Assembly;
            Assert.That(ProtocolHash.FromAssemblies(tests, typeof(object).Assembly, null),
                Is.EqualTo(ProtocolHash.Parse(TestProtocol.Hash)));
        }

        [Test]
        public void NetProtocolAttribute_KeepsSignatureCount()
        {
            var attribute = (NetProtocolAttribute)Attribute.GetCustomAttribute(typeof(ProtocolHashTests).Assembly, typeof(NetProtocolAttribute));
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.SignatureCount, Is.EqualTo(TestProtocol.SignatureCount));
        }
    }

    internal static class TestProtocol
    {
        public const string Hash = "1234567890abcdef";
        public const int SignatureCount = 3;
    }
}

