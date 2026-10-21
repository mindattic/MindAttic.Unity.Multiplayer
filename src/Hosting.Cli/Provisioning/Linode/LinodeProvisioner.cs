using Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;
using Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Linode;

/// <summary>
/// <see cref="IProvisioner"/> for Linode/Akamai Cloud Computing (UM-T26): <see cref="CloudVpsProvisioner"/>
/// wired to <see cref="HttpLinodeApiClient"/> and Linode's own region/size/image defaults. See
/// <c>docs/articles/hosting/linode.md</c> for sizing and setup.
/// </summary>
public sealed class LinodeProvisioner : CloudVpsProvisioner
{
    /// <summary>The <c>server-package.json</c> profile id this provisioner serves.</summary>
    public const string Id = "Linode";

    /// <summary>Newark, NJ (us-east); g6-nanode-1 is Linode's cheapest Shared CPU plan suitable for a small game server.</summary>
    public static readonly CloudVpsProviderSpec Spec = new(Id, "Linode (Akamai Cloud Computing)", DefaultRegion: "us-east", DefaultSize: "g6-nanode-1", ImageSlug: "linode/ubuntu24.04");

    public LinodeProvisioner(ICloudVpsApiClient api, ISshCommandRunner ssh) : base(api, ssh, Spec)
    {
    }
}
