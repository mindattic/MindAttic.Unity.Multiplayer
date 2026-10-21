using System.Diagnostics;
using System.Text;

namespace Unity.Multiplayer.Hosting.Cli.Processes;

/// <summary>Real <see cref="IProcessRunner"/>: shells out via <see cref="Process"/>.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string? standardInput,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            info.ArgumentList.Add(arg);
        }

        if (environmentVariables is not null)
        {
            foreach ((string key, string value) in environmentVariables)
            {
                info.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = info };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(ct);
        return new ProcessOutcome(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
