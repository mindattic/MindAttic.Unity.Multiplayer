namespace Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

/// <summary>
/// The slice of RunPod's GraphQL API <see cref="RunPodProvisioner"/> needs. The only seam between
/// the provisioner and the network, so NUnit mocks it directly instead of faking HTTP responses.
/// </summary>
public interface IRunPodApiClient
{
    /// <summary>Creates a pod from a GHCR image and waits for RunPod to accept the request (not for it to finish starting).</summary>
    Task<RunPodPod> CreatePodAsync(RunPodCreatePodRequest request, CancellationToken ct);

    /// <summary>The pod's current status and port mappings.</summary>
    Task<RunPodPod> GetPodAsync(string podId, CancellationToken ct);

    /// <summary>The pod's recent stdout/stderr.</summary>
    Task<string> GetPodLogsAsync(string podId, int tailLines, CancellationToken ct);

    /// <summary>Stops (but does not delete) a pod.</summary>
    Task StopPodAsync(string podId, CancellationToken ct);

    /// <summary>Permanently deletes a pod.</summary>
    Task TerminatePodAsync(string podId, CancellationToken ct);
}
