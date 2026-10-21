using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Hetzner;

/// <summary>
/// <see cref="IProvisioner"/> for Hetzner Cloud (UM-T26): <see cref="CloudVpsProvisioner"/> wired to
/// <see cref="HttpHetznerApiClient"/> and Hetzner's own region/size/image defaults. See
/// <c>docs/articles/hosting/hetzner.md</c> for sizing and setup.
/// </summary>
public sealed class HetznerProvisioner : CloudVpsProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "Hetzner";

    /// <summary>Nuremberg (cheapest EU region at the time this shipped); CPX11 is Hetzner's cheapest shared-vCPU plan suitable for a small game server.</summary>
    public static readonly CloudVpsProviderSpec Spec = new(Id, "Hetzner Cloud", DefaultRegion: "nbg1", DefaultSize: "cpx11", ImageSlug: "ubuntu-24.04");

    public HetznerProvisioner(ICloudVpsApiClient api, ISshCommandRunner ssh) : base(api, ssh, Spec)
    {
    }
}
