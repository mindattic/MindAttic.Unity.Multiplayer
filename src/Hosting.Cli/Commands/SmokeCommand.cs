using System.Security.Cryptography;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Game;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host game smoke</c> (UM-T57): the end-to-end smoke for any Unity.Multiplayer game, locally or in CI. Builds one
/// Linux player of the game (<see cref="SmokePlan.BuildMethod"/>), writes the stack into <c>Builds/Smoke</c> (the game
/// image's Dockerfile and a compose file with the rendezvous service, a dedicated server and N bots, see
/// <see cref="SmokeStack"/>), builds the images, starts it and waits until every bot logs
/// <c>[NetBot:&lt;role&gt;] OK:</c>. Exit 0 when they all pass, 1 when one fails or the time runs out. The compose logs
/// land in <c>Builds/Smoke/compose.log</c>.
/// </summary>
public static class SmokeCommand
{
    /// <summary>How often the bots' logs are read.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, GameServices services, CancellationToken ct)
    {
        SmokePlan plan = SmokePlan.Create(rawArgs, services, log);
        string compose = Path.Combine(plan.OutputDirectory, "docker-compose.yml");

        if (!plan.DryRun && services.PreferLinuxDocker)
        {
            await DockerEngine.PreferLinuxAsync(services.Processes, log, ct);
        }

        // 1. The Linux player every role runs.
        if (plan.Builds)
        {
            Directory.CreateDirectory(plan.OutputDirectory);
            log.Info($"==> Build the Linux player (Unity {plan.Editor!.Version}, from {plan.Editor.Source}) into {plan.PlayerDirectory}");
            string buildLog = Path.Combine(plan.OutputDirectory, "build.log");
            if (!await Run(plan, log, services, plan.Editor.ExecutablePath, plan.UnityArguments(), plan.Project.Directory, null, ct, "The Unity build", $" See {buildLog}."))
            {
                return 1;
            }

            if (!plan.DryRun && !File.Exists(Path.Combine(plan.PlayerDirectory, SmokeStack.ExecutableName)))
            {
                log.Error($"Unity finished but wrote no {SmokeStack.ExecutableName} into {plan.PlayerDirectory}; see {buildLog}.");
                return 1;
            }
        }

        // 2. The stack (written in a dry run too, so it can be read).
        Directory.CreateDirectory(plan.OutputDirectory);
        await File.WriteAllTextAsync(Path.Combine(plan.OutputDirectory, "Dockerfile"), SmokeStack.Dockerfile(), ct);
        await File.WriteAllTextAsync(Path.Combine(plan.OutputDirectory, ".dockerignore"), SmokeStack.DockerIgnore(), ct);
        await File.WriteAllTextAsync(compose, SmokeStack.Compose(plan), ct);
        log.Info($"==> Wrote the smoke stack: {compose} (rendezvous, server, {string.Join(", ", plan.Bots.Select(b => b.Service))})");

        // 3. Images.
        if (plan.RendezvousSource is not null)
        {
            log.Info($"==> Build the rendezvous image from {plan.RendezvousSource}");
            string dockerfile = Path.Combine(plan.RendezvousSource, "server", "Rendezvous", "Dockerfile");
            if (!await Run(plan, log, services, "docker", new[] { "build", "-q", "-f", dockerfile, "-t", plan.RendezvousImage, plan.RendezvousSource },
                    plan.OutputDirectory, null, ct, "The rendezvous image build", " Is Docker running?"))
            {
                return 1;
            }
        }

        log.Info($"==> Build the game image {plan.GameImage}");
        if (!await Run(plan, log, services, "docker", new[] { "build", "-q", "-t", plan.GameImage, plan.OutputDirectory },
                plan.OutputDirectory, null, ct, "The game image build", " Is Docker running?"))
        {
            return 1;
        }

        // 4. Run it and wait for every bot.
        string[] file = { "compose", "-f", compose };
        if (plan.DryRun)
        {
            log.DryRun($"docker {string.Join(' ', file)} up -d");
            log.DryRun($"docker {string.Join(' ', file)} logs --no-color   (every {PollInterval.TotalSeconds:0} s until every bot logs \"[NetBot:<role>] OK:\", up to {plan.TimeoutSeconds} s)");
            log.DryRun($"docker {string.Join(' ', file)} down -v");
            return 0;
        }

        var environment = new Dictionary<string, string>
        {
            [SmokeStack.SecretVariable] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
        };
        await services.Processes.RunAsync("docker", Compose(file, "down", "-v"), plan.OutputDirectory, null, ct, environment);
        log.Info($"==> docker compose up ({plan.ComposeProject})");
        ProcessOutcome up = await services.Processes.RunAsync("docker", Compose(file, "up", "-d"), plan.OutputDirectory, null, ct, environment);
        if (!up.Succeeded)
        {
            log.Error($"docker compose up failed (exit {up.ExitCode}):\n{up.StandardError.TrimEnd()}");
            await services.Processes.RunAsync("docker", Compose(file, "down", "-v"), plan.OutputDirectory, null, CancellationToken.None, environment);
            return 1;
        }

        int result = 1;
        string logs = "";
        try
        {
            log.Info($"==> Waiting up to {plan.TimeoutSeconds} s for {plan.Bots.Count} bot(s) to pass");
            int polls = (int)Math.Ceiling(plan.TimeoutSeconds / PollInterval.TotalSeconds);
            SmokeVerdict verdict = SmokeStack.Judge("", plan.Bots);
            for (int i = 0; i <= polls; i++)
            {
                ProcessOutcome read = await services.Processes.RunAsync("docker", Compose(file, "logs", "--no-color"), plan.OutputDirectory, null, ct, environment);
                logs = read.StandardOutput + read.StandardError;
                verdict = SmokeStack.Judge(logs, plan.Bots);
                if (verdict.Passed || verdict.Failed) break;
                if (i < polls) await services.Delay(PollInterval, ct);
            }

            foreach (string line in verdict.PassLines) log.Info(line.Trim());
            if (verdict.Passed)
            {
                log.Info($"PASS: every bot passed ({string.Join(", ", plan.Bots.Select(b => b.Role))}).");
                result = 0;
            }
            else if (verdict.Failed)
            {
                foreach (string line in verdict.FailLines) log.Error(line.Trim());
                log.Error("FAIL: a bot failed.");
            }
            else
            {
                log.Error($"FAIL: timed out after {plan.TimeoutSeconds} s waiting for bot(s) {string.Join(", ", verdict.Waiting)}.");
            }
        }
        finally
        {
            string logFile = Path.Combine(plan.OutputDirectory, "compose.log");
            await File.WriteAllTextAsync(logFile, logs, CancellationToken.None);
            log.Info($"Compose logs: {logFile}");
            if (plan.Keep)
            {
                log.Info($"Left running (--keep). Stop it with: docker compose -f \"{compose}\" down -v");
            }
            else
            {
                await services.Processes.RunAsync("docker", Compose(file, "down", "-v"), plan.OutputDirectory, null, CancellationToken.None, environment);
            }
        }

        return result;
    }

    private static string[] Compose(string[] file, params string[] verb) => file.Concat(verb).ToArray();

    private static async Task<bool> Run(SmokePlan plan, IDeployLog log, GameServices services, string executable, IReadOnlyList<string> arguments,
        string workingDirectory, IReadOnlyDictionary<string, string>? environment, CancellationToken ct, string what, string hint)
    {
        if (plan.DryRun)
        {
            log.DryRun(GamePlan.Describe(new[] { executable }.Concat(arguments).ToList()));
            return true;
        }

        ProcessOutcome outcome;
        try
        {
            outcome = await services.Processes.RunAsync(executable, arguments, workingDirectory, null, ct, environment);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.Error($"{what}: could not start '{executable}' ({ex.Message}). Is it installed and on PATH?");
            return false;
        }

        if (!outcome.Succeeded)
        {
            log.Error($"{what} failed (exit {outcome.ExitCode}).{hint}\n{outcome.StandardError.TrimEnd()}");
        }

        return outcome.Succeeded;
    }
}
