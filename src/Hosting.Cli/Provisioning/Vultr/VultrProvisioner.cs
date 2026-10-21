using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Vultr;

/// <summary>
/// <see cref="IProvisioner"/> for Vultr (UM-T26): <see cref="CloudVpsProvisioner"/> wired to
/// <see cref="HttpVultrApiClient"/> and Vultr's own region/size/image defaults. See
/// <c>docs/articles/hosting/vultr.md</c> for sizing and setup.
/// </summary>
public sealed class VultrProvisioner : CloudVpsProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "Vultr";

    /// <summary>New Jersey (ewr); vc2-1c-1gb is Vultr's cheapest Cloud Compute plan suitable for a small game server.</summary>
    public static readonly CloudVpsProviderSpec Spec = new(Id, "Vultr", DefaultRegion: "ewr", DefaultSize: "vc2-1c-1gb", ImageSlug: "1743"); // Ubuntu 24.04 LTS os_id, verify against GET /os before relying on it.

    public VultrProvisioner(ICloudVpsApiClient api, ISshCommandRunner ssh) : base(api, ssh, Spec)
    {
    }
}
