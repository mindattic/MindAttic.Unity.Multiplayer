namespace Unity.Multiplayer.Hosting.Cli.Cli;

/// <summary>
/// Where every pipeline step, provider call and dry-run echo is written. Swapped for a recording
/// fake in tests so a whole deploy can be asserted on without touching a console.
/// </summary>
public interface IDeployLog
{
    /// <summary>A normal progress line ("Pushing ghcr.io/...").</summary>
    void Info(string message);

    /// <summary>Something non-fatal worth the operator's attention (a best-effort step that failed).</summary>
    void Warn(string message);

    /// <summary>A fatal problem; the pipeline stops after this.</summary>
    void Error(string message);

    /// <summary>
    /// What dry-run mode prints instead of making the call: "[dry-run] docker push ...",
    /// "[dry-run] POST https://api.runpod.io/graphql {...}". One line per API call, verbatim
    /// enough that an operator could paste it back by hand.
    /// </summary>
    void DryRun(string call);
}
