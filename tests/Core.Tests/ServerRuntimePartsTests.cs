using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class ServerRuntimePartsTests
    {
        private static HealthReport Report(HealthStatus status)
        {
            return new HealthReport
            {
                Status = status, Name = "Friday", Version = "1.0", Players = 2, MaxPlayers = 8, Port = 7777,
                Transport = TransportKind.Udp, PasswordProtected = true, UptimeSeconds = 61.7,
            };
        }

        // --- health endpoint --------------------------------------------------------------------------------------

        [Test]
        public void Health_RunningIs200WithTheReport()
        {
            string response = HealthEndpoint.Respond("GET /healthz HTTP/1.1", Report(HealthStatus.Running));

            Assert.That(response, Does.StartWith("HTTP/1.1 200 OK\r\n"));
            Assert.That(response, Does.Contain("Content-Type: application/json"));
            Assert.That(response, Does.EndWith("\r\n\r\n{\"status\":\"running\",\"name\":\"Friday\",\"version\":\"1.0\",\"players\":2,\"maxPlayers\":8,"
                + "\"port\":7777,\"transport\":\"udp\",\"password\":true,\"uptimeSeconds\":61}"));
        }

        [TestCase(HealthStatus.Starting)]
        [TestCase(HealthStatus.Stopping)]
        public void Health_NotRunningIs503(HealthStatus status)
        {
            Assert.That(HealthEndpoint.Respond("GET /healthz HTTP/1.1", Report(status)), Does.StartWith("HTTP/1.1 503 Service Unavailable\r\n"));
        }

        [TestCase("GET / HTTP/1.1", "200")]
        [TestCase("GET /healthz?verbose=1 HTTP/1.1", "200")]
        [TestCase("GET /admin HTTP/1.1", "404")]
        [TestCase("POST /healthz HTTP/1.1", "405")]
        [TestCase("garbage", "400")]
        [TestCase("", "400")]
        public void Health_Routes(string requestLine, string status)
        {
            Assert.That(HealthEndpoint.Respond(requestLine, Report(HealthStatus.Running)), Does.StartWith("HTTP/1.1 " + status + " "));
        }

        [Test]
        public void Health_HeadHasNoBody()
        {
            string response = HealthEndpoint.Respond("HEAD /healthz HTTP/1.1", Report(HealthStatus.Running));
            Assert.That(response, Does.EndWith("\r\n\r\n"));
        }

        [Test]
        public async Task Health_ServesOverRealHttp_AndStops()
        {
            HealthReport current = Report(HealthStatus.Starting);
            using (var endpoint = new HealthEndpoint(() => Volatile.Read(ref current)))
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                endpoint.Start(0, IPAddress.Loopback);
                Assert.That(endpoint.IsRunning, Is.True);
                string url = "http://127.0.0.1:" + endpoint.Port + "/healthz";

                HttpResponseMessage starting = await http.GetAsync(url);
                Assert.That(starting.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

                Volatile.Write(ref current, Report(HealthStatus.Running));
                HttpResponseMessage running = await http.GetAsync(url);
                Assert.That(running.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await running.Content.ReadAsStringAsync(), Does.Contain("\"players\":2"));

                int port = endpoint.Port;
                endpoint.Stop();
                Assert.That(endpoint.IsRunning, Is.False);
                Assert.ThrowsAsync<HttpRequestException>(async () => await http.GetAsync("http://127.0.0.1:" + port + "/healthz"));
            }
        }

        [Test]
        public async Task Health_AReportThatThrows_IsServedAsStarting()
        {
            using (var endpoint = new HealthEndpoint(() => throw new InvalidOperationException("boom")))
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                endpoint.Start(0, IPAddress.Loopback);
                HttpResponseMessage response = await http.GetAsync("http://127.0.0.1:" + endpoint.Port + "/healthz");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            }
        }

        // --- admin console ----------------------------------------------------------------------------------------

        [Test]
        public void Admin_RunsRegisteredCommandsWithTheirArguments()
        {
            var console = new AdminConsole();
            IReadOnlyList<string> seen = null;
            console.Register("kick", "kick <clientId> [reason]", "Removes a player.", args =>
            {
                seen = args;
                return "kicked";
            });

            AdminCommandResult result = console.Execute("  KICK 3 \"too \\\"loud\\\" today\"  ");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Output, Is.EqualTo("kicked"));
            Assert.That(seen, Is.EqualTo(new[] { "3", "too \"loud\" today" }));
        }

        [Test]
        public void Admin_UnknownBadArgumentsAndFailuresAreReported()
        {
            var console = new AdminConsole();
            console.Register("kick", "kick <clientId>", "Removes a player.", args => throw new ArgumentException("Give a client id."));
            console.Register("boom", null, null, args => throw new InvalidOperationException("broken"));

            Assert.That(console.Execute("nope").Succeeded, Is.False);
            Assert.That(console.Execute("nope").Output, Does.Contain("Unknown command 'nope'"));
            Assert.That(console.Execute("kick").Output, Is.EqualTo("Give a client id. Usage: kick <clientId>"));
            Assert.That(console.Execute("boom").Output, Is.EqualTo("boom failed: broken"));
            Assert.That(console.Execute("   ").Succeeded, Is.True);
        }

        [Test]
        public void Admin_HelpListsEveryCommand_AndUnregisterRemovesOne()
        {
            var console = new AdminConsole();
            console.Register("stop", "stop", "Shuts the server down cleanly.", args => "bye");

            string help = console.Execute("help").Output;
            Assert.That(help, Does.Contain("help  - Lists the commands."));
            Assert.That(help, Does.Contain("stop  - Shuts the server down cleanly."));
            Assert.That(console.Unregister("stop"), Is.True);
            Assert.That(console.Commands, Is.EqualTo(new[] { "help" }));
            Assert.Throws<ArgumentException>(() => console.Register("two words", null, null, args => string.Empty));
        }

        [Test]
        public void Admin_SplitHandlesQuotesAndEmptyArguments()
        {
            Assert.That(AdminConsole.Split("say \"\" x"), Is.EqualTo(new[] { "say", "", "x" }));
            Assert.That(AdminConsole.Split("a\tb"), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(AdminConsole.Split(null), Is.Empty);
        }

        // --- shutdown ---------------------------------------------------------------------------------------------

        [Test]
        public void Shutdown_OnlyTheFirstRequestCounts()
        {
            var shutdown = new ServerShutdown();
            Assert.That(shutdown.IsRequested, Is.False);
            Assert.That(shutdown.IsOverdue(100, 0), Is.False);

            Assert.That(shutdown.Request("SIGTERM", 10.0), Is.True);
            Assert.That(shutdown.Request("SIGINT", 11.0), Is.False);

            Assert.That(shutdown.Reason, Is.EqualTo("SIGTERM"));
            Assert.That(shutdown.RequestedAt, Is.EqualTo(10.0));
            Assert.That(shutdown.IsOverdue(14.9, 5), Is.False);
            Assert.That(shutdown.IsOverdue(15.0, 5), Is.True);
        }

        [Test]
        public void Shutdown_RequestsFromManyThreadsStartItOnce()
        {
            var shutdown = new ServerShutdown();
            int winners = 0;
            Parallel.For(0, 64, i =>
            {
                if (shutdown.Request("thread " + i, i))
                {
                    Interlocked.Increment(ref winners);
                }
            });

            Assert.That(winners, Is.EqualTo(1));
            Assert.That(shutdown.Reason, Does.StartWith("thread "));
        }

        // --- log file ---------------------------------------------------------------------------------------------

        [Test]
        public void LogFile_WritesTimestampedLines_AndKeepsOnlyTheNewestRuns()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MindAtticLogs-" + Guid.NewGuid().ToString("N"));
            try
            {
                var start = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
                for (int run = 0; run < 4; run++)
                {
                    using (ServerLogFile log = ServerLogFile.Open(dir, start.AddMinutes(run), keep: 3))
                    {
                        log.Write(start.AddMinutes(run), "Info", "run " + run);
                    }
                }

                IReadOnlyList<string> files = ServerLogFile.List(dir);
                Assert.That(files, Has.Count.EqualTo(3));
                Assert.That(Path.GetFileName(files[0]), Is.EqualTo("server-20261020-120100.log"));
                Assert.That(File.ReadAllText(files[2]), Is.EqualTo("2026-10-20T12:03:00.000Z [Info] run 3\n"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void LogFile_SameSecondGetsASuffix_AndWritingAfterDisposeIsIgnored()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MindAtticLogs-" + Guid.NewGuid().ToString("N"));
            try
            {
                var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
                using (ServerLogFile first = ServerLogFile.Open(dir, now, 0))
                using (ServerLogFile second = ServerLogFile.Open(dir, now, 0))
                {
                    Assert.That(Path.GetFileName(second.Path), Is.EqualTo("server-20261020-120000-2.log"));
                    second.Dispose();
                    second.Write(now, "Info", "ignored");
                    Assert.That(File.ReadAllText(second.Path), Is.Empty);
                }
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void LogFile_FormatLine_DefaultsTheLevel()
        {
            var time = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
            Assert.That(ServerLogFile.FormatLine(time, null, "hi"), Is.EqualTo("2026-01-02T03:04:05.678Z [Info] hi"));
        }
    }
}
