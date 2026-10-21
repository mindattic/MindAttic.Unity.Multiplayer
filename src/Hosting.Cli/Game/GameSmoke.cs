using System.Text;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Game;

/// <summary>One bot in the smoke stack: the role it plays (<c>-bot A</c>) and its compose service (<c>bot-a</c>).</summary>
public sealed record SmokeBot(string Role, string Service);

/// <summary>
/// Everything <c>mp-host game smoke</c> (UM-T57) resolved from a Unity project and its flags before anything runs: the
/// player build, the bots, the scenario, the images and the compose project. Pure: <see cref="Create"/> reads the
/// project folder only, so NUnit asserts on it and on the files <see cref="SmokeStack"/> renders from it.
/// </summary>
public sealed class SmokePlan
{
    /// <summary>The library's batch entry point that builds the Linux player every role runs.</summary>
    public const string BuildMethod = "Unity.Multiplayer.Editor.BotPlayerBuild.BuildFromCommandLine";

    /// <summary>The default bots: two, roles A and B.</summary>
    public const string DefaultBots = "A,B";

    /// <summary>The default seconds to wait for every bot to pass.</summary>
    public const int DefaultTimeoutSeconds = 300;

    internal static readonly HashSet<string> BooleanFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "dry-run", "skip-build", "development", "keep",
    };

    private SmokePlan(UnityProject project)
    {
        Project = project;
    }

    public UnityProject Project { get; }

    /// <summary><c>Builds/Smoke</c> under the project: the Dockerfile, compose file and logs (and the player, by default).</summary>
    public string OutputDirectory { get; private set; } = "";

    /// <summary>The Linux player folder (holding <see cref="SmokeStack.ExecutableName"/>), copied into the game image.</summary>
    public string PlayerDirectory { get; private set; } = "";

    /// <summary>True when Unity builds the player first; false with <c>--skip-build</c> or <c>--player</c>.</summary>
    public bool Builds { get; private set; }

    /// <summary>The editor the build runs, or null when nothing is built.</summary>
    public UnityEditorLocation? Editor { get; private set; }

    /// <summary>The scene the player boots (its NetBootstrap), or null for the first enabled Build Settings scene.</summary>
    public string? Scene { get; private set; }

    public IReadOnlyList<SmokeBot> Bots { get; private set; } = Array.Empty<SmokeBot>();

    /// <summary>The <c>-scenario</c> the server and every bot get, or null.</summary>
    public string? Scenario { get; private set; }

    /// <summary>More arguments for the server's launch line.</summary>
    public IReadOnlyList<string> ServerArguments { get; private set; } = Array.Empty<string>();

    /// <summary>More arguments for every bot's launch line.</summary>
    public IReadOnlyList<string> BotArguments { get; private set; } = Array.Empty<string>();

    public int TimeoutSeconds { get; private set; } = DefaultTimeoutSeconds;

    /// <summary>The compose project (and image name prefix): <c>&lt;slug&gt;-smoke</c>.</summary>
    public string ComposeProject { get; private set; } = "";

    /// <summary>The game image every role runs.</summary>
    public string GameImage { get; private set; } = "";

    /// <summary>The rendezvous image.</summary>
    public string RendezvousImage { get; private set; } = "";

    /// <summary>The Unity.Multiplayer checkout the rendezvous image is built from, or null to use <see cref="RendezvousImage"/> as is.</summary>
    public string? RendezvousSource { get; private set; }

    public bool Keep { get; private set; }

    public bool DryRun { get; private set; }

    public bool Development { get; private set; }

    /// <summary>
    /// Resolves <paramref name="rawArgs"/> (<c>game smoke --flags</c>) against the Unity project they name. Defaults come
    /// from the flags, then <c>mp-host.json</c> (<c>bots</c>, <c>scenario</c>, <c>server-args</c>, <c>bot-args</c>,
    /// <c>smoke-timeout</c>, <c>scene</c>), then two bots A and B and no scenario.
    /// </summary>
    /// <exception cref="ArgumentException">A malformed flag.</exception>
    /// <exception cref="InvalidOperationException">Not a Unity project, no Unity editor (real builds), or a missing prebuilt player.</exception>
    public static SmokePlan Create(IReadOnlyList<string> rawArgs, GameServices services, IDeployLog log)
    {
        ArgumentNullException.ThrowIfNull(rawArgs);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(log);
        var args = new ArgumentReader(rawArgs, 2, BooleanFlags);
        UnityProject project = UnityProject.Load(args.Get("project") ?? services.CurrentDirectory);
        string? Option(string option) => args.Get(option) ?? (project.Settings.TryGetValue(option, out string? value) ? value : null);

        var plan = new SmokePlan(project)
        {
            DryRun = args.Has("dry-run"),
            Keep = args.Has("keep"),
            Development = args.Has("development"),
        };

        plan.OutputDirectory = Path.GetFullPath(args.Get("output") ?? Path.Combine(project.Directory, "Builds", "Smoke"));
        plan.Bots = SmokeStack.ParseBots(Option("bots") ?? DefaultBots);
        plan.Scenario = NullIfBlank(Option("scenario"));
        plan.ServerArguments = SmokeStack.SplitArguments(Option("server-args"));
        plan.BotArguments = SmokeStack.SplitArguments(Option("bot-args"));
        string? timeout = Option("timeout") ?? Option("smoke-timeout");
        if (timeout is not null)
        {
            if (!int.TryParse(timeout, out int seconds) || seconds < 10)
            {
                throw new ArgumentException($"--timeout must be a number of seconds, at least 10; got '{timeout}'.");
            }

            plan.TimeoutSeconds = seconds;
        }

        plan.ComposeProject = project.Slug + "-smoke";
        plan.GameImage = plan.ComposeProject + ":local";

        string? player = args.Get("player");
        if (player is not null)
        {
            plan.PlayerDirectory = Path.GetFullPath(player);
            plan.Builds = false;
        }
        else
        {
            plan.PlayerDirectory = Path.Combine(plan.OutputDirectory, "player");
            plan.Builds = !args.Has("skip-build");
        }

        if (!plan.Builds && !File.Exists(Path.Combine(plan.PlayerDirectory, SmokeStack.ExecutableName)))
        {
            string message = $"There is no built player at {Path.Combine(plan.PlayerDirectory, SmokeStack.ExecutableName)}.";
            if (!plan.DryRun) throw new InvalidOperationException(message + " Run without --skip-build / --player to build one.");
            log.Warn("Dry run: " + message);
        }

        if (plan.Builds)
        {
            string? scene = Option("scene");
            if (scene is not null) plan.Scene = project.ResolveServerScene(scene).Scene;
            plan.Editor = LocateEditor(project, args.Get("unity"), services, plan.DryRun, log);
        }

        string? rendezvousImage = Option("rendezvous-image");
        string? source = args.Get("multiplayer") is { } explicitCheckout ? Path.GetFullPath(explicitCheckout) : project.MultiplayerRepository;
        if (rendezvousImage is null && source is not null && File.Exists(Path.Combine(source, "server", "Rendezvous", "Dockerfile")))
        {
            plan.RendezvousSource = source;
            plan.RendezvousImage = plan.ComposeProject + "-rendezvous:local";
        }
        else
        {
            plan.RendezvousImage = rendezvousImage ?? "ghcr.io/mindattic/rendezvous:latest";
        }

        log.Info($"Smoke test of '{project.Slug}' at {project.Directory}: a dedicated server and {plan.Bots.Count} bot(s) "
            + $"({string.Join(", ", plan.Bots.Select(b => b.Role))}){(plan.Scenario is null ? "" : $" on scenario {plan.Scenario}")}, "
            + $"waiting up to {plan.TimeoutSeconds} s{(plan.DryRun ? " (dry run)" : "")}.");
        return plan;
    }

    /// <summary>The Unity batch arguments that build the player into <see cref="PlayerDirectory"/>.</summary>
    public IReadOnlyList<string> UnityArguments()
    {
        var arguments = new List<string>
        {
            "-batchmode", "-quit", "-nographics",
            "-projectPath", Project.Directory,
            "-executeMethod", BuildMethod,
            "-botOutput", PlayerDirectory,
            "-logFile", Path.Combine(OutputDirectory, "build.log"),
        };
        if (Scene is not null) arguments.AddRange(new[] { "-botScene", Scene });
        if (Development) arguments.Add("-botDevelopment");
        return arguments;
    }

    private static UnityEditorLocation LocateEditor(UnityProject project, string? explicitPath, GameServices services, bool dryRun, IDeployLog log)
    {
        try
        {
            return UnityEditorLocator.Locate(project.Directory, explicitPath, services.Editors);
        }
        catch (Exception ex) when (dryRun && ex is InvalidOperationException or FileNotFoundException)
        {
            string version = UnityEditorLocator.ReadProjectVersion(project.Directory, services.Editors);
            log.Warn($"Dry run: {ex.Message.Split('\n')[0]} A real run needs it; narrating with a placeholder.");
            return new UnityEditorLocation(version, $"<Unity {version} editor>", "placeholder");
        }
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// The files and checks of the smoke stack (UM-T57): the game image's Dockerfile, the compose file (rendezvous + a
/// dedicated server + N bots) and the verdict read from the bots' logs. One Linux player plays every role.
/// </summary>
public static class SmokeStack
{
    /// <summary>The player executable <c>BotPlayerBuild</c> writes.</summary>
    public const string ExecutableName = "Game.x86_64";

    /// <summary>Where the server writes its join code and the bots read it, on the shared volume.</summary>
    public const string JoinCodePath = "/shared/join-code.txt";

    /// <summary>The rendezvous service's address inside the compose network.</summary>
    public const string RendezvousUrl = "http://rendezvous:8080";

    /// <summary>The environment variable that carries the rendezvous registration secret into compose.</summary>
    public const string SecretVariable = "RENDEZVOUS_SECRET";

    /// <summary>The tag a bot's log lines start with (the library's <c>BotLog.Tag</c>): <c>[NetBot:A]</c>.</summary>
    public static string Tag(string role) => $"[NetBot:{role}]";

    /// <summary>
    /// Roles from <c>--bots</c>: a comma- or space-separated list (<c>A,B</c>, <c>scout tank healer</c>) or a count
    /// (<c>3</c> gives roles 1, 2 and 3). Each gets a compose service <c>bot-&lt;role&gt;</c>.
    /// </summary>
    /// <exception cref="ArgumentException">No bots, a repeated role, a count outside 1-32, or a role with characters other than letters, digits, '-' and '_'.</exception>
    public static IReadOnlyList<SmokeBot> ParseBots(string text)
    {
        string trimmed = (text ?? "").Trim();
        List<string> roles;
        if (int.TryParse(trimmed, out int count))
        {
            if (count < 1 || count > 32) throw new ArgumentException($"--bots {count}: run between 1 and 32 bots.");
            roles = Enumerable.Range(1, count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        }
        else
        {
            roles = trimmed.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        if (roles.Count == 0) throw new ArgumentException("--bots needs at least one role (e.g. A,B) or a count.");
        var bots = new List<SmokeBot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string role in roles)
        {
            if (!role.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                throw new ArgumentException($"Bot role '{role}': use letters, digits, '-' and '_' only.");
            }

            if (!seen.Add(role)) throw new ArgumentException($"Bot role '{role}' is given twice; each bot needs its own role.");
            bots.Add(new SmokeBot(role, "bot-" + (UnityProject.ToSlug(role) ?? "x")));
        }

        if (bots.Select(b => b.Service).Distinct().Count() != bots.Count)
        {
            throw new ArgumentException("Two bot roles differ only in case or punctuation; give them distinct names.");
        }

        return bots;
    }

    /// <summary>Splits a launch line the way a shell would: on spaces, keeping "double" or 'single' quoted runs together.</summary>
    public static IReadOnlyList<string> SplitArguments(string? text)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return parts;
        var current = new StringBuilder();
        char? quote = null;
        bool any = false;
        foreach (char c in text)
        {
            if (quote is not null)
            {
                if (c == quote) quote = null;
                else current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                any = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (any || current.Length > 0) parts.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
            }
        }

        if (quote is not null) throw new ArgumentException($"Unclosed {quote} in '{text}'.");
        if (any || current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    /// <summary>
    /// The game image: Ubuntu, the player as a non-root user, <c>/shared</c> writable for the join-code file, and
    /// <c>SDL_VIDEODRIVER=dummy</c> -- a plain Linux player still polls input through SDL, and with no display SDL's
    /// default video driver crashes it (SIGSEGV in sdl::IsX11VideoDriver).
    /// </summary>
    public static string Dockerfile() => $"""
        # Generated by mp-host game smoke (Unity.Multiplayer UM-T57). Rebuilding rewrites it.
        # One Linux player (Unity.Multiplayer.Editor.BotPlayerBuild) plays every role: the dedicated server (-mode server)
        # and each bot (-bot <role>), picked by the command line in docker-compose.yml.
        FROM ubuntu:24.04
        RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
        RUN useradd --create-home --shell /bin/false game
        WORKDIR /game
        COPY player/ ./
        RUN chmod +x {ExecutableName} && mkdir -p /shared && chown -R game:game /game /shared
        USER game
        # A plain player with no display crashes in SDL's default video driver; the dummy one doesn't.
        ENV SDL_VIDEODRIVER=dummy
        ENTRYPOINT ["./{ExecutableName}"]

        """;

    /// <summary>What the image build skips: Unity's do-not-ship folders and the stack's own logs.</summary>
    public static string DockerIgnore() => """
        *.log
        player/*_BurstDebugInformation_DoNotShip
        player/*_BackUpThisFolder_ButDontShipItWithYourGame

        """;

    /// <summary>
    /// The compose file: the rendezvous service, a dedicated server that registers with it and writes its join code to
    /// the shared volume (<c>-joincodefile</c>), and one service per bot that waits for that file and joins by the code.
    /// The secret comes from <see cref="SecretVariable"/> in the environment, never the file.
    /// </summary>
    public static string Compose(SmokePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var text = new StringBuilder();
        text.Append("# Generated by mp-host game smoke (Unity.Multiplayer UM-T57). Rebuilding rewrites it.\n");
        text.Append("# A dedicated server and ").Append(plan.Bots.Count).Append(" bot(s) join by code through the rendezvous service;\n");
        text.Append("# each bot logs \"[NetBot:<role>] OK: ...\" once the game's IBotDriver passes it.\n");
        text.Append("#   ").Append(SecretVariable).Append("=dev docker compose -f docker-compose.yml up\n");
        text.Append("name: ").Append(plan.ComposeProject).Append('\n');
        text.Append("services:\n");
        text.Append("  rendezvous:\n");
        text.Append("    image: ").Append(plan.RendezvousImage).Append('\n');
        text.Append("    environment:\n");
        text.Append("      Rendezvous__RegistrationSecret: ${").Append(SecretVariable).Append(":-smoke-test-secret}\n");
        text.Append("    healthcheck:\n");
        text.Append("      test: [\"CMD\", \"dotnet\", \"Unity.Multiplayer.Rendezvous.dll\", \"--healthcheck\"]\n");
        text.Append("      interval: 5s\n      timeout: 3s\n      retries: 10\n\n");

        var server = new List<string> { "-batchmode", "-nographics", "-logFile", "-", "-mode", "server", "-port", "7777", "-transport", "udp",
            "-rendezvous", RendezvousUrl, "-joincodefile", JoinCodePath };
        if (plan.Scenario is not null) server.AddRange(new[] { "-scenario", plan.Scenario });
        server.AddRange(plan.ServerArguments);
        text.Append("  server:\n");
        text.Append("    image: ").Append(plan.GameImage).Append('\n');
        text.Append("    depends_on:\n      rendezvous:\n        condition: service_healthy\n");
        text.Append("    environment:\n");
        text.Append("      MINDATTIC_RENDEZVOUS_SECRET: ${").Append(SecretVariable).Append(":-smoke-test-secret}\n");
        AppendCommand(text, server);
        text.Append("    volumes:\n      - shared:/shared\n");

        foreach (SmokeBot bot in plan.Bots)
        {
            var command = new List<string> { "-batchmode", "-nographics", "-logFile", "-", "-bot", bot.Role, "-joincodefile", JoinCodePath,
                "-rendezvous", RendezvousUrl, "-bottimeout", plan.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (plan.Scenario is not null) command.AddRange(new[] { "-scenario", plan.Scenario });
            command.AddRange(plan.BotArguments);
            text.Append('\n');
            text.Append("  ").Append(bot.Service).Append(":\n");
            text.Append("    image: ").Append(plan.GameImage).Append('\n');
            text.Append("    depends_on:\n      - server\n");
            AppendCommand(text, command);
            text.Append("    volumes:\n      - shared:/shared\n");
        }

        text.Append("\nvolumes:\n  shared:\n");
        return text.ToString();
    }

    /// <summary>
    /// Reads the compose logs: passed once every bot logged its <c>[NetBot:role] OK:</c> line, failed as soon as any
    /// bot logged <c>FAIL:</c>, else still running.
    /// </summary>
    public static SmokeVerdict Judge(string logs, IReadOnlyList<SmokeBot> bots)
    {
        ArgumentNullException.ThrowIfNull(bots);
        string[] lines = (logs ?? "").Split('\n');
        var failures = lines.Where(l => l.Contains("[NetBot:", StringComparison.Ordinal) && l.Contains("] FAIL:", StringComparison.Ordinal))
            .Select(l => l.TrimEnd('\r')).ToList();
        var passed = new List<string>();
        var waiting = new List<string>();
        foreach (SmokeBot bot in bots)
        {
            string ok = Tag(bot.Role) + " OK:";
            string? line = lines.FirstOrDefault(l => l.Contains(ok, StringComparison.Ordinal));
            if (line is null) waiting.Add(bot.Role);
            else passed.Add(line.TrimEnd('\r'));
        }

        return new SmokeVerdict(failures.Count == 0 && waiting.Count == 0, failures.Count > 0, passed, failures, waiting);
    }

    private static void AppendCommand(StringBuilder text, IReadOnlyList<string> command)
    {
        text.Append("    command:\n");
        foreach (string part in command)
        {
            text.Append("      - ").Append(CloudInit.CloudInitRenderer.YamlQuote(part)).Append('\n');
        }
    }
}

/// <summary>Where a smoke run is: every bot passed, one failed, or still waiting on some.</summary>
public sealed record SmokeVerdict(bool Passed, bool Failed, IReadOnlyList<string> PassLines, IReadOnlyList<string> FailLines, IReadOnlyList<string> Waiting);
