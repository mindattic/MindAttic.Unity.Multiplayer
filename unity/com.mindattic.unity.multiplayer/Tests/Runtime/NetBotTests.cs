using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T57: the bot mode end to end in one process, on the test kit's <see cref="NetBotTest"/>: a dedicated server
    /// registers with the in-process rendezvous service and writes its join code to a file, two bots wait for it, join by
    /// it and hand their sessions to an <see cref="IBotDriver"/> that asks the server a question over an RPC and waits for
    /// the answer. Also: a driver that throws fails its bot with the reason, and the passed one keeps playing after its verdict.
    /// </summary>
    public class NetBotTests : NetBotTest
    {
        public NetBotTests() : base(bots: 2)
        {
        }

        protected override float BotTimeoutSeconds
        {
            get { return 30f; }
        }

        protected override string Scenario
        {
            get { return "arena"; }
        }

        protected override void OnSessionsCreated()
        {
            SetPlayerPrefab(CreatePrefab<TestAvatar>("BotAvatar"));
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator TwoBotsJoinByCode_AndEachGetsItsRpcAnswered()
        {
            yield return RunBots(role => new AskingBot(role == "A" ? 1 : 2), "A", "B");

            Assert.That(Server.ConnectedClientIds.Count, Is.EqualTo(2));
            Assert.That(Rendezvous.Count, Is.EqualTo(1), "the server registered once");
            Assert.That(System.IO.File.ReadAllText(JoinCodePath).Trim(), Is.EqualTo(Server.PublishedCodeText));
            foreach (NetBotRunner bot in Bots)
            {
                Assert.That(bot.Session.State, Is.EqualTo(SessionState.Connected), "bot " + bot.Role);
                Assert.That(bot.Bot.JoinCode, Is.EqualTo(Server.PublishedCodeText), "bot " + bot.Role + " joined by the written code");
                Assert.That(bot.Bot.Scenario, Is.EqualTo("arena"));
                Assert.That(bot.PassMessage, Does.Contain("answer " + (bot.Role == "A" ? 101 : 102)));
            }

            string[] names = Server.Roster.All.Select(p => p.DisplayName).OrderBy(n => n).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "Bot A", "Bot B" }), "bots play as Bot <role>");
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator ADriverThatThrows_FailsItsBotWithTheReason_AndThePassedOneKeepsPlaying()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(BotLog.Tag("B")) + " FAIL: the driver threw InvalidOperationException: no strategy for B"));
            var keeper = new AskingBot(1, keepPlaying: true);
            yield return StartServerAndBots(role => role == "A" ? (IBotDriver)keeper : new ThrowingBot(), "A", "B");
            yield return WaitUntil(() => Bots.All(b => b.IsFinished), "both bots to finish", BotTimeoutSeconds);

            NetBotRunner b = Bot("B");
            Assert.That(b.State, Is.EqualTo(NetBotState.Failed));
            Assert.That(b.FailureReason, Does.Contain("no strategy for B"));
            Assert.That(b.Bot.Cancellation.IsCancellationRequested, Is.True, "a failed bot's waits are cancelled");

            NetBotRunner a = Bot("A");
            Assert.That(a.State, Is.EqualTo(NetBotState.Passed));
            float until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.That(a.State, Is.EqualTo(NetBotState.Passed), "a passed bot stays passed while its driver keeps playing");
            Assert.That(keeper.FramesAfterPass, Is.GreaterThan(0), "the driver kept running after it passed");
        }

        /// <summary>Asks the server over an RPC (TestAvatar.Vote, answered with Tell(sender, 100 + choice)) and passes on the answer.</summary>
        private sealed class AskingBot : IBotDriver
        {
            private readonly int _choice;
            private readonly bool _keepPlaying;

            public AskingBot(int choice, bool keepPlaying = false)
            {
                _choice = choice;
                _keepPlaying = keepPlaying;
            }

            public int FramesAfterPass { get; private set; }

            public async Task RunAsync(NetBot bot)
            {
                TestAvatar me = await bot.LocalPlayer<TestAvatar>(10f);
                me.Vote(_choice);
                await bot.Until(() => me.Tells.Contains(100 + _choice), "the server's answer", 10f);
                bot.Pass("answer " + (100 + _choice) + " from the server");
                while (_keepPlaying)
                {
                    await bot.NextFrame();
                    FramesAfterPass++;
                }
            }
        }

        private sealed class ThrowingBot : IBotDriver
        {
            public async Task RunAsync(NetBot bot)
            {
                await bot.NextFrame();
                throw new InvalidOperationException("no strategy for " + bot.Role);
            }
        }
    }

    /// <summary>UM-T57: a <c>-bot</c> launch with no driver component in the scene finds the one <see cref="NetBotDriverAttribute"/> class.</summary>
    public class NetBotDriverDiscoveryTests
    {
        [Test]
        public void ALaunchFindsTheMarkedDriverClass()
        {
            Assert.That(NetBots.MarkedDriverTypes(), Does.Contain(typeof(MarkedBot)));
            var host = new GameObject("driver host");
            try
            {
                string problem;
                IBotDriver driver = NetBots.FindDriver(host, out problem);
                Assert.That(driver, Is.InstanceOf<MarkedBot>(), problem);
            }
            finally
            {
                UnityEngine.Object.Destroy(host);
            }
        }
    }

    /// <summary>The one [NetBotDriver] class in the test assemblies, for <see cref="NetBotDriverDiscoveryTests"/>.</summary>
    [NetBotDriver]
    public sealed class MarkedBot : IBotDriver
    {
        public Task RunAsync(NetBot bot)
        {
            bot.Pass("marked");
            return Task.CompletedTask;
        }
    }
}
