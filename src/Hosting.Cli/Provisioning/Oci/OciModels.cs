namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

/// <summary>An OCI Compute instance, as reported by <c>GET /instances/{id}</c> (plus the resolved primary VNIC public IP).</summary>
public sealed record OciInstance(string Id, string LifecycleState, string? PublicIp);

/// <summary>What to ask <c>POST /instances</c> to launch.</summary>
public sealed record OciLaunchInstanceRequest(
    string CompartmentId,
    string AvailabilityDomain,
    string SubnetId,
    string Shape,
    int Ocpus,
    int MemoryInGBs,
    string ImageId,
    string DisplayName,
    string SshPublicKey,
    string UserDataBase64);

/// <summary>
/// One ingress rule of a VCN security list, OCI's subnet-wide equivalent of a security group/NSG. A
/// plain string protocol (<c>tcp</c>/<c>udp</c>) rather than OCI's own IANA protocol numbers ("6"/"17")
/// -- <see cref="HttpOciApiClient"/> is the only place that translates between the two.
/// </summary>
public sealed record OciSecurityRule(string Protocol, int Port, string Description);
