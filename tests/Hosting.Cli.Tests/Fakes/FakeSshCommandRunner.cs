using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

/// <summary>
/// Records every SSH call <see cref="GenericVpsProvisioner"/> makes and answers canned results by
/// command substring, so tests assert on exactly what mp-host would have run over SSH without
/// opening a socket.
/// </summary>
public sealed class FakeSshCommandRunner : ISshCommandRunner
{
    public sealed record RunCall(SshTarget Target, string Command);
    public sealed record UploadCall(SshTarget Target, string RemotePath, string Content);

    public List<RunCall> RunCalls { get; } = new();
    public List<UploadCall> UploadCalls { get; } = new();

    /// <summary>Command-substring -> result. The first match wins; otherwise a successful empty result.</summary>
    public List<(string Substring, SshCommandResult Result)> Responses { get; } = new();

    public Task<SshCommandResult> RunAsync(SshTarget target, string command, CancellationToken ct)
    {
        RunCalls.Add(new RunCall(target, command));
        foreach (var (substring, result) in Responses)
        {
            if (command.Contains(substring, StringComparison.Ordinal))
            {
                return Task.FromResult(result);
            }
        }

        return Task.FromResult(new SshCommandResult(0, string.Empty, string.Empty));
    }

    public Task UploadTextAsync(SshTarget target, string remotePath, string content, CancellationToken ct)
    {
        UploadCalls.Add(new UploadCall(target, remotePath, content));
        return Task.CompletedTask;
    }
}
