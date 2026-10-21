using System.Text.Json;
using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

/// <summary>
/// Dry-run <see cref="IRunPodApiClient"/>: prints the GraphQL call it would have made and returns
/// a synthesized pod so the rest of the pipeline (health check, join-code wait, printed summary)
/// still has something to narrate against. No network call is made and no API key is read.
/// </summary>
public sealed class DryRunRunPodApiClient : IRunPodApiClient
{
    private const string SimulatedId = "dryrun-pod-id";
    private const string SimulatedIp = "203.0.113.10";

    private readonly IDeployLog _log;

    public DryRunRunPodApiClient(IDeployLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task<RunPodPod> CreatePodAsync(RunPodCreatePodRequest request, CancellationToken ct)
    {
        var body = new { request.Name, request.ImageName, request.ContainerDiskInGb, ports = request.TcpPorts };
        _log.DryRun($"POST https://api.runpod.io/graphql mutation podFindAndDeployOnDemand {JsonSerializer.Serialize(body)}");
        var mappings = request.TcpPorts.Select(p => new RunPodPortMapping(p, p)).ToList();
        return Task.FromResult(new RunPodPod(SimulatedId, "RUNNING", "RUNNING", SimulatedIp, mappings));
    }

    /// <inheritdoc />
    public Task<RunPodPod> GetPodAsync(string podId, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.runpod.io/graphql query pod {{ podId: \"{podId}\" }}");
        return Task.FromResult(new RunPodPod(podId, "RUNNING", "RUNNING", SimulatedIp, new List<RunPodPortMapping>()));
    }

    /// <inheritdoc />
    public Task<string> GetPodLogsAsync(string podId, int tailLines, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.runpod.io/graphql query podLogs {{ podId: \"{podId}\", tail: {tailLines} }}");
        return Task.FromResult($"[MindAttic.Multiplayer] Join code: SIMU-LATE-DRYR\n");
    }

    /// <inheritdoc />
    public Task StopPodAsync(string podId, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.runpod.io/graphql mutation podStop {{ podId: \"{podId}\" }}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TerminatePodAsync(string podId, CancellationToken ct)
    {
        _log.DryRun($"POST https://api.runpod.io/graphql mutation podTerminate {{ podId: \"{podId}\" }}");
        return Task.CompletedTask;
    }
}
