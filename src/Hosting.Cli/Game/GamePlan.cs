using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Game;

/// <summary>An external command <c>mp-host game</c> runs before handing off (today: building the rendezvous image from a local Unity.Multiplayer checkout).</summary>
public sealed record GamePreStep(string Description, string Executable, IReadOnlyList<string> Arguments);

/// <summary>
/// Everything <c>mp-host game &lt;verb&gt;</c> resolved from a Unity project and its flags (UM-T58), before anything
/// runs: the existing mp-host command line it hands off to (<see cref="Delegated"/>) and any <see cref="PreSteps"/>.
/// Pure: <see cref="Create"/> reads the project folder and the vault but starts no process, so NUnit asserts on it.
/// </summary>
public sealed class GamePlan
{
    /// <summary>The verbs <c>mp-host game</c> takes.</summary>
    public static readonly IReadOnlyList<string> Verbs = new[] { "deploy", "update", "local", "down", "status", "logs", "destroy" };

    /// <summary>The profile name that means "this machine" (<c>mp-host local</c>), not a hosted provider.</summary>
    public const string LocalProfile = "Local";

    /// <summary>The profile a project that has never deployed (and names none in <c>mp-host.json</c>) uses.</summary>
    public const string DefaultProfile = "GenericVps";

    /// <summary>The image <c>game local</c> tags a rendezvous built from a local Unity.Multiplayer checkout.</summary>
    public const string LocalRendezvousImage = "mindattic-rendezvous:local";

    /// <summary>Inside the local compose network the game server reaches rendezvous by its service name.</summary>
    public const string LocalRendezvousUrl = "http://rendezvous:8080";

    /// <summary>The placeholder address a hosted dry run uses when none is known (TEST-NET-3, never routable).</summary>
    public const string PlaceholderAddress = "203.0.113.5";

