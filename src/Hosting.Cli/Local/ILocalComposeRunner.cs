using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>
/// Runs <c>docker compose</c> on the operator's own machine for <c>mp-host local up/down</c>
/// (UM-T31) -- no SSH, no remote host, the same process mp-host itself runs in. The only seam
/// between <see cref="Commands.LocalCommand"/> and a real Docker install, so NUnit mocks it directly.
/// </summary>
public interface ILocalComposeRunner
{
    /// <summary>Writes <paramref name="composeYaml"/> to <paramref name="composeFileName"/> under <paramref name="workingDirectory"/>, then <c>docker compose up -d --build</c>.</summary>
    Task UpAsync(string workingDirectory, string composeFileName, string composeYaml, IDeployLog log, CancellationToken ct);

    /// <summary><c>docker compose down -v</c> against an already-written compose file. Safe to call when the stack is already down.</summary>
    Task DownAsync(string workingDirectory, string composeFileName, IDeployLog log, CancellationToken ct);
}
