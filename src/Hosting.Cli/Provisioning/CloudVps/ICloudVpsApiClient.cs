namespace Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

/// <summary>
/// The slice of a plain-VPS cloud provider's REST API <see cref="CloudVpsProvisioner"/> needs:
/// create a server from an Ubuntu image plus cloud-init user-data, read it back, open firewall
/// rules, and destroy it. One implementation per provider (Hetzner, Vultr, DigitalOcean, Linode,
/// OVHcloud -- UM-T26); the shared provisioner, SSH bootstrap and cloud-init rendering never change
/// per provider, only this seam does. NUnit mocks this interface directly instead of faking HTTP
/// responses, the same shape as <c>IRunPodApiClient</c> (UM-T25).
/// </summary>
public interface ICloudVpsApiClient
{
    /// <summary>
    /// Registers <paramref name="publicKey"/> with the provider under <paramref name="name"/> (or
    /// finds an already-registered one with that name) and returns whatever opaque id
    /// <see cref="CloudVpsCreateRequest.SshKeyId"/> expects to attach it to a new server. Some
    /// providers (Linode) take the key's own text inline instead of a separate resource id -- their
    /// client returns the key text itself here, since this id travels unopened through
    /// <see cref="CloudVpsProvisioner"/>.
    /// </summary>
    Task<string> EnsureSshKeyAsync(string name, string publicKey, CancellationToken ct);

    /// <summary>Creates a server and waits for the provider to accept the request (not for it to finish booting).</summary>
    Task<CloudVpsServer> CreateServerAsync(CloudVpsCreateRequest request, CancellationToken ct);

    /// <summary>The server's current status and public IP (null until the provider has assigned one).</summary>
    Task<CloudVpsServer> GetServerAsync(string serverId, CancellationToken ct);

    /// <summary>Opens every rule in <paramref name="rules"/> inbound from anywhere, replacing any mp-host-managed rule set already there.</summary>
    Task EnsureFirewallAsync(string serverId, IReadOnlyList<CloudVpsFirewallRule> rules, CancellationToken ct);

    /// <summary>Permanently destroys the server.</summary>
    Task DestroyServerAsync(string serverId, CancellationToken ct);
}
