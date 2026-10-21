using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.DigitalOcean;

/// <summary>
/// <see cref="IProvisioner"/> for DigitalOcean (UM-T26): <see cref="CloudVpsProvisioner"/> wired to
/// <see cref="HttpDigitalOceanApiClient"/> and DigitalOcean's own region/size/image defaults. See
/// <c>docs/articles/hosting/digitalocean.md</c> for sizing and setup.
/// </summary>
public sealed class DigitalOceanProvisioner : CloudVpsProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "DigitalOcean";

    /// <summary>New York 1; s-1vcpu-1gb is DigitalOcean's cheapest Basic droplet suitable for a small game server.</summary>
    public static readonly CloudVpsProviderSpec Spec = new(Id, "DigitalOcean", DefaultRegion: "nyc1", DefaultSize: "s-1vcpu-1gb", ImageSlug: "ubuntu-24-04-x64");

    public DigitalOceanProvisioner(ICloudVpsApiClient api, ISshCommandRunner ssh) : base(api, ssh, Spec)
    {
    }
}
