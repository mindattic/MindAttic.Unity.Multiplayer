using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Game;

/// <summary>The profile a project last deployed with (UM-T61): what a bare <c>mp-host game status|logs|update|destroy</c> defaults to.</summary>
public sealed record GameProjectProfile(string Project, string Profile, DateTimeOffset DeployedAt);

/// <summary>A game's last hosted deploy: what <c>mp-host game status|logs|update|destroy</c> needs so the operator never copies a server id around.</summary>
public sealed record GameDeployment(string Profile, string Name, string ServerId, string? PublicAddress, string Project, DateTimeOffset DeployedAt);

/// <summary>
/// Remembers <see cref="GameDeployment"/>s per (profile, server name) as small JSON files (UM-T58). Holds ids and
/// addresses only, never a credential, so it lives in the user's local app data rather than the vault:
/// <c>%LOCALAPPDATA%/MindAttic/mp-host/deployments</c> (or <see cref="DirectoryEnvVar"/>).
/// </summary>
public sealed class GameDeploymentStore
{
    /// <summary>Overrides the store's folder (tests, CI).</summary>
    public const string DirectoryEnvVar = "MP_HOST_STATE";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public GameDeploymentStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    /// <summary>The folder the records live in.</summary>
    public string Directory { get; }

    /// <summary>The store at <see cref="DirectoryEnvVar"/>, else the user's local app data.</summary>
    public static GameDeploymentStore Default()
    {
        string? overridden = Environment.GetEnvironmentVariable(DirectoryEnvVar);
        return new GameDeploymentStore(!string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "mp-host", "deployments"));
    }

    /// <summary>The record for <paramref name="profile"/>/<paramref name="name"/>, or null.</summary>
    public GameDeployment? Get(string profile, string name)
    {
        string path = PathFor(profile, name);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<GameDeployment>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Saves (replaces) the record.</summary>
    public void Set(GameDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathFor(deployment.Profile, deployment.Name), JsonSerializer.Serialize(deployment, Json));
    }

    /// <summary>Forgets the record (after a destroy).</summary>
    public void Remove(string profile, string name)
    {
        string path = PathFor(profile, name);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// The profile <paramref name="project"/> last deployed with (<c>game deploy</c>, <c>game local</c> or
    /// <c>game update</c>), or null when it never has from this machine.
    /// </summary>
    public string? LastProfile(string project)
    {
        string path = ProjectPathFor(project);
        if (!File.Exists(path)) return null;
        try
        {
            GameProjectProfile? record = JsonSerializer.Deserialize<GameProjectProfile>(File.ReadAllText(path), Json);
            return string.IsNullOrWhiteSpace(record?.Profile) ? null : record.Profile;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Remembers <paramref name="profile"/> as the one <paramref name="project"/> last deployed with.</summary>
    public void SetLastProfile(string project, string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        string path = ProjectPathFor(project);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new GameProjectProfile(Path.GetFullPath(project), profile, DateTimeOffset.UtcNow), Json));
    }

    /// <summary>
    /// One file per project folder beside the deployment records: the folder's name for reading, plus a hash of its
    /// full path (case-folded, as Windows paths are) so two checkouts of the same game keep their own.
    /// </summary>
    private string ProjectPathFor(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project)).Replace('\\', '/').ToLowerInvariant();
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full));
        string slug = UnityProject.ToSlug(Path.GetFileName(full)) ?? "project";
        return Path.Combine(Directory, "projects", $"{slug}--{Convert.ToHexString(hash, 0, 6).ToLowerInvariant()}.json");
    }

    private string PathFor(string profile, string name) =>
        Path.Combine(Directory, $"{UnityProject.ToSlug(profile) ?? "profile"}--{UnityProject.ToSlug(name) ?? "server"}.json");
}
