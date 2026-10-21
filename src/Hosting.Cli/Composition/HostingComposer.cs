using System.Net.Http.Headers;
using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Docker;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.DigitalOcean;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Hetzner;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Linode;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Ovh;
using Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Vultr;

namespace Unity.Multiplayer.Hosting.Cli.Composition;

/// <summary>
/// Wires real (or dry-run) implementations together from <see cref="HostingCredentialStore"/>,
/// the one place in mp-host that touches MindAttic.Vault. Every command handler goes through this
/// instead of constructing provisioners itself, so the dry-run switch is in exactly one place.
/// </summary>
public sealed class HostingComposer
{
    private readonly HostingCredentialStore _credentials;
    private readonly bool _dryRun;

    public HostingComposer(HostingCredentialStore credentials, bool dryRun)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _dryRun = dryRun;
    }

    /// <summary>GHCR registry host; overridable for a self-hosted or mirrored registry.</summary>
    public string GhcrRegistry { get; init; } = "ghcr.io";

    /// <summary>The server-package builder: reads an already-built package, or drives Unity in batch mode.</summary>
    public IServerPackageBuilder BuildPackageBuilder() => new ServerPackageBuilder(new ProcessRunner());

    /// <summary>The image tool: real docker CLI, or a dry-run echo.</summary>
    public IContainerImageTool BuildImageTool(IDeployLog log) => _dryRun ? new DryRunImageTool() : new DockerCliImageTool(new ProcessRunner());

    /// <summary>GHCR credentials from the vault. Required for every deploy/update (even dry-run, so the printed login line is accurate) except when explicitly running with placeholder creds in tests.</summary>
    /// <exception cref="InvalidOperationException">No GHCR credential is stored.</exception>
    public HostingCredentialStore.GhcrCreds RequireGhcrCreds() =>
        _credentials.GetGhcrCreds() ?? throw new InvalidOperationException(
            "No GHCR credential in the Hosting vault. Store one first (HostingCredentialStore.SetGhcrCreds), " +
            "or set MINDATTIC_HOSTING_CREDENTIALS to a directory that has one.");

    /// <summary>Every hosting profile id <see cref="BuildProvisioner"/> and <see cref="BuildConnection"/> know about, for error messages.</summary>
    private static readonly string[] KnownProfiles =
    {
        GenericVpsProvisioner.Id, RunPodProvisioner.Id,
        HetznerProvisioner.Id, VultrProvisioner.Id, DigitalOceanProvisioner.Id, LinodeProvisioner.Id, OvhProvisioner.Id,
    };

    /// <summary>The provisioner for <paramref name="profileId"/>, wired to the vault credential (or dry-run) for it.</summary>
    /// <exception cref="ArgumentException"><paramref name="profileId"/> is not a known hosting profile.</exception>
    public IProvisioner BuildProvisioner(string profileId, IDeployLog log)
    {
        if (string.Equals(profileId, GenericVpsProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            return new GenericVpsProvisioner(BuildSsh(log));
        }

        if (string.Equals(profileId, RunPodProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            IRunPodApiClient api = _dryRun ? new DryRunRunPodApiClient(log) : BuildRealRunPodClient();
            return new RunPodProvisioner(api);
        }

        if (string.Equals(profileId, HetznerProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            ICloudVpsApiClient api = _dryRun ? DryRunCloudVps(HetznerProvisioner.Id, "https://api.hetzner.cloud/v1", log) : BuildHetznerClient();
            return new HetznerProvisioner(api, BuildSsh(log));
        }

        if (string.Equals(profileId, VultrProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            ICloudVpsApiClient api = _dryRun ? DryRunCloudVps(VultrProvisioner.Id, "https://api.vultr.com/v2", log) : BuildVultrClient();
            return new VultrProvisioner(api, BuildSsh(log));
        }

        if (string.Equals(profileId, DigitalOceanProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            ICloudVpsApiClient api = _dryRun ? DryRunCloudVps(DigitalOceanProvisioner.Id, "https://api.digitalocean.com/v2", log) : BuildDigitalOceanClient();
            return new DigitalOceanProvisioner(api, BuildSsh(log));
        }

        if (string.Equals(profileId, LinodeProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            ICloudVpsApiClient api = _dryRun ? DryRunCloudVps(LinodeProvisioner.Id, "https://api.linode.com/v4", log) : BuildLinodeClient();
            return new LinodeProvisioner(api, BuildSsh(log));
        }

        if (string.Equals(profileId, OvhProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            ICloudVpsApiClient api = _dryRun ? DryRunCloudVps(OvhProvisioner.Id, "https://<endpoint>.api.ovh.com/1.0", log) : BuildOvhClient();
            return new OvhProvisioner(api, BuildSsh(log));
        }

        throw new ArgumentException($"Unknown hosting profile '{profileId}'. Known: {string.Join(", ", KnownProfiles)}.");
    }

    private ISshCommandRunner BuildSsh(IDeployLog log) => _dryRun ? new DryRunSshCommandRunner(log) : new ProcessSshCommandRunner(new ProcessRunner());

    private static DryRunCloudVpsApiClient DryRunCloudVps(string providerId, string apiBaseUrl, IDeployLog log) => new(log, providerId, apiBaseUrl);

    /// <summary>The connection info a provisioner needs for <paramref name="serverName"/>, resolved from the vault.</summary>
    /// <exception cref="InvalidOperationException">GenericVps was asked for but has no stored SSH target.</exception>
    public ProviderConnection BuildConnection(string profileId, string serverName)
    {
        if (string.Equals(profileId, GenericVpsProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            var ssh = _credentials.GetSshCreds(serverName) ?? throw new InvalidOperationException(
                $"No SSH target stored for '{serverName}'. Store one first (HostingCredentialStore.SetSshCreds).");
            var options = new Dictionary<string, string>
            {
                ["sshHost"] = ssh.Host,
                ["sshPort"] = ssh.Port.ToString(),
                ["sshUser"] = ssh.User,
                ["sshKeyPath"] = ssh.PrivateKeyPath,
            };
            var secrets = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ssh.Passphrase))
            {
                secrets["sshPassphrase"] = ssh.Passphrase;
            }

            return new ProviderConnection(serverName, options, secrets);
        }

        if (string.Equals(profileId, HetznerProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            var creds = _credentials.GetHetznerCreds() ?? throw new InvalidOperationException(
                "No Hetzner credential in the Hosting vault. Store one first (HostingCredentialStore.SetHetznerCreds).");
            return CloudVpsConnection(serverName, creds);
        }

        if (string.Equals(profileId, VultrProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            var creds = _credentials.GetVultrCreds() ?? throw new InvalidOperationException(
                "No Vultr credential in the Hosting vault. Store one first (HostingCredentialStore.SetVultrCreds).");
            return CloudVpsConnection(serverName, creds);
        }

        if (string.Equals(profileId, DigitalOceanProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            var creds = _credentials.GetDigitalOceanCreds() ?? throw new InvalidOperationException(
                "No DigitalOcean credential in the Hosting vault. Store one first (HostingCredentialStore.SetDigitalOceanCreds).");
            return CloudVpsConnection(serverName, creds);
        }

        if (string.Equals(profileId, LinodeProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            var creds = _credentials.GetLinodeCreds() ?? throw new InvalidOperationException(
                "No Linode credential in the Hosting vault. Store one first (HostingCredentialStore.SetLinodeCreds).");
            return CloudVpsConnection(serverName, creds);
        }

        if (string.Equals(profileId, OvhProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            var creds = _credentials.GetOvhCreds() ?? throw new InvalidOperationException(
                "No OVHcloud credential in the Hosting vault. Store one first (HostingCredentialStore.SetOvhCreds).");
            return CloudVpsConnection(serverName, creds.SshUser, creds.SshPrivateKeyPath, creds.SshPublicKey);
        }

        return ProviderConnection.Named(serverName);
    }

    /// <summary>
    /// The SSH-fallback half of a UM-T26 cloud provider's <see cref="ProviderConnection"/>: the
    /// shared SSH keypair mp-host registers with the provider and uses for status/logs/update once a
    /// server has a public IP. The host itself is never in here -- it is only known after
    /// <see cref="IProvisioner.ProvisionAsync"/> creates the server, so every later call resolves it
    /// fresh from the provider's API.
    /// </summary>
    private static ProviderConnection CloudVpsConnection(string serverName, HostingCredentialStore.CloudVpsCreds creds) =>
        CloudVpsConnection(serverName, creds.SshUser, creds.SshPrivateKeyPath, creds.SshPublicKey);

    private static ProviderConnection CloudVpsConnection(string serverName, string sshUser, string sshKeyPath, string sshPublicKey)
    {
        var options = new Dictionary<string, string>
        {
            ["sshUser"] = sshUser,
            ["sshKeyPath"] = sshKeyPath,
            ["sshPublicKey"] = sshPublicKey,
        };
        return new ProviderConnection(serverName, options, new Dictionary<string, string>());
    }

    private HttpRunPodApiClient BuildRealRunPodClient()
    {
        var creds = _credentials.GetRunPodCreds() ?? throw new InvalidOperationException(
            "No RunPod API key in the Hosting vault. Store one first (HostingCredentialStore.SetRunPodCreds).");
        var http = new HttpClient { BaseAddress = new Uri("https://api.runpod.io/graphql") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);
        return new HttpRunPodApiClient(http);
    }

    private HttpHetznerApiClient BuildHetznerClient()
    {
        var creds = _credentials.GetHetznerCreds() ?? throw new InvalidOperationException(
            "No Hetzner credential in the Hosting vault. Store one first (HostingCredentialStore.SetHetznerCreds).");
        var http = new HttpClient { BaseAddress = new Uri("https://api.hetzner.cloud/v1/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);
        return new HttpHetznerApiClient(http);
    }

    private HttpVultrApiClient BuildVultrClient()
    {
        var creds = _credentials.GetVultrCreds() ?? throw new InvalidOperationException(
            "No Vultr credential in the Hosting vault. Store one first (HostingCredentialStore.SetVultrCreds).");
        var http = new HttpClient { BaseAddress = new Uri("https://api.vultr.com/v2/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);
        return new HttpVultrApiClient(http);
    }

    private HttpDigitalOceanApiClient BuildDigitalOceanClient()
    {
        var creds = _credentials.GetDigitalOceanCreds() ?? throw new InvalidOperationException(
            "No DigitalOcean credential in the Hosting vault. Store one first (HostingCredentialStore.SetDigitalOceanCreds).");
        var http = new HttpClient { BaseAddress = new Uri("https://api.digitalocean.com/v2/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);
        return new HttpDigitalOceanApiClient(http);
    }

    private HttpLinodeApiClient BuildLinodeClient()
    {
        var creds = _credentials.GetLinodeCreds() ?? throw new InvalidOperationException(
            "No Linode credential in the Hosting vault. Store one first (HostingCredentialStore.SetLinodeCreds).");
        var http = new HttpClient { BaseAddress = new Uri("https://api.linode.com/v4/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);
        return new HttpLinodeApiClient(http);
    }

    private HttpOvhApiClient BuildOvhClient()
    {
        var creds = _credentials.GetOvhCreds() ?? throw new InvalidOperationException(
            "No OVHcloud credential in the Hosting vault. Store one first (HostingCredentialStore.SetOvhCreds).");
        var http = new HttpClient { BaseAddress = new Uri($"https://{creds.Endpoint}.api.ovh.com/1.0/") };
        return new HttpOvhApiClient(http, creds.ApplicationKey, creds.ApplicationSecret, creds.ConsumerKey, creds.ServiceName);
    }
}
