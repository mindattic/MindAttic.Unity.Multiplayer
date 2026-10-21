using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Multiplayer.Testing
{
    /// <summary>
    /// The nightly compose smoke without Docker (UM-T57): a dedicated server and N bot clients in one PlayMode test. The
    /// server registers with an in-process rendezvous service and writes its join code to a file, exactly as one launched
    /// with <c>-joincodefile</c> does; each bot is a <see cref="NetBotRunner"/> that waits for the file, joins by the code
    /// and hands its session to the game's <see cref="IBotDriver"/>. The test passes when every bot passes.
    /// </summary>
    /// <remarks>
    /// <para>Built on <see cref="NetSessionTest"/> with a dedicated <see cref="Topology.Server"/> and one client session per
    /// bot. Register prefabs, set the player prefab and attach the game's server-side components to
    /// <see cref="NetSessionTest.Server"/> in <see cref="NetSessionTest.OnSessionsCreated"/>, then
    /// <c>yield return</c> <see cref="RunBots(Func{string, IBotDriver}, string[])"/>. Give the test a NUnit
    /// <c>[Timeout]</c> above <see cref="BotTimeoutSeconds"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public class TwoBotsTests : NetBotTest
    /// {
    ///     public TwoBotsTests() : base(bots: 2) { }
    ///
    ///     protected override void OnSessionsCreated() =&gt; SetPlayerPrefab(MyGame.LoadPlayerPrefab());
    ///
    ///     [UnityTest, Timeout(180000)]
    ///     public IEnumerator BothBotsReachTheGoal() =&gt; RunBots(role =&gt; new MyBotDriver(), "A", "B");
    /// }
    /// </code>
    /// </example>
    public abstract class NetBotTest : NetSessionTest
    {
        private readonly List<NetBotRunner> _bots = new List<NetBotRunner>();

        /// <param name="bots">How many bots (client sessions) the test runs; at least 1.</param>
        /// <param name="transport">The transport the server and bots use.</param>
        protected NetBotTest(int bots, TransportKind transport = TransportKind.Udp)
            : base(Topology.Server, CheckBots(bots), transport)
        {
        }

        /// <summary>Seconds each bot's run may take before it fails. Defaults to 120.</summary>
        protected virtual float BotTimeoutSeconds
        {
            get { return 120f; }
        }

        /// <summary>The scenario every bot is handed (<see cref="NetBot.Scenario"/>); null by default.</summary>
        protected virtual string Scenario
        {
            get { return null; }
        }

        /// <summary>The in-process rendezvous service, once <see cref="RunBots(Func{string, IBotDriver}, string[])"/> started it.</summary>
        protected InProcessRendezvousServer Rendezvous { get; private set; }

        /// <summary>The file the server writes its join code to and the bots read it from (deleted when the test ends).</summary>
        protected string JoinCodePath { get; private set; }

        /// <summary>The bots, in the order of their roles.</summary>
        protected IReadOnlyList<NetBotRunner> Bots
        {
            get { return _bots; }
        }

        /// <summary>The bot playing <paramref name="role"/>.</summary>
        /// <exception cref="ArgumentException">No bot plays it.</exception>
        protected NetBotRunner Bot(string role)
        {
            foreach (NetBotRunner bot in _bots)
            {
                if (string.Equals(bot.Role, role, StringComparison.OrdinalIgnoreCase))
                {
                    return bot;
                }
            }

            throw new ArgumentException("No bot plays " + role + ".", nameof(role));
        }

        /// <summary>
        /// Runs the whole end to end: starts the bots (one per role, on the client sessions in order, each with the driver
        /// <paramref name="drivers"/> gives for its role; they wait for the code), then the dedicated server with the
        /// rendezvous service and the join-code file, then waits for every verdict. Fails the test unless every bot passes.
        /// </summary>
        /// <exception cref="ArgumentException">Not one role per bot.</exception>
        protected IEnumerator RunBots(Func<string, IBotDriver> drivers, params string[] roles)
        {
            if (drivers == null)
            {
                throw new ArgumentNullException(nameof(drivers));
            }

            yield return StartServerAndBots(drivers, roles);
            yield return WaitForBots();
        }

        /// <summary>
        /// <see cref="RunBots(Func{string, IBotDriver}, string[])"/>'s first half: the bots, then the server with its code
        /// written to <see cref="JoinCodePath"/>. Waits only until the server runs and the code is written.
        /// </summary>
        protected IEnumerator StartServerAndBots(Func<string, IBotDriver> drivers, params string[] roles)
        {
            if (roles == null || roles.Length != Clients.Count)
            {
                throw new ArgumentException("Give one role per bot: this fixture has " + Clients.Count + ".", nameof(roles));
            }

            Rendezvous = StartRendezvous();
            JoinCodePath = Path.Combine(Application.temporaryCachePath, "netbot-join-code-" + Guid.NewGuid().ToString("N") + ".txt");
            DisposeAtTearDown(new DeleteFile(JoinCodePath));
            NetBots.WriteJoinCodes(Server, JoinCodePath);

            for (int i = 0; i < roles.Length; i++)
            {
                StartBot(Clients[i], roles[i], drivers(roles[i]));
            }

            yield return StartServer(Rendezvous.ForServer(Config()));
            yield return WaitUntil(() => File.Exists(JoinCodePath), () => "the server never wrote its join code (published: '" + Server.PublishedCodeText + "')");
        }

        /// <summary>
        /// Starts one bot on <paramref name="client"/> playing <paramref name="role"/> with <paramref name="driver"/>, joining
        /// by the code in <see cref="JoinCodePath"/> through <see cref="Rendezvous"/>. Its GameObject is destroyed when the
        /// test ends.
        /// </summary>
        protected NetBotRunner StartBot(NetSession client, string role, IBotDriver driver)
        {
            if (Rendezvous == null || JoinCodePath == null)
            {
                throw new InvalidOperationException("StartBot needs the rendezvous service and the join-code file: call it from StartServerAndBots or RunBots.");
            }

            var host = new GameObject("NetBot " + role);
            DisposeAtTearDown(new DestroyObject(host));
            NetBotRunner bot = NetBotRunner.Run(host, client, driver, new NetBotOptions
            {
                Role = role,
                JoinCodeFile = JoinCodePath,
                Scenario = Scenario,
                JoinConfig = Rendezvous.ForClient(Config()),
                TimeoutSeconds = BotTimeoutSeconds,
            });
            _bots.Add(bot);
            return bot;
        }

        /// <summary>Waits until every bot has a verdict (or one fails), then fails the test unless they all passed.</summary>
        protected IEnumerator WaitForBots()
        {
            float deadline = Time.realtimeSinceStartup + BotTimeoutSeconds + 10f;
            while (Time.realtimeSinceStartup < deadline && !AllFinished() && !AnyFailed())
            {
                yield return null;
            }

            var problems = new StringBuilder();
            foreach (NetBotRunner bot in _bots)
            {
                if (bot.State != NetBotState.Passed)
                {
                    problems.Append("\n  bot ").Append(bot.Role).Append(": ").Append(bot.State)
                        .Append(bot.FailureReason != null ? " - " + bot.FailureReason : string.Empty);
                }
            }

            Assert.That(problems.Length, Is.EqualTo(0), "not every bot passed:" + problems);
        }

        /// <inheritdoc />
        protected override IEnumerator OnSessionsTearDown()
        {
            _bots.Clear();
            Rendezvous = null;
            JoinCodePath = null;
            yield break;
        }

        private bool AllFinished()
        {
            foreach (NetBotRunner bot in _bots)
            {
                if (!bot.IsFinished)
                {
                    return false;
                }
            }

            return true;
        }

        private bool AnyFailed()
        {
            foreach (NetBotRunner bot in _bots)
            {
                if (bot.State == NetBotState.Failed)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CheckBots(int bots)
        {
            if (bots < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bots), "A bot test needs at least one bot.");
            }

            return bots;
        }

        private sealed class DeleteFile : IDisposable
        {
            private readonly string _path;

            public DeleteFile(string path)
            {
                _path = path;
            }

            public void Dispose()
            {
                try
                {
                    if (File.Exists(_path))
                    {
                        File.Delete(_path);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        private sealed class DestroyObject : IDisposable
        {
            private readonly GameObject _target;

            public DestroyObject(GameObject target)
            {
                _target = target;
            }

            public void Dispose()
            {
                if (_target != null)
                {
                    UnityEngine.Object.Destroy(_target);
                }
            }
        }
    }
}
