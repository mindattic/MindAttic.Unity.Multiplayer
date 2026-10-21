using System.Text.Json;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;

namespace Unity.Multiplayer.Hosting.Cli.Credentials;

/// <summary>
/// Hosting keyring at <c>%APPDATA%\MindAttic\Hosting\</c> (MindAttic.Vault, never the repo). One
/// entry per provider id mp-host knows about (<c>ghcr</c>, <c>genericvps:&lt;server-name&gt;</c>,
/// <c>runpod</c>, ...). Shape mirrors <c>MindAttic.Vault.Credentials.BrokerCredentialStore</c>:
/// <code>
/// {
///   "ghcr":      { "type": "ghcr",      "apiKey": "&lt;PAT with write:packages&gt;", "secret": "&lt;github username&gt;" },
///   "runpod":    { "type": "runpod",    "apiKey": "&lt;RunPod API key&gt;" },
///   "genericvps:prod-box": { "type": "ssh", "apiKey": "&lt;passphrase or empty&gt;", "secret": "root@203.0.113.10:22",
///                             "sshKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519" }
/// }
/// </code>
/// Every real provisioner call reads from here; nothing it needs is ever passed on the command
/// line or committed to a project file. Tests construct their own instance over a temp directory
/// instead of touching the real keyring.
/// </summary>
public sealed class HostingCredentialStore : CredentialStore
{
    /// <summary>Bucket folder name under <c>%APPDATA%\MindAttic\</c>.</summary>
    public const string Bucket = "Hosting";

    /// <summary>Environment variable that overrides the resolved bucket directory.</summary>
    public const string DirectoryEnvVar = "MINDATTIC_HOSTING_CREDENTIALS";

    /// <summary>Default instance rooted at <c>%APPDATA%\MindAttic\Hosting\</c> (or <see cref="DirectoryEnvVar"/>).</summary>
    public static HostingCredentialStore Default { get; } = new(ResolveDefaultDirectory());

    /// <inheritdoc />
    public HostingCredentialStore(string directory) : base(directory) { }

    private static string ResolveDefaultDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable(DirectoryEnvVar);
        return string.IsNullOrWhiteSpace(overrideDir) ? VaultPaths.RoamingBucket(Bucket) : overrideDir;
    }

    /// <summary>A GitHub Container Registry token: <paramref name="Username"/> + a PAT with <c>write:packages</c>.</summary>
    public sealed record GhcrCreds(string Username, string Token);

    /// <summary>An SSH target for a bring-your-own (generic VPS) host.</summary>
    public sealed record SshCreds(string Host, int Port, string User, string PrivateKeyPath, string? Passphrase);

    /// <summary>A bare provider API key (RunPod and friends).</summary>
    public sealed record ApiKeyCreds(string ApiKey);

    /// <summary>GHCR provider id in this store.</summary>
    public const string GhcrProviderId = "ghcr";

    /// <summary>RunPod provider id in this store.</summary>
    public const string RunPodProviderId = "runpod";

    /// <summary>The generic-VPS provider id for a given server name (one SSH target per server).</summary>
    public static string GenericVpsProviderId(string serverName) => "genericvps:" + serverName;

    /// <summary>Reads the GHCR credential, or null when missing or incomplete.</summary>
    public GhcrCreds? GetGhcrCreds()
    {
        var raw = LoadAllRaw();
        if (!raw.TryGetValue(GhcrProviderId, out var json) || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            string token = StringProp(doc, "apiKey");
            string user = StringProp(doc, "secret");
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(user)) return null;
            return new GhcrCreds(user.Trim(), token.Trim());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Upserts the GHCR credential.</summary>
    public void SetGhcrCreds(GhcrCreds creds)
    {
        ArgumentNullException.ThrowIfNull(creds);
        SaveRaw(GhcrProviderId, Write(w =>
        {
            w.WriteString("type", "ghcr");
            w.WriteString("apiKey", creds.Token);
            w.WriteString("secret", creds.Username);
        }));
    }

    /// <summary>Reads the RunPod API key, or null when missing.</summary>
    public ApiKeyCreds? GetRunPodCreds()
    {
        string? key = GetKey(RunPodProviderId);
        return string.IsNullOrWhiteSpace(key) ? null : new ApiKeyCreds(key);
    }

    /// <summary>Upserts the RunPod API key.</summary>
    public void SetRunPodCreds(string apiKey) => SetKey(RunPodProviderId, apiKey);

    /// <summary>Reads the SSH target for <paramref name="serverName"/>'s generic-VPS entry, or null when missing or incomplete.</summary>
    public SshCreds? GetSshCreds(string serverName)
    {
        var raw = LoadAllRaw();
        if (!raw.TryGetValue(GenericVpsProviderId(serverName), out var json) || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            string hostPort = StringProp(doc, "secret");
            string user = "root";
            string host = hostPort;
            int port = 22;
            int at = hostPort.IndexOf('@');
            if (at >= 0)
            {
                user = hostPort[..at];
                hostPort = hostPort[(at + 1)..];
            }

            int colon = hostPort.LastIndexOf(':');
            if (colon >= 0 && int.TryParse(hostPort[(colon + 1)..], out int parsedPort))
            {
                host = hostPort[..colon];
                port = parsedPort;
            }
            else
            {
                host = hostPort;
            }

            string keyPath = StringProp(doc, "sshKeyPath");
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(keyPath)) return null;
            string? passphrase = StringPropOrNull(doc, "apiKey");
            return new SshCreds(host, port, user, keyPath, string.IsNullOrWhiteSpace(passphrase) ? null : passphrase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Upserts the SSH target for <paramref name="serverName"/>'s generic-VPS entry.</summary>
    public void SetSshCreds(string serverName, SshCreds creds)
    {
        ArgumentNullException.ThrowIfNull(creds);
        SaveRaw(GenericVpsProviderId(serverName), Write(w =>
        {
            w.WriteString("type", "ssh");
            w.WriteString("apiKey", creds.Passphrase ?? "");
            w.WriteString("secret", $"{creds.User}@{creds.Host}:{creds.Port}");
            w.WriteString("sshKeyPath", creds.PrivateKeyPath);
        }));
    }

    private static string StringProp(JsonDocument doc, string name) => StringPropOrNull(doc, name) ?? "";

    private static string? StringPropOrNull(JsonDocument doc, string name) =>
        doc.RootElement.ValueKind == JsonValueKind.Object
        && doc.RootElement.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
