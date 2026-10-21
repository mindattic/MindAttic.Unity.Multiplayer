using System.Text.Json;

namespace Unity.Multiplayer.Hosting.Cli.Game;

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

    private string PathFor(string profile, string name) =>
        Path.Combine(Directory, $"{UnityProject.ToSlug(profile) ?? "profile"}--{UnityProject.ToSlug(name) ?? "server"}.json");
}
