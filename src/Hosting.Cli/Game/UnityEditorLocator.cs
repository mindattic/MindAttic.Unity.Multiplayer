using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Game;

/// <summary>The machine facts <see cref="UnityEditorLocator"/> reads, injectable so tests never depend on a real Unity install.</summary>
public sealed class EditorSearchEnvironment
{
    /// <summary>Reads an environment variable (null when unset).</summary>
    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    /// <summary>True when the file exists.</summary>
    public Func<string, bool> FileExists { get; init; } = File.Exists;

    /// <summary>Reads a whole text file, or null when it is missing or unreadable.</summary>
    public Func<string, string?> ReadFile { get; init; } = path =>
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    };

    /// <summary>The OS: <c>windows</c>, <c>macos</c> or <c>linux</c>.</summary>
    public string Platform { get; init; } =
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>The user's home folder.</summary>
    public string Home { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Unity Hub's settings folder (<c>editors-v2.json</c>, <c>secondaryInstallPath.json</c>), or null when there is none.</summary>
    public string? HubConfigDirectory { get; init; } = DefaultHubConfigDirectory();

    private static string? DefaultHubConfigDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "UnityHub");
        }

        return string.IsNullOrEmpty(appData) ? null : Path.Combine(appData, "UnityHub");
    }
}

/// <summary>Where the editor came from, for the narration and error messages.</summary>
public sealed record UnityEditorLocation(string Version, string ExecutablePath, string Source);

/// <summary>
/// Finds the Unity editor a project needs (UM-T58) so no game carries its own lookup script. Order:
/// <list type="number">
/// <item><c>--unity &lt;exe&gt;</c> (the caller's explicit path), then the <c>UNITY_EDITOR</c> environment variable.</item>
/// <item>The exact version in <c>ProjectSettings/ProjectVersion.txt</c> under Unity Hub's editor folders: the
/// <c>UNITY_HUB_EDITORS</c> environment variable, Hub's own install list (<c>editors-v2.json</c>/<c>editors.json</c>,
/// which holds editors added from disk), Hub's secondary install path, then the default
/// <c>…/Unity/Hub/Editor/&lt;version&gt;</c> folder for the OS.</item>
/// </list>
/// The match is exact: a server package built by another editor version would re-import the whole project and may
/// not build the same, so a missing version is an error naming the version and the override, never a silent pick.
/// </summary>
public static class UnityEditorLocator
{
    /// <summary>The override environment variable for the editor executable.</summary>
    public const string EditorEnvVar = "UNITY_EDITOR";

    /// <summary>An extra Hub-style editors folder (<c>&lt;dir&gt;/&lt;version&gt;/…</c>) searched first.</summary>
    public const string HubEditorsEnvVar = "UNITY_HUB_EDITORS";

