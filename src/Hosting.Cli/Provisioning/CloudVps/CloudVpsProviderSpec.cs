namespace Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

/// <summary>
/// The per-provider constants <see cref="CloudVpsProvisioner"/> needs beyond its
/// <see cref="ICloudVpsApiClient"/>: the profile id and name, a cheapest-suitable default
/// region/size (an operator's <c>--region</c>/<c>--size</c> always wins), the Ubuntu image slug to
/// boot, and the default SSH user that image logs in as.
/// </summary>
public sealed record CloudVpsProviderSpec(
    string ProviderId,
    string DisplayName,
    string DefaultRegion,
    string DefaultSize,
    string ImageSlug,
    string DefaultSshUser = "root");
