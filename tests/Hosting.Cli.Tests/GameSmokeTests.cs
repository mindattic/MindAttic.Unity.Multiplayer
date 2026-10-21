using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Commands;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Game;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

/// <summary>
/// UM-T57: <c>mp-host game smoke</c>, the end-to-end smoke any game runs with one command: the plan's defaults, the
/// rendered Dockerfile and compose stack (rendezvous + a dedicated server + N bots), the verdict read from the bots'
/// logs, and whole runs against a scripted docker/Unity, without a Unity install, Docker or a network.
/// </summary>
[TestFixture]
public class GameSmokeTests
{
    private const string Version = "6000.4.3f1";

    private string _root = null!;
    private string _project = null!;
    private string _editor = null!;
    private HostingCredentialStore _vault = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "mp-host-smoke-" + Guid.NewGuid().ToString("N"));
        _project = Path.Combine(_root, "My Game");
        GameCommandTests.WriteProject(_project, "Unity.Cursory", ("Assets/Game/Main.unity", true));
        _editor = Path.Combine(_root, "Hub", Version, "Editor", "Unity.exe");
        GameCommandTests.Touch(_editor);
        _vault = new HostingCredentialStore(Path.Combine(_root, "vault"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // ---- bots, arguments, verdicts -------------------------------------------------------------------------------

    [Test]
    public void ParseBots_takes_a_list_or_a_count_and_names_a_service_per_bot()
    {
        Assert.That(SmokeStack.ParseBots("A,B"), Is.EqualTo(new[] { new SmokeBot("A", "bot-a"), new SmokeBot("B", "bot-b") }));
        Assert.That(SmokeStack.ParseBots("scout tank_1").Select(b => b.Service), Is.EqualTo(new[] { "bot-scout", "bot-tank-1" }));
        Assert.That(SmokeStack.ParseBots("3").Select(b => b.Role), Is.EqualTo(new[] { "1", "2", "3" }));

        Assert.Throws<ArgumentException>(() => SmokeStack.ParseBots(""));
        Assert.Throws<ArgumentException>(() => SmokeStack.ParseBots("0"));
        Assert.Throws<ArgumentException>(() => SmokeStack.ParseBots("A,a"), "a role given twice");
        Assert.Throws<ArgumentException>(() => SmokeStack.ParseBots("A$"), "only letters, digits, - and _");
    }

    [Test]
    public void SplitArguments_keeps_quoted_runs_together()
    {
        Assert.That(SmokeStack.SplitArguments("-maxplayers 4 -name \"Friday game\" -x ''"), Is.EqualTo(new[] { "-maxplayers", "4", "-name", "Friday game", "-x", "" }));
        Assert.That(SmokeStack.SplitArguments(null), Is.Empty);
        Assert.Throws<ArgumentException>(() => SmokeStack.SplitArguments("-name \"open"));
    }

    [Test]
    public void Judge_passes_on_every_bots_OK_line_fails_fast_on_FAIL_and_otherwise_waits()
    {
        IReadOnlyList<SmokeBot> bots = SmokeStack.ParseBots("A,B");
        string a = "bot-a-1  | [NetBot:A] OK: level 2 solved\r";
        string b = "bot-b-1  | [NetBot:B] OK: level 2 solved";

        SmokeVerdict waiting = SmokeStack.Judge(a, bots);
        Assert.That(waiting.Passed || waiting.Failed, Is.False);
        Assert.That(waiting.Waiting, Is.EqualTo(new[] { "B" }));

        SmokeVerdict passed = SmokeStack.Judge(a + "\n" + b, bots);
        Assert.That(passed.Passed, Is.True);
        Assert.That(passed.PassLines, Has.Count.EqualTo(2));
        Assert.That(passed.PassLines[0], Does.Not.EndWith("\r"));

        SmokeVerdict failed = SmokeStack.Judge(a + "\nbot-b-1  | [NetBot:B] FAIL: timed out", bots);
        Assert.That(failed.Failed, Is.True);
        Assert.That(failed.Passed, Is.False);
        Assert.That(failed.FailLines.Single(), Does.Contain("timed out"));
        Assert.That(SmokeStack.Judge("[NetBot:AB] OK: x", SmokeStack.ParseBots("A")).Passed, Is.False, "another role's line does not count");
    }

    // ---- the plan and the rendered stack ------------------------------------------------------------------------

    [Test]
    public void Plan_defaults_to_two_bots_a_fresh_build_and_the_stack_under_Builds_Smoke()
    {
        SmokePlan plan = Plan("game", "smoke", "--project", _project);

        Assert.That(plan.Bots.Select(b => b.Role), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(plan.Builds, Is.True);
        Assert.That(plan.Editor!.ExecutablePath, Is.EqualTo(_editor));
        Assert.That(plan.OutputDirectory, Is.EqualTo(Path.Combine(Path.GetFullPath(_project), "Builds", "Smoke")));
        Assert.That(plan.PlayerDirectory, Is.EqualTo(Path.Combine(plan.OutputDirectory, "player")));
        Assert.That(plan.ComposeProject, Is.EqualTo("unity-cursory-smoke"));
        Assert.That(plan.GameImage, Is.EqualTo("unity-cursory-smoke:local"));
        Assert.That(plan.RendezvousImage, Is.EqualTo("ghcr.io/mindattic/rendezvous:latest"), "no checkout to build it from");
        Assert.That(plan.TimeoutSeconds, Is.EqualTo(SmokePlan.DefaultTimeoutSeconds));

        IReadOnlyList<string> unity = plan.UnityArguments();
        Assert.That(After(unity, "-executeMethod"), Is.EqualTo(SmokePlan.BuildMethod));
        Assert.That(After(unity, "-botOutput"), Is.EqualTo(plan.PlayerDirectory));
        Assert.That(After(unity, "-projectPath"), Is.EqualTo(Path.GetFullPath(_project)));
        Assert.That(unity, Does.Contain("-nographics"));
        Assert.That(unity, Does.Not.Contain("-botScene"));
    }

    [Test]
    public void Plan_takes_bots_scenario_and_args_from_mp_host_json_and_lets_flags_win()
    {
        File.WriteAllText(Path.Combine(_project, "mp-host.json"),
            """{ "bots": "A,B,C", "scenario": "2", "server-args": "-maxplayers 4", "smoke-timeout": 120, "scene": "Assets/Game/Main.unity" }""");

        SmokePlan fromFile = Plan("game", "smoke", "--project", _project);
        Assert.That(fromFile.Bots, Has.Count.EqualTo(3));
        Assert.That(fromFile.Scenario, Is.EqualTo("2"));
        Assert.That(fromFile.ServerArguments, Is.EqualTo(new[] { "-maxplayers", "4" }));
        Assert.That(fromFile.TimeoutSeconds, Is.EqualTo(120));
        Assert.That(After(fromFile.UnityArguments(), "-botScene"), Is.EqualTo("Assets/Game/Main.unity"));

        SmokePlan flags = Plan("game", "smoke", "--project", _project, "--bots", "1", "--scenario", "arena", "--timeout", "60", "--bot-args", "-speed fast");
        Assert.That(flags.Bots.Single().Role, Is.EqualTo("1"));
        Assert.That(flags.Scenario, Is.EqualTo("arena"));
        Assert.That(flags.TimeoutSeconds, Is.EqualTo(60));
        Assert.That(flags.BotArguments, Is.EqualTo(new[] { "-speed", "fast" }));
        Assert.Throws<ArgumentException>(() => Plan("game", "smoke", "--project", _project, "--timeout", "3"));
    }

    [Test]
    public void Plan_reuses_a_built_player_and_builds_rendezvous_from_the_checkout()
    {
        Assert.Throws<InvalidOperationException>(() => Plan("game", "smoke", "--project", _project, "--skip-build"), "no player built yet");

        string player = Path.Combine(_root, "ci-player");
        GameCommandTests.Touch(Path.Combine(player, SmokeStack.ExecutableName));
        string checkout = Path.Combine(_root, "Unity.Multiplayer");
        GameCommandTests.Touch(Path.Combine(checkout, "server", "Rendezvous", "Dockerfile"));

        SmokePlan plan = Plan("game", "smoke", "--project", _project, "--player", player, "--multiplayer", checkout);
        Assert.That(plan.Builds, Is.False);
        Assert.That(plan.Editor, Is.Null, "no Unity needed to reuse a player");
        Assert.That(plan.PlayerDirectory, Is.EqualTo(Path.GetFullPath(player)));
        Assert.That(plan.RendezvousSource, Is.EqualTo(Path.GetFullPath(checkout)));
        Assert.That(plan.RendezvousImage, Is.EqualTo("unity-cursory-smoke-rendezvous:local"));
    }

    [Test]
    public void Compose_runs_rendezvous_a_server_that_writes_its_code_and_a_service_per_bot()
    {
        File.WriteAllText(Path.Combine(_project, "mp-host.json"), """{ "scenario": "2", "server-args": "-name \"Smoke $1\"" }""");
        SmokePlan plan = Plan("game", "smoke", "--project", _project, "--bots", "A,B", "--timeout", "240");

        string compose = SmokeStack.Compose(plan);

        Assert.That(compose, Does.Contain("name: unity-cursory-smoke\n"));
        Assert.That(compose, Does.Contain("  rendezvous:\n    image: ghcr.io/mindattic/rendezvous:latest\n"));
        Assert.That(compose, Does.Contain("Rendezvous__RegistrationSecret: ${RENDEZVOUS_SECRET:-smoke-test-secret}"));
        Assert.That(compose, Does.Contain("MINDATTIC_RENDEZVOUS_SECRET: ${RENDEZVOUS_SECRET:-smoke-test-secret}"));
        Assert.That(compose, Does.Contain("condition: service_healthy"));
        string server = Section(compose, "  server:");
        Assert.That(server, Does.Contain("- \"-mode\"\n      - \"server\""));
        Assert.That(server, Does.Contain("- \"-joincodefile\"\n      - \"/shared/join-code.txt\""));
        Assert.That(server, Does.Contain("- \"-scenario\"\n      - \"2\""));
        Assert.That(server, Does.Contain("- \"Smoke $$1\""), "compose's $ interpolation is escaped");
        foreach (string role in new[] { "A", "B" })
        {
            string bot = Section(compose, $"  bot-{role.ToLowerInvariant()}:");
            Assert.That(bot, Does.Contain($"- \"-bot\"\n      - \"{role}\""));
            Assert.That(bot, Does.Contain("- \"-joincodefile\"\n      - \"/shared/join-code.txt\""));
            Assert.That(bot, Does.Contain("- \"-rendezvous\"\n      - \"http://rendezvous:8080\""));
            Assert.That(bot, Does.Contain("- \"-bottimeout\"\n      - \"240\""));
            Assert.That(bot, Does.Contain("- \"-scenario\"\n      - \"2\""));
            Assert.That(bot, Does.Contain("- shared:/shared"));
        }

        Assert.That(compose, Does.EndWith("volumes:\n  shared:\n"));
    }

    [Test]
    public void Dockerfile_runs_the_player_headless_with_SDLs_dummy_driver()
    {
        string dockerfile = SmokeStack.Dockerfile();
        Assert.That(dockerfile, Does.Contain("ENV SDL_VIDEODRIVER=dummy"));
        Assert.That(dockerfile, Does.Contain("COPY player/ ./"));
        Assert.That(dockerfile, Does.Contain("chown -R game:game /game /shared"));
        Assert.That(dockerfile, Does.Contain("USER game"));
        Assert.That(dockerfile, Does.Contain($"ENTRYPOINT [\"./{SmokeStack.ExecutableName}\"]"));
        Assert.That(SmokeStack.DockerIgnore(), Does.Contain("DoNotShip"));
    }

    // ---- whole runs -----------------------------------------------------------------------------------------------

    [Test]
    public async Task Run_builds_writes_the_stack_starts_it_and_passes_once_every_bot_logs_OK()
    {
        var docker = new SmokeDocker(_editor) { Logs = poll => poll < 2 ? "server-1 | starting" : "bot-a-1 | [NetBot:A] OK: solved\nbot-b-1 | [NetBot:B] OK: solved\n" };
        var log = new RecordingDeployLog();

        int exit = await GameCommand.RunAsync(new[] { "game", "smoke", "--project", _project }, log, Services(docker), CancellationToken.None);

        Assert.That(exit, Is.EqualTo(0), string.Join('\n', log.Lines));
        string out_ = Path.Combine(Path.GetFullPath(_project), "Builds", "Smoke");
        Assert.That(File.ReadAllText(Path.Combine(out_, "Dockerfile")), Does.Contain("SDL_VIDEODRIVER=dummy"));
        Assert.That(File.ReadAllText(Path.Combine(out_, "docker-compose.yml")), Does.Contain("bot-b:"));
        Assert.That(File.ReadAllText(Path.Combine(out_, "compose.log")), Does.Contain("[NetBot:B] OK:"));
        Assert.That(log.Lines, Has.Some.Contains("PASS: every bot passed (A, B)"));

        List<string> calls = docker.Calls;
        Assert.That(calls[0], Does.StartWith(_editor + " -batchmode"));
        Assert.That(calls, Has.Some.EqualTo($"docker build -q -t unity-cursory-smoke:local {out_}"));
        int up = calls.FindIndex(c => c.EndsWith(" up -d"));
        Assert.That(up, Is.GreaterThan(0));
        Assert.That(calls.Count(c => c.EndsWith(" logs --no-color")), Is.EqualTo(3));
        Assert.That(calls.Last(), Does.EndWith(" down -v"), "the stack is torn down afterwards");
        Assert.That(docker.Secrets.Distinct().Single(), Has.Length.EqualTo(32), "one generated secret for the whole run");
    }

    [Test]
    public async Task Run_fails_on_a_bots_FAIL_line_and_after_the_timeout()
    {
        var failing = new SmokeDocker(_editor) { Logs = _ => "bot-a-1 | [NetBot:A] OK: fine\nbot-b-1 | [NetBot:B] FAIL: could not connect" };
        var log = new RecordingDeployLog();
        Assert.That(await GameCommand.RunAsync(new[] { "game", "smoke", "--project", _project }, log, Services(failing), CancellationToken.None), Is.EqualTo(1));
        Assert.That(log.Lines, Has.Some.Contains("error: bot-b-1 | [NetBot:B] FAIL: could not connect"));
        Assert.That(failing.Calls.Last(), Does.EndWith(" down -v"));

        var silent = new SmokeDocker(_editor) { Logs = _ => "bot-a-1 | [NetBot:A] OK: fine" };
        log = new RecordingDeployLog();
        Assert.That(await GameCommand.RunAsync(new[] { "game", "smoke", "--project", _project, "--skip-build", "--timeout", "30", "--keep" }, log, Services(silent), CancellationToken.None), Is.EqualTo(1));
        Assert.That(log.Lines, Has.Some.Contains("timed out after 30 s waiting for bot(s) B"));
        Assert.That(silent.Calls.Count(c => c.EndsWith(" logs --no-color")), Is.EqualTo(11), "30 s at one read every 3 s, plus the first");
        Assert.That(silent.Calls.Last(), Does.EndWith(" logs --no-color"), "--keep leaves it running");
        Assert.That(silent.Calls, Has.None.StartsWith(_editor), "--skip-build reuses the player");
    }

    [Test]
    public async Task Run_stops_when_unity_writes_no_player()
    {
        var docker = new SmokeDocker(_editor) { WritesPlayer = false };
        var log = new RecordingDeployLog();

        Assert.That(await GameCommand.RunAsync(new[] { "game", "smoke", "--project", _project }, log, Services(docker), CancellationToken.None), Is.EqualTo(1));
        Assert.That(log.Lines, Has.Some.Contains("wrote no Game.x86_64"));
        Assert.That(docker.Calls, Has.None.StartsWith("docker"));
    }

    [Test]
    public async Task Dry_run_writes_the_stack_and_narrates_without_starting_anything()
    {
        var docker = new SmokeDocker(_editor);
        var log = new RecordingDeployLog();

        int exit = await GameCommand.RunAsync(new[] { "game", "smoke", "--project", _project, "--dry-run" }, log, Services(docker), CancellationToken.None);

        Assert.That(exit, Is.EqualTo(0), string.Join('\n', log.Lines));
        Assert.That(docker.Calls, Is.Empty);
        Assert.That(log.DryRunCalls, Has.Some.Contains(SmokePlan.BuildMethod));
        Assert.That(log.DryRunCalls, Has.Some.Contains("up -d"));
        Assert.That(File.Exists(Path.Combine(_project, "Builds", "Smoke", "docker-compose.yml")), Is.True, "the stack is written so it can be read");
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private SmokePlan Plan(params string[] args) => SmokePlan.Create(args, Services(new SmokeDocker(_editor)), new RecordingDeployLog());

    private GameServices Services(SmokeDocker docker) => new(_vault)
    {
        Processes = docker,
        Editors = new EditorSearchEnvironment
        {
            Platform = "windows",
            Home = Path.Combine(_root, "home"),
            HubConfigDirectory = Path.Combine(_root, "no-hub-config"),
            GetEnvironmentVariable = name => name == "UNITY_HUB_EDITORS" ? Path.Combine(_root, "Hub") : null,
        },
        Deployments = new GameDeploymentStore(Path.Combine(_root, "deployments")),
        CurrentDirectory = _root,
        PreferLinuxDocker = false,
        Delay = (_, _) => Task.CompletedTask,
    };

    private static string? After(IReadOnlyList<string> args, string option)
    {
        int i = args.ToList().IndexOf(option);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    /// <summary>The lines of <paramref name="compose"/> from <paramref name="header"/> to the next blank line.</summary>
    private static string Section(string compose, string header)
    {
        int start = compose.IndexOf(header + "\n", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), header + " missing:\n" + compose);
        int end = compose.IndexOf("\n\n", start, StringComparison.Ordinal);
        return end < 0 ? compose[start..] : compose[start..end];
    }

    /// <summary>A scripted Unity (writes the player) and docker (answers <c>compose logs</c> from <see cref="Logs"/>).</summary>
    private sealed class SmokeDocker : IProcessRunner
    {
        private readonly string _editor;
        private int _polls;

        public SmokeDocker(string editor) => _editor = editor;

        public List<string> Calls { get; } = new();
        public List<string> Secrets { get; } = new();
        public Func<int, string> Logs { get; init; } = _ => "";
        public bool WritesPlayer { get; init; } = true;

        public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, string? standardInput,
            CancellationToken ct, IReadOnlyDictionary<string, string>? environmentVariables = null)
        {
            Calls.Add(fileName + " " + string.Join(' ', arguments));
            if (environmentVariables is not null && environmentVariables.TryGetValue(SmokeStack.SecretVariable, out string? secret)) Secrets.Add(secret);
            if (fileName == _editor && WritesPlayer)
            {
                int output = arguments.ToList().IndexOf("-botOutput");
                GameCommandTests.Touch(Path.Combine(arguments[output + 1], SmokeStack.ExecutableName));
            }

            string stdout = arguments.Contains("logs") ? Logs(_polls++) : "";
            return Task.FromResult(new ProcessOutcome(0, stdout, ""));
        }
    }
}
