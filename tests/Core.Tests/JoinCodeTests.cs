using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class JoinCodeTests
    {
        // A fixed code used across tests: data "ABCD2345XY" plus its check character.
        private static readonly JoinCode Sample = JoinCode.FromData("ABCD2345XY");

        [Test]
        public void Alphabet_Is32DistinctSymbolsWithoutLookAlikes()
        {
            string alphabet = JoinCodeFormat.Alphabet;

            Assert.That(alphabet.Length, Is.EqualTo(32));
            Assert.That(alphabet.Distinct().Count(), Is.EqualTo(32));
            Assert.That(alphabet, Does.Not.Contain("0").And.Not.Contain("O").And.Not.Contain("1").And.Not.Contain("I"));
            Assert.That(alphabet.All(c => char.IsUpper(c) || char.IsDigit(c)), Is.True);
            Assert.That(1 << JoinCodeFormat.BitsPerCharacter, Is.EqualTo(alphabet.Length));
        }

        [Test]
        public void DefaultCode_Has50BitsOfEntropy()
        {
            Assert.That(JoinCodeFormat.EntropyBits(JoinCodeFormat.DefaultDataLength), Is.EqualTo(40));
        }

        [Test]
        public void Generate_ProducesValidCodesOfTheRequestedLength()
        {
            for (int length = JoinCodeFormat.MinDataLength; length <= JoinCodeFormat.MaxDataLength; length++)
            {
                JoinCode code = JoinCode.Generate(length);

                Assert.That(code.IsEmpty, Is.False);
                Assert.That(code.Data.Length, Is.EqualTo(length));
                Assert.That(code.Length, Is.EqualTo(length + 1));
                Assert.That(code.Value.All(JoinCodeFormat.IsAlphabetCharacter), Is.True);
                Assert.That(JoinCodeFormat.HasValidCheck(code.Value), Is.True);
                Assert.That(JoinCode.Parse(code.ToString()), Is.EqualTo(code));
            }
        }

        [Test]
        public void Generate_DefaultLengthIsTenDataCharacters()
        {
            Assert.That(JoinCode.Generate().Data.Length, Is.EqualTo(JoinCodeFormat.DefaultDataLength));
        }

        [Test]
        public void Generate_IsUniqueAcrossManyCodes()
        {
            var seen = new HashSet<JoinCode>();
            for (int i = 0; i < 20000; i++)
            {
                Assert.That(seen.Add(JoinCode.Generate()), Is.True, "duplicate code after " + i);
            }
        }

        [Test]
        public void Generate_UsesEverySymbol()
        {
            var seen = new HashSet<char>();
            for (int i = 0; i < 500; i++)
            {
                foreach (char c in JoinCode.Generate().Data)
                {
                    seen.Add(c);
                }
            }

            Assert.That(seen.Count, Is.EqualTo(32));
        }

        [Test]
        public void Generate_IsDeterministicForAGivenRng()
        {
            using (var rng = new SequenceRng(0, 1, 2, 31, 32, 255))
            {
                JoinCode code = JoinCode.Generate(6, rng);

                // byte & 31 picks the symbol: 0->'2', 1->'3', 2->'4', 31->'Z', 32->'2', 255->'Z'.
                Assert.That(code.Data, Is.EqualTo("234Z2Z"));
            }
        }

        [TestCase(JoinCodeFormat.MinDataLength - 1)]
        [TestCase(JoinCodeFormat.MaxDataLength + 1)]
        [TestCase(0)]
        public void Generate_RejectsLengthOutOfRange(int length)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => JoinCode.Generate(length));
        }

        [Test]
        public void Generate_RejectsNullRng()
        {
            Assert.Throws<ArgumentNullException>(() => JoinCode.Generate(8, null));
        }

        [Test]
        public void FromData_AppendsTheCheckCharacter()
        {
            Assert.That(Sample.Data, Is.EqualTo("ABCD2345XY"));
            Assert.That(Sample.CheckCharacter, Is.EqualTo(JoinCodeFormat.ComputeCheckCharacter("ABCD2345XY")));
            Assert.That(Sample.Value, Is.EqualTo("ABCD2345XY" + Sample.CheckCharacter));
        }

        [Test]
        public void FromData_NormalizesCaseAndSeparators()
        {
            Assert.That(JoinCode.FromData("abcd-2345 xy"), Is.EqualTo(Sample));
        }

        [TestCase("ABCDE")]
        [TestCase("ABCDEFGHJKLMNPQRS")]
        [TestCase("ABCDE0")]
        [TestCase("ABCDEI")]
        public void FromData_RejectsBadData(string data)
        {
            Assert.Throws<ArgumentException>(() => JoinCode.FromData(data));
        }

        [Test]
        public void FromData_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => JoinCode.FromData(null));
        }

        [Test]
        public void Normalize_UpperCasesAndStripsSeparators()
        {
            Assert.That(JoinCodeFormat.Normalize("  ab-cd_ef.gh \t jk–lm "), Is.EqualTo("ABCDEFGHJKLM"));
            Assert.That(JoinCodeFormat.Normalize(null), Is.EqualTo(string.Empty));
            Assert.That(JoinCodeFormat.Normalize(" - "), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Format_GroupsInFours()
        {
            Assert.That(JoinCodeFormat.Format("ABCDEFGHJ"), Is.EqualTo("ABC-DEF-GHJ"));
            Assert.That(JoinCodeFormat.Format("ABCDEFGH"), Is.EqualTo("ABC-DEF-GH"));
            Assert.That(JoinCodeFormat.Format("ABCDEFG", 3, ' '), Is.EqualTo("ABC DEF G"));
            Assert.That(JoinCodeFormat.Format(string.Empty), Is.EqualTo(string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => JoinCodeFormat.Format("ABC", 0, '-'));
        }

        [Test]
        public void ToString_IsTheGroupedForm()
        {
            Assert.That(Sample.ToString(), Is.EqualTo(JoinCodeFormat.Format(Sample.Value)));
            Assert.That(Sample.ToString(), Does.Contain("-"));
        }

        [Test]
        public void Parse_AcceptsCanonicalFormattedAndSloppyInput()
        {
            string canonical = Sample.Value;
            string sloppy = "  " + canonical.ToLowerInvariant().Insert(3, " ").Insert(7, "-") + "\n";

            Assert.That(JoinCode.Parse(canonical), Is.EqualTo(Sample));
            Assert.That(JoinCode.Parse(Sample.ToString()), Is.EqualTo(Sample));
            Assert.That(JoinCode.Parse(sloppy), Is.EqualTo(Sample));
        }

        [Test]
        public void Parse_DetectsEverySingleSubstitution()
        {
            string canonical = Sample.Value;
            int checkedCount = 0;

            for (int i = 0; i < canonical.Length; i++)
            {
                foreach (char replacement in JoinCodeFormat.Alphabet)
                {
                    if (replacement == canonical[i])
                    {
                        continue;
                    }

                    char[] chars = canonical.ToCharArray();
                    chars[i] = replacement;

                    JoinCodeParseResult result;
                    Assert.That(JoinCode.TryParse(new string(chars), out result), Is.False, "missed substitution at " + i);
                    Assert.That(result.Error, Is.EqualTo(JoinCodeError.ChecksumMismatch));
                    checkedCount++;
                }
            }

            Assert.That(checkedCount, Is.EqualTo(canonical.Length * 31));
        }

        [Test]
        public void Parse_DetectsEverySwapOfNeighbouringCharacters_ForManyCodes()
        {
            for (int n = 0; n < 200; n++)
            {
                string canonical = JoinCode.Generate().Value;
                for (int i = 0; i + 1 < canonical.Length; i++)
                {
                    if (canonical[i] == canonical[i + 1])
                    {
                        continue;
                    }

                    char[] chars = canonical.ToCharArray();
                    char t = chars[i];
                    chars[i] = chars[i + 1];
                    chars[i + 1] = t;

                    Assert.That(JoinCode.TryParse(new string(chars), out JoinCode _), Is.False,
                        "missed swap at " + i + " in " + canonical);
                }
            }
        }

        [Test]
        public void Check_DetectsEverySwapOfAnyTwoSymbolsInAnyContext()
        {
            // Exhaustive over the quasigroup: (c∘x)∘y differs from (c∘y)∘x for every interim c and x ≠ y,
            // shown here by checking every pair after every one-symbol prefix.
            foreach (char prefix in JoinCodeFormat.Alphabet)
            {
                foreach (char x in JoinCodeFormat.Alphabet)
                {
                    foreach (char y in JoinCodeFormat.Alphabet)
                    {
                        if (x == y)
                        {
                            continue;
                        }

                        string a = "" + prefix + x + y + "2345";
                        string b = "" + prefix + y + x + "2345";
                        Assert.That(JoinCodeFormat.ComputeCheckCharacter(a), Is.Not.EqualTo(JoinCodeFormat.ComputeCheckCharacter(b)));
                    }
                }
            }
        }

        [Test]
        public void Parse_DetectsSwapsOfCharactersOneApart()
        {
            for (int n = 0; n < 200; n++)
            {
                string canonical = JoinCode.Generate().Value;
                for (int i = 0; i + 2 < canonical.Length; i++)
                {
                    if (canonical[i] == canonical[i + 2])
                    {
                        continue;
                    }

                    char[] chars = canonical.ToCharArray();
                    char t = chars[i];
                    chars[i] = chars[i + 2];
                    chars[i + 2] = t;

                    Assert.That(JoinCode.TryParse(new string(chars), out JoinCode _), Is.False,
                        "missed jump swap at " + i + " in " + canonical);
                }
            }
        }

        [Test]
        public void Parse_CorrectsOneLookAlike_AtEveryPositionItCouldStandFor()
        {
            // For every generated code and every character that has a look-alike, typing the look-alike
            // instead parses back to the original and reports the correction.
            var lookAlikeOf = new Dictionary<char, char[]>
            {
                { 'D', new[] { '0', 'O', 'o' } },
                { 'Q', new[] { '0', 'O' } },
                { 'L', new[] { '1', 'I', 'i' } },
                { 'J', new[] { '1', 'I' } },
                { 'T', new[] { '1', 'I' } },
                { '7', new[] { '1', 'I' } },
            };

            int corrections = 0;
            for (int n = 0; n < 300; n++)
            {
                JoinCode code = JoinCode.Generate();
                string canonical = code.Value;
                for (int i = 0; i < canonical.Length; i++)
                {
                    char[] typos;
                    if (!lookAlikeOf.TryGetValue(canonical[i], out typos))
                    {
                        continue;
                    }

                    foreach (char typo in typos)
                    {
                        char[] chars = canonical.ToCharArray();
                        chars[i] = typo;

                        JoinCodeParseResult result;
                        Assert.That(JoinCode.TryParse(new string(chars), out result), Is.True, result.Message);
                        Assert.That(result.Code, Is.EqualTo(code));
                        Assert.That(result.Corrected, Is.True);
                        corrections++;
                    }
                }
            }

            Assert.That(corrections, Is.GreaterThan(100));
        }

        [Test]
        public void Parse_ExactInputIsNotReportedAsCorrected()
        {
            JoinCodeParseResult result;
            Assert.That(JoinCode.TryParse(Sample.Value, out result), Is.True);
            Assert.That(result.Corrected, Is.False);
            Assert.That(result.Error, Is.EqualTo(JoinCodeError.None));
            Assert.That(result.Message, Is.Empty);
        }

        [Test]
        public void Parse_RejectsTwoLookAlikes()
        {
            JoinCode code = JoinCode.FromData("DDXYZ234");
            string typed = "00" + code.Value.Substring(2);

            JoinCodeParseResult result;
            Assert.That(JoinCode.TryParse(typed, out result), Is.False);
            Assert.That(result.Error, Is.EqualTo(JoinCodeError.InvalidCharacter));
            Assert.That(result.Position, Is.EqualTo(1));
        }

        [Test]
        public void Parse_RejectsALookAlikeTheCheckCannotResolve()
        {
            // 'E' has no look-alike, so a '0' where the 'E' belongs cannot become any valid code.
            JoinCode code = JoinCode.FromData("EXYZ2345");
            string typed = "0" + code.Value.Substring(1);

            JoinCodeParseResult result;
            Assert.That(JoinCode.TryParse(typed, out result), Is.False);
            Assert.That(result.Error, Is.EqualTo(JoinCodeError.InvalidCharacter));
            Assert.That(result.Position, Is.EqualTo(0));
            Assert.That(result.Message, Does.Contain("0, O, 1 or I"));
        }

        [TestCase(null, JoinCodeError.Empty)]
        [TestCase("", JoinCodeError.Empty)]
        [TestCase(" -- ", JoinCodeError.Empty)]
        [TestCase("ABCDEF", JoinCodeError.TooShort)]
        [TestCase("ABCDEFGHJKLMNPQRST", JoinCodeError.TooLong)]
        public void Parse_ReportsLengthProblems(string input, JoinCodeError expected)
        {
            JoinCodeParseResult result;
            Assert.That(JoinCode.TryParse(input, out result), Is.False);
            Assert.That(result.Error, Is.EqualTo(expected));
            Assert.That(result.Code.IsEmpty, Is.True);
            Assert.That(result.Message, Is.Not.Empty);
        }

        [Test]
        public void Parse_ReportsAnInvalidCharacterAndWhere()
        {
            JoinCodeParseResult result;
            Assert.That(JoinCode.TryParse("ABC@EFGH", out result), Is.False);
            Assert.That(result.Error, Is.EqualTo(JoinCodeError.InvalidCharacter));
            Assert.That(result.Position, Is.EqualTo(3));
            Assert.That(result.Message, Does.Contain("@").And.Contain("position 4"));
        }

        [Test]
        public void Parse_ThrowsFormatExceptionWithTheReason()
        {
            var ex = Assert.Throws<FormatException>(() => JoinCode.Parse("ABC"));
            Assert.That(ex.Message, Does.Contain("too short"));
        }

        [Test]
        public void Default_IsEmpty()
        {
            JoinCode code = default(JoinCode);

            Assert.That(code.IsEmpty, Is.True);
            Assert.That(code, Is.EqualTo(JoinCode.Empty));
            Assert.That(code.Value, Is.EqualTo(string.Empty));
            Assert.That(code.Data, Is.EqualTo(string.Empty));
            Assert.That(code.Length, Is.EqualTo(0));
            Assert.That(code.CheckCharacter, Is.EqualTo('\0'));
            Assert.That(code.ToString(), Is.EqualTo(string.Empty));
            Assert.That(code.GetHashCode(), Is.EqualTo(JoinCode.Empty.GetHashCode()));
        }

        [Test]
        public void Equality_ComparesCanonicalForm()
        {
            JoinCode a = JoinCode.Parse(Sample.Value.ToLowerInvariant());
            JoinCode b = JoinCode.Parse(Sample.ToString());
            JoinCode other = JoinCode.FromData("ABCD2345XZ");

            Assert.That(a == b, Is.True);
            Assert.That(a != other, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)"not a code"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void HasValidCheck_RejectsEmptyAndForeignCharacters()
        {
            Assert.That(JoinCodeFormat.HasValidCheck(null), Is.False);
            Assert.That(JoinCodeFormat.HasValidCheck(string.Empty), Is.False);
            Assert.That(JoinCodeFormat.HasValidCheck("ABC0"), Is.False);
            Assert.Throws<ArgumentNullException>(() => JoinCodeFormat.ComputeCheckCharacter(null));
            Assert.Throws<ArgumentException>(() => JoinCodeFormat.ComputeCheckCharacter("ab"));
        }

        [Test]
        public void LookAlikeCandidates_CoverExactlyTheExcludedGlyphs()
        {
            Assert.That(JoinCodeFormat.LookAlikeCandidates('0'), Is.EqualTo("DQ"));
            Assert.That(JoinCodeFormat.LookAlikeCandidates('O'), Is.EqualTo("DQ"));
            Assert.That(JoinCodeFormat.LookAlikeCandidates('1'), Is.EqualTo("LJT7"));
            Assert.That(JoinCodeFormat.LookAlikeCandidates('I'), Is.EqualTo("LJT7"));
            Assert.That(JoinCodeFormat.LookAlikeCandidates('A'), Is.Empty);
            Assert.That(JoinCodeFormat.LookAlikeCandidates('é'), Is.Empty);
            Assert.That(JoinCodeFormat.IsAlphabetCharacter('é'), Is.False);
        }

        /// <summary>Hands out a fixed byte sequence, repeating it as needed.</summary>
        internal sealed class SequenceRng : RandomNumberGenerator
        {
            private readonly byte[] sequence;
            private int next;

            public SequenceRng(params byte[] sequence)
            {
                this.sequence = sequence;
            }

            public override void GetBytes(byte[] data)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = sequence[next++ % sequence.Length];
                }
            }
        }
    }
}
