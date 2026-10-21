using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Ovh;

/// <summary>
/// <see cref="IProvisioner"/> for OVHcloud Public Cloud instances (UM-T26): <see cref="CloudVpsProvisioner"/>
/// wired to <see cref="HttpOvhApiClient"/> and OVH's own region/size/image defaults. OVH includes
/// free, always-on anti-DDoS protection on every Public Cloud instance -- see
/// <c>docs/articles/hosting/ovhcloud.md</c> for that and sizing/setup.
/// </summary>
public sealed class OvhProvisioner : CloudVpsProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "OvhCloud";

    /// <summary>Gravelines, France (GRA); d2-2 is OVH's cheapest Public Cloud "Discovery" instance suitable for a small game server.</summary>
    public static readonly CloudVpsProviderSpec Spec = new(Id, "OVHcloud", DefaultRegion: "GRA", DefaultSize: "d2-2", ImageSlug: "Ubuntu 24.04");

    public OvhProvisioner(ICloudVpsApiClient api, ISshCommandRunner ssh) : base(api, ssh, Spec)
    {
    }
}
