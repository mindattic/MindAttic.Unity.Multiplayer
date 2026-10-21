using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Composition;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Deploy;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host deploy --profile &lt;GenericVps|RunPod|...|GameLift&gt; --name &lt;server&gt; --image &lt;owner/repo&gt;
/// [--tag &lt;tag&gt;] (--package &lt;dir&gt; | --unity &lt;exe&gt; --unity-project &lt;dir&gt;)
/// --rendezvous-url &lt;url&gt; --rendezvous-secret &lt;secret&gt; --public-address &lt;addr&gt;
/// [--server-password &lt;pwd&gt;] [--env KEY=VALUE ...] [--region &lt;r&gt;] [--size &lt;s&gt;]
/// [--health-timeout &lt;seconds&gt;] [--joincode-timeout &lt;seconds&gt;] [--dry-run]</c>
///
/// Runs the full pipeline and prints the join code.
/// </summary>
public static class DeployCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct)
    {
        ArgumentReader args = DeployArguments.Parse(rawArgs);
        string profile = args.Require("profile");
        string serverName = args.Require("name");

        var composer = new HostingComposer(credentials, args.Has("dry-run"));
        var ghcr = composer.RequireGhcrCreds(log);
        string imageReference = DeployArguments.BuildImageReference(args, composer.GhcrRegistry);
        var environment = DeployArguments.BuildEnvironment(args, rawArgs);
        var connection = composer.BuildConnection(profile, serverName, log);

        var pipeline = new DeployPipeline(composer.BuildPackageBuilder(), composer.BuildImageTool(log), composer.BuildProvisioner(profile, log));
        DeployOutcome outcome = await pipeline.RunAsync(
            DeployArguments.BuildPackageRequest(args, profile),
            imageReference,
            composer.GhcrRegistry,
            ghcr.Username,
            ghcr.Token,
            connection,
            environment,
            args.Get("region"),
            args.Get("size"),
            DeployArguments.HealthTimeout(args),
            DeployArguments.JoinCodeTimeout(args),
            log,
            ct);

        log.Info($"Server id: {outcome.ServerId}");
        log.Info($"Address: {outcome.PublicAddress}");
        log.Info($"Healthy: {outcome.Healthy}");
        log.Info(outcome.JoinCode is not null ? $"Join code: {outcome.JoinCode}" : "Join code: (not seen yet -- check 'mp-host logs')");
        return outcome.Healthy ? 0 : 1;
    }
}
