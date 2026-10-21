using System.Text;
using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Game;

/// <summary>
/// What <c>mp-host game</c> reads from a Unity project so the game needs no deploy script (UM-T58): its product
/// name (the default server and image name), its enabled Build Settings scenes (the default server scene), the
/// optional <c>mp-host.json</c> defaults file, and where the Unity.Multiplayer checkout is when the package is
/// referenced by <c>file:</c> path (to build the rendezvous image locally). Read-only: nothing here writes the project.
/// </summary>
public sealed class UnityProject
{
    /// <summary>The optional per-game defaults file at the project root.</summary>
    public const string SettingsFileName = "mp-host.json";

    /// <summary>The Unity package id the library ships as.</summary>
    public const string PackageId = "com.mindattic.unity.multiplayer";

    private UnityProject(string directory, string? productName, IReadOnlyList<string> buildScenes, IReadOnlyDictionary<string, string> settings, string? multiplayerRepository)
    {
        Directory = directory;
        ProductName = productName;
        BuildScenes = buildScenes;
        Settings = settings;
        MultiplayerRepository = multiplayerRepository;
    }

    /// <summary>The project folder, full path.</summary>
    public string Directory { get; }

    /// <summary><c>productName</c> from <c>ProjectSettings/ProjectSettings.asset</c>, or null.</summary>
    public string? ProductName { get; }

    /// <summary>The enabled scenes of <c>ProjectSettings/EditorBuildSettings.asset</c>, in Build Settings order.</summary>
    public IReadOnlyList<string> BuildScenes { get; }

    /// <summary>
    /// The defaults in <see cref="SettingsFileName"/>: option name (as on the command line, without <c>--</c>) to
    /// value, e.g. <c>{"name": "cursory", "image": "mindattic/unity-cursory", "scene": "Assets/Game/Main.unity"}</c>.
    /// Empty when the file is absent. A command-line option always wins.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>The Unity.Multiplayer repository the project's manifest points at by <c>file:</c> path (holds <c>server/Rendezvous</c>), or null.</summary>
    public string? MultiplayerRepository { get; }

    /// <summary>A docker- and DNS-safe name for the game: the product name (else the folder name), lower case, <c>[a-z0-9-]</c>.</summary>
    public string Slug => ToSlug(ProductName) ?? ToSlug(Path.GetFileName(Directory.TrimEnd('/', '\\'))) ?? "game";

    /// <summary>Reads <paramref name="projectDirectory"/>.</summary>
    /// <exception cref="InvalidOperationException">The folder is not a Unity project, or <see cref="SettingsFileName"/> is not a JSON object of strings/numbers/booleans.</exception>
    public static UnityProject Load(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string directory = Path.GetFullPath(projectDirectory);
        if (!File.Exists(Path.Combine(directory, "ProjectSettings", "ProjectVersion.txt")))
        {
            throw new InvalidOperationException(
                $"'{directory}' is not a Unity project (no ProjectSettings/ProjectVersion.txt). Pass --project <unity project dir>, or run from inside one.");
        }

        return new UnityProject(
            directory,
            ParseProductName(ReadOrNull(Path.Combine(directory, "ProjectSettings", "ProjectSettings.asset"))),
            ParseBuildScenes(ReadOrNull(Path.Combine(directory, "ProjectSettings", "EditorBuildSettings.asset"))),
            ParseSettings(ReadOrNull(Path.Combine(directory, SettingsFileName))),
            FindMultiplayerRepository(directory));
    }

