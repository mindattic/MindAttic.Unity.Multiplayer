using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class DryRunSshCommandRunnerTests
{
    private static readonly SshTarget Target = new("203.0.113.9", 22, "root", "/key", null);

    [Test]
    public async Task RunAsync_simulates_a_healthy_probe_so_a_dry_run_does_not_block_for_the_real_timeout()
    {
        var runner = new DryRunSshCommandRunner(new RecordingDeployLog());
        SshCommandResult result = await runner.RunAsync(
            Target, "curl -sf -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/healthz", CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.StandardOutput.Trim(), Is.EqualTo("200"));
    }

    [Test]
    public async Task RunAsync_simulates_a_join_code_log_line_so_a_dry_run_does_not_block_waiting_for_one()
    {
        var runner = new DryRunSshCommandRunner(new RecordingDeployLog());
        SshCommandResult result = await runner.RunAsync(
            Target, "docker compose -f docker-compose.yml logs --no-color --tail 200 game", CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.StandardOutput, Does.Contain("[MindAttic.Multiplayer] Join code:"));
    }

    [Test]
    public async Task RunAsync_simulates_the_game_service_running_so_a_dry_run_status_check_reports_running()
    {
        var runner = new DryRunSshCommandRunner(new RecordingDeployLog());
        SshCommandResult result = await runner.RunAsync(
            Target, "docker compose -f docker-compose.yml ps --status running --services", CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Has.Some.EqualTo("game"));
    }

    [Test]
    public async Task RunAsync_echoes_the_command_and_returns_an_otherwise_empty_success()
    {
        var log = new RecordingDeployLog();
        var runner = new DryRunSshCommandRunner(log);
        SshCommandResult result = await runner.RunAsync(Target, "docker compose -f docker-compose.yml pull", CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.StandardOutput, Is.Empty);
        Assert.That(log.DryRunCalls, Has.Some.Matches<string>(c => c.Contains("docker compose -f docker-compose.yml pull")));
    }
}
