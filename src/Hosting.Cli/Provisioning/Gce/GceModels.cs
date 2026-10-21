namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Gce;

/// <summary>A GCE instance, as reported by <c>instances.get</c>.</summary>
public sealed record GceInstance(string Name, string Status, string? PublicIp);

/// <summary>What to ask <c>instances.insert</c> to create.</summary>
public sealed record GceInsertInstanceRequest(
    string Name,
    string Zone,
    string MachineType,
    string SourceImage,
    string Network,
    string NetworkTag,
    string SshUser,
    string SshPublicKey,
    string StartupScript);

/// <summary>One <c>allowed</c> entry of a firewall rule: a protocol and the ports it covers.</summary>
public sealed record GceFirewallAllow(string IpProtocol, IReadOnlyList<string> Ports);
