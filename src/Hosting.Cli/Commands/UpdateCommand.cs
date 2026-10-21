using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Composition;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Docker;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Provisioning;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// <c>mp-host update --profile &lt;p&gt; --name &lt;server&gt; --server-id &lt;id&gt; --image &lt;owner/repo&gt;
/// [--tag &lt;tag&gt;] (--package &lt;dir&gt; | --unity ... --unity-project ...)
/// --rendezvous-url ... --rendezvous-secret ... --public-address ... [--env KEY=VALUE ...] [--dry-run]</c>
///
/// Rebuilds and re-pushes the image, then asks the provisioner for a rolling restart onto it.
/// </summary>
public static class UpdateCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> rawArgs, IDeployLog log, HostingCredentialStore credentials, CancellationToken ct)
    {
        ArgumentReader args = DeployArguments.Parse(rawArgs);
        string profile = args.Require("profile");
        string serverName = args.Require("name");
        string serverId = args.Require("server-id");

        var composer = new HostingComposer(credentials, args.Has("dry-run"));
        var ghcr = composer.RequireGhcrCreds();
        string imageReference = DeployArguments.BuildImageReference(args, composer.GhcrRegistry);
        var environment = DeployArguments.BuildEnvironment(args, rawArgs);
        var connection = composer.BuildConnection(profile, serverName);

        IServerPackageBuilder packageBuilder = composer.BuildPackageBuilder();
        IContainerImageTool imageTool = composer.BuildImageTool(log);
        IProvisioner provisioner = composer.BuildProvisioner(profile, log);

        var package = await packageBuilder.BuildAsync(DeployArguments.BuildPackageRequest(args, profile), log, ct);
        await imageTool.LoginAsync(composer.GhcrRegistry, ghcr.Username, ghcr.Token, log, ct);
        await imageTool.BuildAsync(package.Directory, imageReference, log, ct);
        await imageTool.PushAsync(imageReference, log, ct);

        var request = new ProvisionRequest(connection, imageReference, composer.GhcrRegistry, ghcr.Username, ghcr.Token,
            package.Manifest, environment, args.Get("region"), args.Get("size"));
        await provisioner.UpdateAsync(serverId, request, log, ct);
        log.Info($"{serverId} updated to {imageReference}.");
        return 0;
    }
}
