using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Docker;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Provisioning;

namespace Unity.Multiplayer.Hosting.Cli.Deploy;

/// <summary>
/// The one pipeline UM-T25 gives every provider profile: build the server package, build and push
/// its image to GHCR, provision the host, open its firewall, wait for it to go healthy, wait for
/// the self-registered join code, and hand back a summary. Every step is an injected interface, so
/// a dry run or a test substitutes every one of them without this class knowing the difference.
/// </summary>
public sealed class DeployPipeline
{
    private readonly IServerPackageBuilder _packageBuilder;
    private readonly IContainerImageTool _imageTool;
    private readonly IProvisioner _provisioner;

    public DeployPipeline(IServerPackageBuilder packageBuilder, IContainerImageTool imageTool, IProvisioner provisioner)
    {
        _packageBuilder = packageBuilder ?? throw new ArgumentNullException(nameof(packageBuilder));
        _imageTool = imageTool ?? throw new ArgumentNullException(nameof(imageTool));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    }

    /// <summary>Runs every step in order; throws on the first one that fails (image build/push, provisioning). Health and join-code waits degrade to a warning instead, since the server may simply still be starting.</summary>
    public async Task<DeployOutcome> RunAsync(
        ServerPackageBuildRequest packageRequest,
        string imageReference,
        string ghcrRegistry,
        string ghcrUsername,
        string ghcrToken,
        ProviderConnection connection,
        IReadOnlyDictionary<string, string> environmentVariables,
        string? region,
        string? size,
        TimeSpan healthTimeout,
        TimeSpan joinCodeTimeout,
        IDeployLog log,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(log);

        BuiltServerPackage package = await _packageBuilder.BuildAsync(packageRequest, log, ct);

        await _imageTool.LoginAsync(ghcrRegistry, ghcrUsername, ghcrToken, log, ct);
        await _imageTool.BuildAsync(package.Directory, imageReference, log, ct);
        await _imageTool.PushAsync(imageReference, log, ct);

        var request = new ProvisionRequest(
            connection, imageReference, ghcrRegistry, ghcrUsername, ghcrToken,
            package.Manifest, environmentVariables, region, size);

        ProvisionResult provisioned = await _provisioner.ProvisionAsync(request, log, ct);
        await _provisioner.OpenFirewallAsync(provisioned.ServerId, request, log, ct);
        bool healthy = await _provisioner.WaitForHealthyAsync(provisioned.ServerId, request, healthTimeout, log, ct);

        string? joinCode = await JoinCodeWatcher.WaitForJoinCodeAsync(
            _provisioner, provisioned.ServerId, connection, joinCodeTimeout, TimeSpan.FromSeconds(3), log, ct);

        if (joinCode is not null)
        {
            log.Info($"Join code: {joinCode}");
        }

        return new DeployOutcome(provisioned.ServerId, provisioned.PublicAddress, healthy, joinCode);
    }
}
