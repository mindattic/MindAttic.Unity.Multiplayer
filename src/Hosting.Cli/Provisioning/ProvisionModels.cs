using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning;

/// <summary>
/// How to reach an already-known (or about-to-exist) server: its logical name (the
/// <see cref="Credentials.HostingCredentialStore"/> key) plus provider-specific connection details.
///
/// <para>Carried by every <see cref="IProvisioner"/> call, including status/logs/destroy, which
/// need no image or manifest but still need to know which box/pod to reach and how.
/// <see cref="ProviderOptions"/> holds non-secret knobs by well-known key, documented on each
/// provisioner (<c>GenericVpsProvisioner</c>: <c>sshHost</c>/<c>sshPort</c>/<c>sshUser</c>/
/// <c>sshKeyPath</c>; <c>RunPodProvisioner</c>: none required beyond the API key).
/// <see cref="ProviderSecrets"/> holds the same idea for values a provisioner must never echo in a
/// dry-run log line (an SSH key passphrase, a provider API key) -- logging one is a bug.</para>
/// </summary>
public sealed record ProviderConnection(
    string ServerName,
    IReadOnlyDictionary<string, string> ProviderOptions,
    IReadOnlyDictionary<string, string> ProviderSecrets)
{
    /// <summary>A connection with no provider-specific details, for providers that need none beyond a secret resolved elsewhere.</summary>
    public static ProviderConnection Named(string serverName) =>
        new(serverName, new Dictionary<string, string>(), new Dictionary<string, string>());
}

/// <summary>What to provision: the pushed image, its manifest, and the connection to reach the host.</summary>
public sealed record ProvisionRequest(
    ProviderConnection Connection,
    string ImageReference,
    string GhcrRegistry,
    string GhcrUsername,
    string GhcrToken,
    ServerPackageManifest Manifest,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string? Region,
    string? Size,
    string? PackageDirectory = null)
{
    /// <summary>Convenience passthrough.</summary>
    public string ServerName => Connection.ServerName;
}

/// <summary>Where a server is in its deploy lifecycle, independent of provider vocabulary.</summary>
public enum ServerState
{
    /// <summary>Still coming up (creating the VM/pod, or the container hasn't answered health yet).</summary>
    Provisioning,

    /// <summary>Healthy and accepting players.</summary>
    Running,

    /// <summary>Up, but failing its health check.</summary>
    Unhealthy,

    /// <summary>Stopped (not destroyed).</summary>
    Stopped,

    /// <summary>Destroyed, or never existed.</summary>
    Gone,

    /// <summary>The provider could not say.</summary>
    Unknown,
}

/// <summary>What provisioning (or a later status check) found.</summary>
public sealed record ProvisionResult(string ServerId, string PublicAddress, IReadOnlyDictionary<int, int> PortMappings);

/// <summary>A point-in-time status report.</summary>
public sealed record ServerStatus(string ServerId, ServerState State, string? PublicAddress, string Detail);
