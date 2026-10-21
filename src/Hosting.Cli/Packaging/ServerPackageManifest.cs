using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Packaging;

/// <summary>One entry of <c>server-package.json</c>'s <c>ports</c> array.</summary>
public sealed record ManifestPort(string Name, int Port, string Protocol, string? Path)
{
    /// <summary>True for the generated health-check port (name <c>"health"</c>).</summary>
    public bool IsHealthPort => string.Equals(Name, "health", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads <c>server-package.json</c>, the manifest <see cref="Unity.Multiplayer.Core.ServerPackageSpec.ComposeManifest"/>
/// writes into every built server package (format 1): the profile it was built for, its
/// executable, declared ports (the game port plus an optional health port) and shutdown
/// behaviour. mp-host never re-derives any of this -- the manifest is the one contract between
/// the Unity build and the deploy CLI.
/// </summary>
public sealed record ServerPackageManifest(
    int Format,
    string Profile,
    string Executable,
    string Start,
    string Config,
    string Transport,
    IReadOnlyList<ManifestPort> Ports,
    string Logs,
    string Saves,
    string StopSignal,
    double StopGraceSeconds)
{
    /// <summary>The manifest file's name inside a built package.</summary>
    public const string FileName = "server-package.json";

    /// <summary>The declared game port (never the health port), or null if somehow absent.</summary>
    public ManifestPort? GamePort => Ports.FirstOrDefault(p => !p.IsHealthPort);

    /// <summary>The declared health port, or null when the package has none.</summary>
    public ManifestPort? HealthPort => Ports.FirstOrDefault(p => p.IsHealthPort);

    /// <summary>Parses a manifest already read into memory.</summary>
    /// <exception cref="FormatException">The JSON is not a valid server-package.json.</exception>
    public static ServerPackageManifest Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ports = new List<ManifestPort>();
            foreach (var p in root.GetProperty("ports").EnumerateArray())
            {
                ports.Add(new ManifestPort(
                    p.GetProperty("name").GetString() ?? "",
                    p.GetProperty("port").GetInt32(),
                    p.GetProperty("protocol").GetString() ?? "tcp",
                    p.TryGetProperty("path", out var path) ? path.GetString() : null));
            }

            return new ServerPackageManifest(
                root.GetProperty("format").GetInt32(),
                root.GetProperty("profile").GetString() ?? "",
                root.GetProperty("executable").GetString() ?? "",
                root.GetProperty("start").GetString() ?? "./start.sh",
                root.GetProperty("config").GetString() ?? "server.json",
                root.GetProperty("transport").GetString() ?? "udp",
                ports,
                root.GetProperty("logs").GetString() ?? "Logs",
                root.GetProperty("saves").GetString() ?? "Saves",
                root.GetProperty("stopSignal").GetString() ?? "SIGTERM",
                root.GetProperty("stopGraceSeconds").GetDouble());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"'{FileName}' is missing a required field: {ex.Message}", ex);
        }
    }

    /// <summary>Reads and parses the manifest from a built package directory.</summary>
    /// <exception cref="FileNotFoundException">The package has no <see cref="FileName"/>.</exception>
    /// <exception cref="FormatException">The JSON is not a valid server-package.json.</exception>
    public static ServerPackageManifest Load(string packageDirectory)
    {
        string path = Path.Combine(packageDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No {FileName} in '{packageDirectory}'. Build it first with " +
                "Unity's MindAttic > Multiplayer > Build Server Package menu, or mp-host deploy --unity <path>.", path);
        }

        return Parse(File.ReadAllText(path));
    }
}
