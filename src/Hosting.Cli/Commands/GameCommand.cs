using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Deploy;
using Unity.Multiplayer.Hosting.Cli.Game;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host game &lt;deploy|update|local|down|status|logs|destroy&gt; [--project &lt;unity project dir&gt;] [--profile &lt;p&gt;]
/// [--scene &lt;path&gt;] [--unity &lt;exe&gt;] [--skip-build] [--dry-run] ...</c> (UM-T58): deploy any Unity game built on
/// Unity.Multiplayer with no per-game script. It reads the project (<see cref="UnityProject"/>), finds the editor its
/// <c>ProjectVersion.txt</c> names (<see cref="UnityEditorLocator"/>), picks the server scene (flag, <c>mp-host.json</c>,
/// else the first enabled Build Settings scene), fills every other value with a default (<see cref="GamePlan"/>), and
/// hands off to the existing commands -- <c>deploy</c>/<c>update</c>/<c>status</c>/<c>logs</c>/<c>destroy</c> for a
/// hosted profile, <c>local up</c>/<c>local down</c> for <c>Local</c> -- whose package step drives the library's own
/// batch entry point, <c>Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine -serverScene ...</c>.
/// </summary>
public static class GameCommand
{
    public const string Usage = """
        mp-host game <verb> [--project <unity project dir, default .>] [options]   (UM-T58: any game, no per-game script)

          game local     build the server package and run it + rendezvous on this machine (Docker)
          game down      stop the local server
          game deploy    build, push and deploy to --profile (default mp-host.json's, else GenericVps; Local = 'game local')
          game update    rebuild and roll the deployed server onto the new image
          game status | logs [--tail n] | destroy --yes     the deployed server (profile and id remembered from the
                         project's last 'game deploy' / 'game local'; --profile picks another)

        Options: --profile <GenericVps|RunPod|Hetzner|...|Local>  --scene <Assets/...unity>  --unity <editor exe>
                 --name <server>  --image <owner/repo>  --tag <t>  --public-address <addr>  --rendezvous-url <url>
                 --rendezvous-secret <s>  --server-password <p>  --env KEY=VALUE  --skip-build  --package <dir>
                 --development  --upnp  --no-rendezvous  --server-id <id>  --dry-run
        Defaults come from <project>/mp-host.json when present (e.g. {"name": "...", "image": "...", "scene": "..."}).
        The editor is ProjectVersion.txt's version from Unity Hub (or --unity / UNITY_EDITOR / UNITY_HUB_EDITORS).
        """;

    public static Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct) =>
        RunAsync(rawArgs, log, new GameServices(credentials), ct);

    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, GameServices services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rawArgs);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(services);
        if (rawArgs.Count < 2 || rawArgs[1] is "help" or "--help" or "-h")
        {
            log.Info(Usage);
            return rawArgs.Count < 2 ? 1 : 0;
        }

        GamePlan plan = GamePlan.Create(rawArgs, services, log);

        bool usesDocker = plan.IsLocal || plan.Verb is "deploy" or "update";
        if (!plan.DryRun && usesDocker && services.PreferLinuxDocker)
        {
            await DockerEngine.PreferLinuxAsync(services.Processes, log, ct);
        }

        foreach (GamePreStep step in plan.PreSteps)
        {
            log.Info($"==> {step.Description}");
            if (plan.DryRun)
            {
                log.DryRun($"{step.Executable} {GamePlan.Describe(step.Arguments)}");
                continue;
            }

            log.Info($"{step.Executable} {GamePlan.Describe(step.Arguments)}");
            ProcessOutcome outcome;
            try
            {
                outcome = await services.Processes.RunAsync(step.Executable, step.Arguments, plan.Project.Directory, null, ct);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException($"{step.Description}: could not start '{step.Executable}' ({ex.Message}). Is it installed and on PATH?");
            }

            if (!outcome.Succeeded)
            {
                throw new InvalidOperationException($"{step.Description} failed (exit {outcome.ExitCode}). Is Docker running?\n{outcome.StandardError}");
            }
        }

        if (plan.Delegated is null)
        {
            return await LocalInspectAsync(plan, log, services, ct);
        }

        log.Info($"==> mp-host {GamePlan.Describe(plan.Delegated)}");
        int exit = plan.Delegated[0] switch
        {
            "local" => await LocalCommand.RunAsync(plan.Delegated, log, services.Credentials, ct),
            "deploy" => await DeployCommand.RunAsync(plan.Delegated, log, services.Credentials, ct, outcome => Remember(plan, outcome, services)),
            "update" => await UpdateCommand.RunAsync(plan.Delegated, log, services.Credentials, ct),
            "status" => await StatusCommand.RunAsync(plan.Delegated, log, services.Credentials, ct),
            "logs" => await LogsCommand.RunAsync(plan.Delegated, log, services.Credentials, ct),
            "destroy" => await DestroyCommand.RunAsync(plan.Delegated, log, services.Credentials, ct),
            _ => throw new InvalidOperationException($"unreachable: '{plan.Delegated[0]}'"),
        };

        if (exit == 0 && !plan.DryRun)
        {
            if (plan.Verb == "destroy" && !plan.IsLocal) services.Deployments.Remove(plan.Profile, plan.Name);
            if (plan.Verb is "deploy" or "update" or "local") services.Deployments.SetLastProfile(plan.Project.Directory, plan.Profile);
            if (plan.IsLocal && plan.Delegated[1] == "up")
            {
                log.Info($"Stop it with: mp-host game down --project \"{plan.Project.Directory}\"");
            }
        }

        return exit;
    }

    private static void Remember(GamePlan plan, DeployOutcome outcome, GameServices services)
    {
        if (plan.DryRun) return;
        services.Deployments.Set(new GameDeployment(plan.Profile, plan.Name, outcome.ServerId, outcome.PublicAddress, plan.Project.Directory, DateTimeOffset.UtcNow));
    }

    /// <summary><c>game status|logs</c> on Local: <c>docker compose ps|logs</c> against the compose file <c>local up</c> wrote.</summary>
    private static async Task<int> LocalInspectAsync(GamePlan plan, IDeployLog log, GameServices services, CancellationToken ct)
    {
        HostingCredentialStore.LocalServerState? state = services.Credentials.GetLocalState(plan.Name);
        if (state is null || string.IsNullOrEmpty(state.PackageDirectory))
        {
            log.Error($"No local server '{plan.Name}' is running from this machine (start one with 'mp-host game local').");
            return 1;
        }

        var arguments = new List<string> { "compose", "-f", state.ComposeFileName };
        arguments.AddRange(plan.Verb == "logs" ? new[] { "logs", "--tail", "200" } : new[] { "ps" });
        if (plan.DryRun)
        {
            log.DryRun($"docker {string.Join(' ', arguments)} (in {state.PackageDirectory})");
            return 0;
        }

        if (services.PreferLinuxDocker) await DockerEngine.PreferLinuxAsync(services.Processes, log, ct);
        ProcessOutcome outcome = await services.Processes.RunAsync("docker", arguments, state.PackageDirectory, null, ct);
        log.Info(outcome.StandardOutput.TrimEnd());
        if (!outcome.Succeeded) log.Error(outcome.StandardError.TrimEnd());
        return outcome.Succeeded ? 0 : 1;
    }
}
