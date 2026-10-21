using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Docker;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Provisioning;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.RunPod;

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

    /// <summary>The provisioner for <paramref name="profileId"/>, wired to the vault credential (or dry-run) for it.</summary>
    /// <exception cref="ArgumentException"><paramref name="profileId"/> is not GenericVps or RunPod.</exception>
    public IProvisioner BuildProvisioner(string profileId, IDeployLog log)
    {
        if (string.Equals(profileId, GenericVpsProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            ISshCommandRunner ssh = _dryRun ? new DryRunSshCommandRunner(log) : new ProcessSshCommandRunner(new ProcessRunner());
            return new GenericVpsProvisioner(ssh);
        }

        if (string.Equals(profileId, RunPodProvisioner.Id, StringComparison.OrdinalIgnoreCase))
        {
            IRunPodApiClient api = _dryRun ? new DryRunRunPodApiClient(log) : BuildRealRunPodClient();
            return new RunPodProvisioner(api);
        }

        throw new ArgumentException($"Unknown hosting profile '{profileId}'. Known: {GenericVpsProvisioner.Id}, {RunPodProvisioner.Id}.");
    }

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

        return ProviderConnection.Named(serverName);
    }

    private HttpRunPodApiClient BuildRealRunPodClient()
    {
        var creds = _credentials.GetRunPodCreds() ?? throw new InvalidOperationException(
            "No RunPod API key in the Hosting vault. Store one first (HostingCredentialStore.SetRunPodCreds).");
        var http = new HttpClient { BaseAddress = new Uri("https://api.runpod.io/graphql") };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", creds.ApiKey);
        return new HttpRunPodApiClient(http);
    }
}
