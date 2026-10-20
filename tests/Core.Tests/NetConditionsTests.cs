using System.Linq;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class NetConditionsTests
    {
        [Test]
        public void None_IsTheDefault_AndSimulatesNothing()
        {
            Assert.That(NetConditions.None.IsNone, Is.True);
            Assert.That(default(NetConditions), Is.EqualTo(NetConditions.None));
            Assert.That(NetConditions.For(NetConditionsProfile.None), Is.EqualTo(NetConditions.None));
            Assert.That(NetConditions.None.ToString(), Is.EqualTo("no simulation"));
        }

        [Test]
        public void Lossy_IsTheSuitesProfile_150msAnd5PercentLoss()
        {
            NetConditions lossy = NetConditions.For(NetConditionsProfile.Lossy);
            Assert.That(lossy.LatencyMs, Is.EqualTo(150));
            Assert.That(lossy.PacketLossPercent, Is.EqualTo(5));
            Assert.That(lossy.JitterMs, Is.EqualTo(10));
            Assert.That(lossy.ToString(), Is.EqualTo("150 ms ±10 ms, 5% loss"));
        }

        [TestCase(NetConditionsProfile.Broadband)]
        [TestCase(NetConditionsProfile.Mobile4G)]
        [TestCase(NetConditionsProfile.Mobile3G)]
        [TestCase(NetConditionsProfile.Lossy)]
        public void EveryNamedProfile_IsValidAndSimulatesSomething(NetConditionsProfile profile)
        {
            NetConditions conditions = NetConditions.For(profile);
            Assert.That(conditions.Validate(), Is.Empty);
            Assert.That(conditions.IsNone, Is.False);
        }

        [Test]
        public void Profiles_GetWorseInOrder()
        {
            int[] latency = new[] { NetConditionsProfile.Broadband, NetConditionsProfile.Mobile4G, NetConditionsProfile.Mobile3G }
                .Select(p => NetConditions.For(p).LatencyMs).ToArray();
            Assert.That(latency, Is.Ordered.Ascending);
        }

        [Test]
        public void Custom_HasNoNumbersOfItsOwn()
        {
            Assert.That(NetConditions.For(NetConditionsProfile.Custom), Is.EqualTo(NetConditions.None));
        }

        [TestCase(-1, 0, 0)]
        [TestCase(NetConditions.MaxLatencyMs + 1, 0, 0)]
        [TestCase(100, -1, 0)]
        [TestCase(100, 101, 0)]
        [TestCase(0, 0, -1)]
        [TestCase(0, 0, 101)]
        public void Validate_RejectsOutOfRangeNumbers(int latency, int jitter, int loss)
        {
            Assert.That(new NetConditions(latency, jitter, loss).Validate(), Has.Count.EqualTo(1));
        }

        [TestCase(0, 0, 0)]
        [TestCase(0, 0, 100)]
        [TestCase(NetConditions.MaxLatencyMs, NetConditions.MaxLatencyMs, 50)]
        public void Validate_AcceptsBounds(int latency, int jitter, int loss)
        {
            Assert.That(new NetConditions(latency, jitter, loss).IsValid, Is.True);
        }

        [Test]
        public void Equality_IsByValue()
        {
            Assert.That(new NetConditions(1, 0, 2) == new NetConditions(1, 0, 2), Is.True);
            Assert.That(new NetConditions(1, 0, 2) != new NetConditions(1, 0, 3), Is.True);
            Assert.That(new NetConditions(1, 0, 2).GetHashCode(), Is.EqualTo(new NetConditions(1, 0, 2).GetHashCode()));
        }

        [TestCase("none", NetConditionsProfile.None)]
        [TestCase("OFF", NetConditionsProfile.None)]
        [TestCase("Broadband", NetConditionsProfile.Broadband)]
        [TestCase("4g", NetConditionsProfile.Mobile4G)]
        [TestCase("mobile4g", NetConditionsProfile.Mobile4G)]
        [TestCase("3G", NetConditionsProfile.Mobile3G)]
        [TestCase(" lossy ", NetConditionsProfile.Lossy)]
        [TestCase("custom", NetConditionsProfile.Custom)]
        public void TryParseProfile_KnowsEveryName(string text, NetConditionsProfile expected)
        {
            Assert.That(NetConditions.TryParseProfile(text, out NetConditionsProfile profile), Is.True);
            Assert.That(profile, Is.EqualTo(expected));
        }

        [TestCase("dialup")]
        [TestCase("")]
        [TestCase(null)]
        public void TryParseProfile_RefusesUnknownNames(string text)
        {
            Assert.That(NetConditions.TryParseProfile(text, out _), Is.False);
        }

        // --- NetConfig --------------------------------------------------------------------------------------------

        [Test]
        public void NetConfig_DefaultsToNoSimulation()
        {
            var config = new NetConfig();
            Assert.That(config.Conditions, Is.EqualTo(NetConditionsProfile.None));
            Assert.That(config.EffectiveConditions.IsNone, Is.True);
            Assert.That(config.ToString(), Does.Not.Contain("simulating"));
        }

        [Test]
        public void NetConfig_NamedProfile_IgnoresTheCustomNumbers()
        {
            var config = new NetConfig { Conditions = NetConditionsProfile.Lossy, SimulatedLatencyMs = -5 };
            Assert.That(config.EffectiveConditions, Is.EqualTo(NetConditions.For(NetConditionsProfile.Lossy)));
            Assert.That(config.IsValid, Is.True, "custom numbers are only checked when Custom is chosen");
            Assert.That(config.ToString(), Does.Contain("simulating Lossy (150 ms ±10 ms, 5% loss)"));
        }

        [Test]
        public void NetConfig_Custom_UsesAndValidatesTheCustomNumbers()
        {
            var config = new NetConfig { Conditions = NetConditionsProfile.Custom, SimulatedLatencyMs = 80, SimulatedJitterMs = 5, SimulatedPacketLossPercent = 2 };
            Assert.That(config.EffectiveConditions, Is.EqualTo(new NetConditions(80, 5, 2)));
            Assert.That(config.IsValid, Is.True);

            config.SimulatedPacketLossPercent = 120;
            Assert.That(config.Validate().Single(), Does.Contain("packet loss"));
        }

        [Test]
        public void NetConfig_RejectsAnUndefinedProfile()
        {
            var config = new NetConfig { Conditions = (NetConditionsProfile)42 };
            Assert.That(config.Validate().Single(), Does.Contain("Conditions 42"));
        }

        [Test]
        public void NetConfig_UseCustomConditions_StartsFromTheChosenProfile()
        {
            var config = new NetConfig { Conditions = NetConditionsProfile.Mobile4G };
            config.UseCustomConditions();
            Assert.That(config.Conditions, Is.EqualTo(NetConditionsProfile.Custom));
            Assert.That(config.EffectiveConditions, Is.EqualTo(NetConditions.For(NetConditionsProfile.Mobile4G)));

            config.SimulatedLatencyMs = 99;
            config.UseCustomConditions();
            Assert.That(config.SimulatedLatencyMs, Is.EqualTo(99), "already custom: nothing is reset");
        }

        [Test]
        public void NetConfig_Clone_CopiesTheConditions()
        {
            var config = new NetConfig { Conditions = NetConditionsProfile.Custom, SimulatedLatencyMs = 40, SimulatedJitterMs = 4, SimulatedPacketLossPercent = 1 };
            Assert.That(config.Clone().EffectiveConditions, Is.EqualTo(config.EffectiveConditions));
        }

        // --- NetCommandLine ---------------------------------------------------------------------------------------

        [Test]
        public void CommandLine_NetSim_SelectsAProfile()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-netsim", "lossy" });
            Assert.That(result.Errors, Is.Empty);
            Assert.That(config.Conditions, Is.EqualTo(NetConditionsProfile.Lossy));
        }

        [Test]
        public void CommandLine_LatencyJitterLoss_MakeCustomConditions()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-latency", "150", "-jitter=20", "-loss", "5%" });
            Assert.That(result.Errors, Is.Empty);
            Assert.That(config.Conditions, Is.EqualTo(NetConditionsProfile.Custom));
            Assert.That(config.EffectiveConditions, Is.EqualTo(new NetConditions(150, 20, 5)));
        }

        [Test]
        public void CommandLine_ANumberAfterAProfile_KeepsTheProfilesOtherNumbers()
        {
            var config = new NetConfig();
            NetCommandLine.Apply(config, new[] { "-netsim", "mobile4g", "-loss", "10" });
            NetConditions mobile = NetConditions.For(NetConditionsProfile.Mobile4G);
            Assert.That(config.EffectiveConditions, Is.EqualTo(new NetConditions(mobile.LatencyMs, mobile.JitterMs, 10)));
        }

        [Test]
        public void CommandLine_BadValues_AreReportedAndChangeNothing()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-netsim", "dialup", "-latency", "-3", "-jitter", "x", "-loss", "101" });
            Assert.That(result.Errors, Has.Count.EqualTo(4));
            Assert.That(result.Errors[0], Does.Contain("-netsim"));
            Assert.That(config.Conditions, Is.EqualTo(NetConditionsProfile.None));
            Assert.That(config.EffectiveConditions.IsNone, Is.True);
        }
    }
}