    /// <summary>
    /// The scene the server boots and Unity opens before building, and why: <paramref name="explicitScene"/>
    /// (<c>--scene</c>), else <see cref="Settings"/>' <c>scene</c>, else the first enabled Build Settings scene.
    /// </summary>
    /// <exception cref="InvalidOperationException">No scene is named anywhere, or the named scene file does not exist.</exception>
    public (string Scene, string Source) ResolveServerScene(string? explicitScene)
    {
        (string? scene, string source) = !string.IsNullOrWhiteSpace(explicitScene) ? (explicitScene, "--scene")
            : Settings.TryGetValue("scene", out string? configured) && !string.IsNullOrWhiteSpace(configured) ? (configured, SettingsFileName)
            : (BuildScenes.FirstOrDefault(), "the first enabled scene in Build Settings");
        if (scene is null)
        {
            throw new InvalidOperationException(
                "No server scene: add the scene with your NetBootstrap to File > Build Settings, put \"scene\" in mp-host.json, or pass --scene Assets/.../Main.unity.");
        }

        scene = scene.Replace('\\', '/');
        if (!File.Exists(Path.Combine(Directory, scene)))
        {
            throw new InvalidOperationException($"The server scene '{scene}' (from {source}) does not exist in {Directory}.");
        }

        return (scene, source);
    }

    /// <summary><paramref name="text"/> lower-cased to <c>[a-z0-9-]</c> with runs of anything else as one '-', or null when nothing is left.</summary>
    public static string? ToSlug(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var slug = new StringBuilder();
        foreach (char c in text.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        string result = slug.ToString().Trim('-');
        return result.Length == 0 ? null : result;
    }

    /// <summary><c>productName:</c> from a ProjectSettings.asset's text.</summary>
    public static string? ParseProductName(string? projectSettings)
    {
        if (projectSettings is null) return null;
        foreach (string raw in projectSettings.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("productName:", StringComparison.Ordinal))
            {
                string value = line["productName:".Length..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// The enabled scene paths of an EditorBuildSettings.asset's <c>m_Scenes</c> list, in order:
    /// <c>- enabled: 1</c> followed by <c>path: Assets/...unity</c>.
    /// </summary>
    public static IReadOnlyList<string> ParseBuildScenes(string? editorBuildSettings)
    {
        var scenes = new List<string>();
        if (editorBuildSettings is null) return scenes;
        bool? enabled = null;
        foreach (string raw in editorBuildSettings.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("- enabled:", StringComparison.Ordinal))
            {
                enabled = line["- enabled:".Length..].Trim() == "1";
            }
            else if (line.StartsWith("path:", StringComparison.Ordinal) && enabled is not null)
            {
                string path = line["path:".Length..].Trim();
                if (enabled == true && path.Length > 0) scenes.Add(path);
                enabled = null;
            }
        }

        return scenes;
    }

    /// <summary>An mp-host.json's text as option defaults; null text is none.</summary>
    /// <exception cref="InvalidOperationException">The JSON is invalid or not an object of scalar values.</exception>
    public static IReadOnlyDictionary<string, string> ParseSettings(string? json)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return settings;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{SettingsFileName} must be a JSON object of option defaults.");
            }

            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                string key = property.Name.TrimStart('-');
                settings[key] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString()!,
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => throw new InvalidOperationException($"{SettingsFileName}: '{property.Name}' must be a string, number or boolean."),
                };
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{SettingsFileName} is not valid JSON: {ex.Message}");
        }

        return settings;
    }

    /// <summary>
    /// The Unity.Multiplayer repository behind a <c>"com.mindattic.unity.multiplayer": "file:../x/unity/com.mindattic.unity.multiplayer"</c>
    /// manifest entry: the package folder's grandparent, when it holds <c>server/Rendezvous/Dockerfile</c>. Null for a
    /// git/registry install (then the published rendezvous image is used instead).
    /// </summary>
    public static string? FindMultiplayerRepository(string projectDirectory)
    {
        string packages = Path.Combine(projectDirectory, "Packages");
        string? manifest = ReadOrNull(Path.Combine(packages, "manifest.json"));
        if (manifest is null) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(manifest);
            if (!doc.RootElement.TryGetProperty("dependencies", out JsonElement deps)
                || !deps.TryGetProperty(PackageId, out JsonElement reference)
                || reference.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string value = reference.GetString()!;
            if (!value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
            string package = Path.GetFullPath(Path.Combine(packages, value["file:".Length..]));
            string repository = Path.GetFullPath(Path.Combine(package, "..", ".."));
            return File.Exists(Path.Combine(repository, "server", "Rendezvous", "Dockerfile")) ? repository : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
