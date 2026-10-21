namespace Unity.Multiplayer.Hosting.Cli.Cli;

/// <summary>Writes to the process's standard streams: info/dry-run to stdout, warnings/errors to stderr.</summary>
public sealed class ConsoleDeployLog : IDeployLog
{
    /// <inheritdoc />
    public void Info(string message) => Console.Out.WriteLine(message);

    /// <inheritdoc />
    public void Warn(string message) => Console.Error.WriteLine("warning: " + message);

    /// <inheritdoc />
    public void Error(string message) => Console.Error.WriteLine("error: " + message);

    /// <inheritdoc />
    public void DryRun(string call) => Console.Out.WriteLine("[dry-run] " + call);
}
