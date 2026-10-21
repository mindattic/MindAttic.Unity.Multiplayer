using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Processes;

namespace Unity.Multiplayer.Hosting.Cli.Game;

/// <summary>What <c>mp-host game</c> touches outside its arguments, injectable so a whole run is testable with fakes.</summary>
public sealed class GameServices
{
    public GameServices(HostingCredentialStore credentials)
    {
        Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    /// <summary>The vault (GHCR, SSH targets, rendezvous secret, local state).</summary>
    public HostingCredentialStore Credentials { get; }

    /// <summary>Runs docker for the pre-steps, local status/logs and the Linux-engine probe.</summary>
    public IProcessRunner Processes { get; init; } = new ProcessRunner();

    /// <summary>Where the Unity editor is looked for.</summary>
    public EditorSearchEnvironment Editors { get; init; } = new();

    /// <summary>Remembered hosted deployments (server ids).</summary>
    public GameDeploymentStore Deployments { get; init; } = GameDeploymentStore.Default();

    /// <summary>The default <c>--project</c>.</summary>
    public string CurrentDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>Point docker at Docker Desktop's Linux engine when it is in Windows-containers mode (real runs only).</summary>
    public bool PreferLinuxDocker { get; init; } = true;
}
