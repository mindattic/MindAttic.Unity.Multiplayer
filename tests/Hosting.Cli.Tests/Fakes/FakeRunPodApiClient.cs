using Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>Records every call <see cref="RunPodProvisioner"/> makes and answers canned pods, with no real HTTP.</summary>
public sealed class FakeRunPodApiClient : IRunPodApiClient
{
    public List<RunPodCreatePodRequest> CreateCalls { get; } = new();
    public List<string> GetCalls { get; } = new();
    public List<string> LogCalls { get; } = new();
    public List<string> StopCalls { get; } = new();
    public List<string> TerminateCalls { get; } = new();

    public RunPodPod PodToCreate { get; set; } = new("pod-1", "RUNNING", "RUNNING", "198.51.100.1", new List<RunPodPortMapping> { new(7777, 37777) });
    public Queue<RunPodPod>? GetSequence { get; set; }
    public string LogsToReturn { get; set; } = string.Empty;

    public Task<RunPodPod> CreatePodAsync(RunPodCreatePodRequest request, CancellationToken ct)
    {
        CreateCalls.Add(request);
        return Task.FromResult(PodToCreate);
    }

    public Task<RunPodPod> GetPodAsync(string podId, CancellationToken ct)
    {
        GetCalls.Add(podId);
        var pod = GetSequence is { Count: > 0 } ? GetSequence.Dequeue() : PodToCreate;
        return Task.FromResult(pod);
    }

    public Task<string> GetPodLogsAsync(string podId, int tailLines, CancellationToken ct)
    {
        LogCalls.Add(podId);
        return Task.FromResult(LogsToReturn);
    }

    public Task StopPodAsync(string podId, CancellationToken ct)
    {
        StopCalls.Add(podId);
        return Task.CompletedTask;
    }

    public Task TerminatePodAsync(string podId, CancellationToken ct)
    {
        TerminateCalls.Add(podId);
        return Task.CompletedTask;
    }
}