    /// <summary>The <c>m_EditorVersion</c> of <paramref name="projectVersionText"/> (<c>ProjectSettings/ProjectVersion.txt</c>), or null.</summary>
    public static string? ParseProjectVersion(string? projectVersionText)
    {
        if (string.IsNullOrWhiteSpace(projectVersionText)) return null;
        foreach (string raw in projectVersionText.Split('\n'))
        {
            string line = raw.Trim();
            const string key = "m_EditorVersion:";
            if (line.StartsWith(key, StringComparison.Ordinal))
            {
                string value = line[key.Length..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    /// <summary>The project's editor version from <c>&lt;project&gt;/ProjectSettings/ProjectVersion.txt</c>.</summary>
    /// <exception cref="InvalidOperationException">The file is missing or names no version (not a Unity project).</exception>
    public static string ReadProjectVersion(string projectDirectory, EditorSearchEnvironment? environment = null)
    {
        environment ??= new EditorSearchEnvironment();
        string path = Path.Combine(projectDirectory, "ProjectSettings", "ProjectVersion.txt");
        return ParseProjectVersion(environment.ReadFile(path))
            ?? throw new InvalidOperationException($"'{projectDirectory}' is not a Unity project: {path} is missing or names no m_EditorVersion. Pass --project <unity project dir>.");
    }

    /// <summary>The editor executable inside a Hub-style version folder, per OS.</summary>
    public static string ExecutableIn(string versionDirectory, string platform) => platform switch
    {
        "windows" => Path.Combine(versionDirectory, "Editor", "Unity.exe"),
        "macos" => Path.Combine(versionDirectory, "Unity.app", "Contents", "MacOS", "Unity"),
        _ => Path.Combine(versionDirectory, "Editor", "Unity"),
    };

    /// <summary>Every place <see cref="Locate"/> looks for <paramref name="version"/>, in order, with where each came from.</summary>
    public static IReadOnlyList<(string Path, string Source)> Candidates(string version, EditorSearchEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var candidates = new List<(string, string)>();

        string? hubEditors = environment.GetEnvironmentVariable(HubEditorsEnvVar);
        if (!string.IsNullOrWhiteSpace(hubEditors))
        {
            candidates.Add((ExecutableIn(Path.Combine(hubEditors, version), environment.Platform), HubEditorsEnvVar));
        }

        if (environment.HubConfigDirectory is not null)
        {
            foreach (string listed in HubListedEditors(version, environment))
            {
                candidates.Add((listed, "Unity Hub's editor list"));
            }

            string? secondary = ReadJsonString(environment.ReadFile(Path.Combine(environment.HubConfigDirectory, "secondaryInstallPath.json")));
            if (!string.IsNullOrWhiteSpace(secondary))
            {
                candidates.Add((ExecutableIn(Path.Combine(secondary, version), environment.Platform), "Unity Hub's install location"));
            }
        }

        foreach (string root in DefaultHubRoots(environment))
        {
            candidates.Add((ExecutableIn(Path.Combine(root, version), environment.Platform), "Unity Hub's default folder"));
        }

        return candidates;
    }

    /// <summary>The editor for <paramref name="projectDirectory"/>: <paramref name="explicitPath"/>, else <see cref="EditorEnvVar"/>, else the Hub install of the project's version.</summary>
    /// <exception cref="InvalidOperationException">The project's version is not installed anywhere searched (the message lists where it looked).</exception>
    /// <exception cref="FileNotFoundException">An explicit or environment path names no file.</exception>
    public static UnityEditorLocation Locate(string projectDirectory, string? explicitPath, EditorSearchEnvironment? environment = null)
    {
        environment ??= new EditorSearchEnvironment();
        string version = ReadProjectVersion(projectDirectory, environment);

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return environment.FileExists(explicitPath)
                ? new UnityEditorLocation(version, explicitPath, "--unity")
                : throw new FileNotFoundException($"--unity '{explicitPath}' does not exist.", explicitPath);
        }

        string? fromEnv = environment.GetEnvironmentVariable(EditorEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return environment.FileExists(fromEnv)
                ? new UnityEditorLocation(version, fromEnv, EditorEnvVar)
                : throw new FileNotFoundException($"{EditorEnvVar} '{fromEnv}' does not exist.", fromEnv);
        }

        IReadOnlyList<(string Path, string Source)> candidates = Candidates(version, environment);
        foreach ((string path, string source) in candidates)
        {
            if (environment.FileExists(path)) return new UnityEditorLocation(version, path, source);
        }

        throw new InvalidOperationException(
            $"Unity {version} (this project's ProjectSettings/ProjectVersion.txt) is not installed; looked in:\n  "
            + string.Join("\n  ", candidates.Select(c => c.Path).Distinct())
            + $"\nInstall it with Unity Hub (with Linux Dedicated Server Build Support), or pass --unity <editor exe> / set {EditorEnvVar}.");
    }

    private static IEnumerable<string> DefaultHubRoots(EditorSearchEnvironment environment)
    {
        switch (environment.Platform)
        {
            case "windows":
                string programFiles = environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
                yield return Path.Combine(programFiles, "Unity", "Hub", "Editor");
                break;
            case "macos":
                yield return "/Applications/Unity/Hub/Editor";
                break;
            default:
                yield return Path.Combine(environment.Home, "Unity", "Hub", "Editor");
                break;
        }
    }

    /// <summary>
    /// Editors Hub lists for <paramref name="version"/>: <c>editors-v2.json</c> (<c>{"data":[{"version","location":[..]}]}</c>)
    /// and the older <c>editors.json</c> (<c>{"&lt;version&gt;":{"location":[..]}}</c>). Read leniently: an unknown shape is no entries.
    /// </summary>
    private static IEnumerable<string> HubListedEditors(string version, EditorSearchEnvironment environment)
    {
        var found = new List<string>();
        foreach (string file in new[] { "editors-v2.json", "editors.json" })
        {
            string? text = environment.ReadFile(Path.Combine(environment.HubConfigDirectory!, file));
            if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                IEnumerable<JsonElement> entries = doc.RootElement.ValueKind != JsonValueKind.Object ? Array.Empty<JsonElement>()
                    : doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList()
                    : doc.RootElement.EnumerateObject().Select(p => p.Value).ToList();
                foreach (JsonElement entry in entries)
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    if (!entry.TryGetProperty("version", out JsonElement v) || v.ValueKind != JsonValueKind.String || v.GetString() != version) continue;
                    if (!entry.TryGetProperty("location", out JsonElement location)) continue;
                    if (location.ValueKind == JsonValueKind.String) found.Add(location.GetString()!);
                    else if (location.ValueKind == JsonValueKind.Array)
                    {
                        found.AddRange(location.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()!));
                    }
                }
            }
            catch (JsonException)
            {
                // A Hub settings file we don't understand is just no hint.
            }
        }

        return found;
    }

    private static string? ReadJsonString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
