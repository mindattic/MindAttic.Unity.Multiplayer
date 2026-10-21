namespace Unity.Multiplayer.Hosting.Cli.Cli;

/// <summary>
/// An in-memory <see cref="IDeployLog"/> that keeps every line it was given, in order, prefixed by
/// its kind. Tests assert on <see cref="Lines"/> (e.g. a dry-run deploy prints the GHCR push and
/// the RunPod GraphQL mutation, never calls either for real) instead of capturing stdout.
/// </summary>
public sealed class RecordingDeployLog : IDeployLog
{
    private readonly List<string> _lines = new();

    /// <summary>Every line written so far, in order, as "info: ...", "warn: ...", "error: ..." or "dry-run: ...".</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Only the dry-run echoes, in order, with the "[dry-run] " marker stripped.</summary>
    public IReadOnlyList<string> DryRunCalls =>
        _lines.Where(l => l.StartsWith("dry-run: ", StringComparison.Ordinal))
              .Select(l => l["dry-run: ".Length..])
              .ToList();

    /// <inheritdoc />
    public void Info(string message) => _lines.Add("info: " + message);

    /// <inheritdoc />
    public void Warn(string message) => _lines.Add("warn: " + message);

    /// <inheritdoc />
    public void Error(string message) => _lines.Add("error: " + message);

    /// <inheritdoc />
    public void DryRun(string call) => _lines.Add("dry-run: " + call);
}
