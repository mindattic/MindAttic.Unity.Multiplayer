namespace Unity.Multiplayer.Hosting.Cli.Provisioning;

/// <summary>
/// The provisioners a running mp-host knows about. UM-T25 registers <c>GenericVps</c> and
/// <c>RunPod</c> (the two profiles UM-T23 ships); UM-T26-UM-T31 add their own with
/// <see cref="Register"/> without touching this type or the pipeline.
/// </summary>
public sealed class ProvisionerRegistry
{
    private readonly Dictionary<string, IProvisioner> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds (or replaces) a provisioner under its own <see cref="IProvisioner.ProviderId"/>.</summary>
    public ProvisionerRegistry Register(IProvisioner provisioner)
    {
        ArgumentNullException.ThrowIfNull(provisioner);
        _byId[provisioner.ProviderId] = provisioner;
        return this;
    }

    /// <summary>The provisioner for <paramref name="providerId"/> (the hosting profile id), or null.</summary>
    public IProvisioner? Find(string providerId) => _byId.GetValueOrDefault(providerId);

    /// <summary>The provisioner for <paramref name="providerId"/>.</summary>
    /// <exception cref="ArgumentException">No provisioner is registered for it.</exception>
    public IProvisioner Require(string providerId) =>
        Find(providerId) ?? throw new ArgumentException(
            $"No provisioner for profile '{providerId}'. Known: {string.Join(", ", All.Select(p => p.ProviderId))}.");

    /// <summary>Every registered provisioner.</summary>
    public IReadOnlyCollection<IProvisioner> All => _byId.Values;
}
