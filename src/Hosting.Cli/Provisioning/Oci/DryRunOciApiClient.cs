using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

/// <summary>
/// Dry-run <see cref="IOciApiClient"/>: prints the Core Services API call it would have made and
/// returns a synthesized instance so the rest of the pipeline still has something to narrate
/// against. No network call is made and no private key is read or touched.
/// </summary>
public sealed class DryRunOciApiClient : IOciApiClient
{
    private const string SimulatedPublicIp = "203.0.113.50";
    private const string SimulatedInstanceId = "ocid1.instance.oc1..dryrun";
    private readonly IDeployLog _log;

    public DryRunOciApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<OciInstance> LaunchInstanceAsync(OciLaunchInstanceRequest request, CancellationToken ct)
    {
        _log.DryRun($"POST https://iaas.{{region}}.oraclecloud.com/20160918/instances displayName={request.DisplayName} shape={request.Shape} ad={request.AvailabilityDomain} subnet={request.SubnetId}");
        return Task.FromResult(new OciInstance(SimulatedInstanceId, "RUNNING", SimulatedPublicIp));
    }

    /// <inheritdoc />
    public Task<OciInstance> GetInstanceAsync(string instanceId, CancellationToken ct)
    {
        _log.DryRun($"GET https://iaas.{{region}}.oraclecloud.com/20160918/instances/{instanceId}");
        return Task.FromResult(new OciInstance(instanceId, "RUNNING", SimulatedPublicIp));
    }

    /// <inheritdoc />
    public Task TerminateInstanceAsync(string instanceId, CancellationToken ct)
    {
        _log.DryRun($"DELETE https://iaas.{{region}}.oraclecloud.com/20160918/instances/{instanceId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OciSecurityRule>> GetIngressSecurityRulesAsync(string securityListId, CancellationToken ct)
    {
        _log.DryRun($"GET https://iaas.{{region}}.oraclecloud.com/20160918/securityLists/{securityListId}");
        return Task.FromResult<IReadOnlyList<OciSecurityRule>>(Array.Empty<OciSecurityRule>());
    }

    /// <inheritdoc />
    public Task SetIngressSecurityRulesAsync(string securityListId, IReadOnlyList<OciSecurityRule> rules, CancellationToken ct)
    {
        _log.DryRun($"PUT https://iaas.{{region}}.oraclecloud.com/20160918/securityLists/{securityListId} rules=[{string.Join(", ", rules.Select(r => $"{r.Protocol}:{r.Port}"))}]");
        return Task.CompletedTask;
    }
}