    internal static readonly HashSet<string> BooleanFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "dry-run", "skip-build", "development", "upnp", "no-rendezvous", "yes",
    };

    private static readonly string[] PassThrough =
    {
        "server-password", "region", "size", "health-timeout", "joincode-timeout", "tail",
    };

    private GamePlan(string verb, UnityProject project, string profile, string name, bool dryRun)
    {
        Verb = verb;
        Project = project;
        Profile = profile;
        Name = name;
        DryRun = dryRun;
    }

    public string Verb { get; }
    public UnityProject Project { get; }

    /// <summary>The hosting profile: <see cref="LocalProfile"/> or a provider id (GenericVps, RunPod, Hetzner, ...).</summary>
    public string Profile { get; }

    /// <summary>True for <see cref="LocalProfile"/>.</summary>
    public bool IsLocal => string.Equals(Profile, LocalProfile, StringComparison.OrdinalIgnoreCase);

    /// <summary>The server's name (vault key for its SSH target / local state, and the deployment record's key).</summary>
    public string Name { get; }

    public bool DryRun { get; }

    /// <summary>The Unity editor the build uses, or null when no build runs.</summary>
    public UnityEditorLocation? Editor { get; private set; }

    /// <summary>The server scene and where it came from, or null when no build runs.</summary>
    public (string Scene, string Source)? Scene { get; private set; }

    /// <summary>The package folder the build writes (or an existing one is reused from), or null when no package is involved.</summary>
    public string? PackageDirectory { get; private set; }

    /// <summary>True when an existing package is reused instead of running Unity.</summary>
    public bool ReusesPackage { get; private set; }

    /// <summary>Commands to run before <see cref="Delegated"/>.</summary>
    public List<GamePreStep> PreSteps { get; } = new();

    /// <summary>The existing mp-host command line (<c>deploy ...</c>, <c>local up ...</c>, ...) this plan hands off to; null for local status/logs, which <c>game</c> runs itself.</summary>
    public List<string>? Delegated { get; private set; }

    /// <summary>Known server id for update/status/logs/destroy (flag or remembered deployment).</summary>
    public string? ServerId { get; private set; }

    /// <summary>
    /// Resolves <paramref name="rawArgs"/> (<c>game &lt;verb&gt; --flags</c>) against the Unity project they name.
    /// </summary>
    /// <exception cref="ArgumentException">An unknown verb, or a flag that conflicts with the verb.</exception>
    /// <exception cref="InvalidOperationException">Not a Unity project, no server scene, no Unity editor (real runs), or a required value that has no default.</exception>
    public static GamePlan Create(IReadOnlyList<string> rawArgs, GameServices services, IDeployLog log)
    {
        ArgumentNullException.ThrowIfNull(rawArgs);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(log);
        if (rawArgs.Count < 2 || !Verbs.Contains(rawArgs[1], StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Usage: mp-host game <{string.Join('|', Verbs)}> [--project <unity project dir>] [--profile <p>] [--scene <path>] [--dry-run] ...");
        }

        string verb = rawArgs[1].ToLowerInvariant();
        var args = new ArgumentReader(rawArgs, 2, BooleanFlags);
        UnityProject project = UnityProject.Load(args.Get("project") ?? services.CurrentDirectory);
        string? Option(string option) => args.Get(option) ?? (project.Settings.TryGetValue(option, out string? value) ? value : null);
        bool Flag(string option) => args.Has(option) || (project.Settings.TryGetValue(option, out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        string profile;
        if (verb is "local" or "down")
        {
            string? asked = args.Get("profile");
            if (asked is not null && !string.Equals(asked, LocalProfile, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(verb == "down"
                    ? $"'game down' stops the Local server; tear a {asked} server down with 'mp-host game destroy --profile {asked} --yes'."
                    : $"'game local' always runs on this machine; use 'mp-host game deploy --profile {asked}' for a hosted server.");
            }

            profile = LocalProfile;
        }
        else
        {
            // UM-T61: a bare status/logs/update/destroy looks at the profile this project last deployed with (so after
            // 'game local' it inspects the local server, not a VPS that doesn't exist). --profile always wins; deploy
            // itself never guesses, it takes --profile, mp-host.json, else GenericVps.
            string? remembered = verb == "deploy" || args.Get("profile") is not null ? null : services.Deployments.LastProfile(project.Directory);
            profile = args.Get("profile") ?? remembered ?? Option("profile") ?? DefaultProfile;
            if (remembered is not null && args.Get("profile") is null)
            {
                log.Info($"Profile {remembered}: the one this project last deployed with (pass --profile to pick another).");
            }

            if (string.Equals(profile, LocalProfile, StringComparison.OrdinalIgnoreCase)) profile = LocalProfile;
        }

        string baseName = Option("name") ?? project.Slug;
        bool local = string.Equals(profile, LocalProfile, StringComparison.OrdinalIgnoreCase);
        string name = local ? args.Get("name") ?? baseName + "-local" : baseName;
        bool dryRun = Flag("dry-run");
        var plan = new GamePlan(verb, project, profile, name, dryRun);

        log.Info($"Game '{project.Slug}' at {project.Directory}: {verb} on {profile} as '{name}'{(dryRun ? " (dry run)" : string.Empty)}.");

        if (local)
        {
            plan.PlanLocal(verb, args, Option, Flag, services, log);
        }
        else
        {
            plan.PlanHosted(verb, args, Option, Flag, services, log);
        }

        return plan;
    }

    private void PlanLocal(string verb, ArgumentReader args, Func<string, string?> option, Func<string, bool> flag, GameServices services, IDeployLog log)
    {
        switch (verb)
        {
            case "deploy" or "local" or "update":
            {
                var up = new List<string> { "local", "up", "--name", Name };
                up.AddRange(PackageArguments(args, option, flag, services, log));
                if (!flag("no-rendezvous"))
                {
                    up.AddRange(new[] { "--rendezvous-url", option("rendezvous-url") ?? LocalRendezvousUrl });
                    AddIfSet(up, "rendezvous-secret", option);
                    string? image = option("rendezvous-image");
                    if (image is null && Project.MultiplayerRepository is not null)
                    {
                        image = LocalRendezvousImage;
                        string repository = Project.MultiplayerRepository;
                        PreSteps.Add(new GamePreStep(
                            $"Build the rendezvous image from Unity.Multiplayer ({repository})",
                            "docker",
                            new[] { "build", "-f", Path.Combine(repository, "server", "Rendezvous", "Dockerfile"), "-t", image, repository }));
                    }

                    if (image is not null) up.AddRange(new[] { "--rendezvous-image", image });
                }

                AddIfSet(up, "public-address", option);
                if (!flag("upnp")) up.Add("--no-upnp");
                AddCommon(up, option, args);
                Delegated = up;
                break;
            }

            case "down" or "destroy":
                Delegated = new List<string> { "local", "down", "--name", Name };
                if (DryRun) Delegated.Add("--dry-run");
                break;

            default: // status, logs: 'game' runs docker compose itself against the stored local state
                Delegated = null;
                break;
        }
    }

    private void PlanHosted(string verb, ArgumentReader args, Func<string, string?> option, Func<string, bool> flag, GameServices services, IDeployLog log)
    {
        if (verb is "local" or "down") throw new InvalidOperationException("unreachable: local verbs are planned by PlanLocal");

        if (verb != "deploy")
        {
            ServerId = option("server-id") ?? services.Deployments.Get(Profile, Name)?.ServerId;
            if (ServerId is null)
            {
                if (!DryRun)
                {
                    throw new InvalidOperationException(
                        $"No server id for '{Name}' on {Profile}: none was remembered from a 'mp-host game deploy' on this machine. Pass --server-id <id>.");
                }

                ServerId = "<server-id>";
                log.Warn($"Dry run: no remembered {Profile} deployment of '{Name}'; using the placeholder server id {ServerId}.");
            }
        }

        switch (verb)
        {
            case "deploy" or "update":
            {
                var command = new List<string> { verb, "--profile", Profile, "--name", Name };
                if (verb == "update") command.AddRange(new[] { "--server-id", ServerId! });
                command.AddRange(PackageArguments(args, option, flag, services, log));

                string image = option("image") ?? $"{ImageOwner(services.Credentials)}/{Project.Slug}";
                command.AddRange(new[] { "--image", image, "--tag", option("tag") ?? "latest" });

                string publicAddress = option("public-address") ?? DefaultPublicAddress(services.Credentials, log);
                string rendezvousUrl = option("rendezvous-url") ?? DefaultRendezvousUrl(publicAddress, log);
                command.AddRange(new[] { "--rendezvous-url", rendezvousUrl });
                AddIfSet(command, "rendezvous-secret", option);
                command.AddRange(new[] { "--public-address", publicAddress });
                AddCommon(command, option, args);
                Delegated = command;
                break;
            }

            case "status" or "logs":
                Delegated = new List<string> { verb, "--profile", Profile, "--name", Name, "--server-id", ServerId! };
                if (verb == "logs") AddIfSet(Delegated, "tail", option);
                if (DryRun) Delegated.Add("--dry-run");
                break;

            case "destroy":
                Delegated = new List<string> { "destroy", "--profile", Profile, "--name", Name, "--server-id", ServerId! };
                if (args.Has("yes")) Delegated.Add("--yes"); // never from mp-host.json: destroying is always asked for on the command line
                if (DryRun) Delegated.Add("--dry-run");
                break;
        }
    }

    /// <summary>The package flags for the delegated command: <c>--package</c> to reuse one, else the Unity build flags.</summary>
    private IEnumerable<string> PackageArguments(ArgumentReader args, Func<string, string?> option, Func<string, bool> flag, GameServices services, IDeployLog log)
    {
        string packageProfile = PackageProfiles.For(IsLocal ? PackageProfiles.GenericVps : Profile);
        string? package = args.Get("package");
        if (package is not null)
        {
            PackageDirectory = package;
            ReusesPackage = true;
            return new[] { "--package", package };
        }

        string defaultPackage = Path.Combine(Project.Directory, "Builds", "ServerPackage", packageProfile);
        if (flag("skip-build"))
        {
            if (File.Exists(Path.Combine(defaultPackage, ServerPackageManifest.FileName)))
            {
                PackageDirectory = defaultPackage;
                ReusesPackage = true;
                return new[] { "--package", defaultPackage };
            }

            if (!DryRun)
            {
                throw new InvalidOperationException($"--skip-build: there is no built package at {defaultPackage}. Run without --skip-build to build one.");
            }

            log.Warn($"Dry run: --skip-build but no package at {defaultPackage}; narrating the build instead.");
        }

        Scene = Project.ResolveServerScene(option("scene"));
        Editor = LocateEditor(option("unity"), services, log);
        PackageDirectory = defaultPackage;
        log.Info($"Unity {Editor.Version}: {Editor.ExecutablePath} (from {Editor.Source}); server scene {Scene.Value.Scene} (from {Scene.Value.Source}).");

        var build = new List<string>
        {
            "--unity", Editor.ExecutablePath,
            "--unity-project", Project.Directory,
            "--scene", Scene.Value.Scene,
            "--package-output", defaultPackage,
            "--unity-log", Path.Combine(Project.Directory, "Builds", "ServerPackage", $"build-{packageProfile}.log"),
        };
        if (flag("development")) build.Add("--development");
        return build;
    }

    private UnityEditorLocation LocateEditor(string? explicitPath, GameServices services, IDeployLog log)
    {
        try
        {
            return UnityEditorLocator.Locate(Project.Directory, explicitPath, services.Editors);
        }
        catch (Exception ex) when (DryRun && ex is InvalidOperationException or FileNotFoundException)
        {
            string version = UnityEditorLocator.ReadProjectVersion(Project.Directory, services.Editors);
            log.Warn($"Dry run: {ex.Message.Split('\n')[0]} A real run needs it; narrating with a placeholder.");
            return new UnityEditorLocation(version, $"<Unity {version} editor>", "placeholder");
        }
    }

    private string ImageOwner(HostingCredentialStore credentials)
    {
        string? user = credentials.GetGhcrCreds()?.Username;
        return string.IsNullOrWhiteSpace(user) ? "<ghcr-user>" : user.ToLowerInvariant();
    }

    private string DefaultPublicAddress(HostingCredentialStore credentials, IDeployLog log)
    {
        if (string.Equals(Profile, PackageProfiles.RunPod, StringComparison.OrdinalIgnoreCase))
        {
            return Name; // cosmetic on RunPod: the pod advertises RUNPOD_PUBLIC_IP itself
        }

        if (string.Equals(Profile, "GenericVps", StringComparison.OrdinalIgnoreCase))
        {
            string? host = credentials.GetSshCreds(Name)?.Host;
            if (!string.IsNullOrWhiteSpace(host)) return host;
        }

        if (DryRun)
        {
            log.Warn($"Dry run: no --public-address; using the placeholder {PlaceholderAddress}.");
            return PlaceholderAddress;
        }

        throw new InvalidOperationException(
            $"--public-address (the address players reach the {Profile} server at) is required"
            + (string.Equals(Profile, "GenericVps", StringComparison.OrdinalIgnoreCase)
                ? $"; or store the box with 'mp-host credentials set-ssh --name {Name} --host <ip> ...' and it defaults to that host."
                : "."));
    }

    private string DefaultRendezvousUrl(string publicAddress, IDeployLog log)
    {
        if (!string.Equals(Profile, PackageProfiles.RunPod, StringComparison.OrdinalIgnoreCase))
        {
            return $"http://{publicAddress}:8080"; // the rendezvous container runs beside the game on the box
        }

        if (DryRun)
        {
            log.Warn("Dry run: no --rendezvous-url; using the placeholder http://rendezvous.example.com:8080.");
            return "http://rendezvous.example.com:8080";
        }

        throw new InvalidOperationException("--rendezvous-url is required for RunPod: the pod runs no rendezvous of its own (host one with --profile GenericVps).");
    }

    private void AddCommon(List<string> command, Func<string, string?> option, ArgumentReader args)
    {
        foreach (string name in PassThrough)
        {
            if (name == "tail") continue;
            AddIfSet(command, name, option);
        }

        foreach (KeyValuePair<string, string> env in EnvPairs(args))
        {
            command.AddRange(new[] { "--env", $"{env.Key}={env.Value}" });
        }

        if (DryRun) command.Add("--dry-run");
    }

    private static void AddIfSet(List<string> command, string name, Func<string, string?> option)
    {
        string? value = option(name);
        if (value is not null) command.AddRange(new[] { "--" + name, value });
    }

    /// <summary>Every repeated <c>--env KEY=VALUE</c> of the raw command line (the reader keeps only the last).</summary>
    private IEnumerable<KeyValuePair<string, string>> EnvPairs(ArgumentReader args) => args.Repeated("env").Select(pair =>
    {
        int eq = pair.IndexOf('=');
        if (eq <= 0) throw new ArgumentException($"--env '{pair}' must be KEY=VALUE.");
        return new KeyValuePair<string, string>(pair[..eq], pair[(eq + 1)..]);
    });

    /// <summary><paramref name="command"/> as one printable line with secrets masked.</summary>
    public static string Describe(IReadOnlyList<string> command)
    {
        var parts = new List<string>();
        for (int i = 0; i < command.Count; i++)
        {
            string part = command[i];
            parts.Add(part.Contains(' ') ? $"\"{part}\"" : part);
            if (part is "--rendezvous-secret" or "--server-password" && i + 1 < command.Count)
            {
                parts.Add("***");
                i++;
            }
        }

        return string.Join(' ', parts);
    }
}
