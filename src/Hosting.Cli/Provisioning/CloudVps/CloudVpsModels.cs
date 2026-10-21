namespace Unity.Multiplayer.Hosting.Cli.Provisioning.CloudVps;

/// <summary>One inbound firewall rule a <see cref="ICloudVpsApiClient"/> must open: the game port (UDP or TCP) or the rendezvous port (TCP).</summary>
public sealed record CloudVpsFirewallRule(int Port, string Protocol);

/// <summary>What to ask a plain-VPS cloud provider to create.</summary>
public sealed record CloudVpsCreateRequest(
    string Name,
    string Region,
    string Size,
    string ImageSlug,
    string SshKeyId,
    string CloudInitUserData);

/// <summary>A cloud VPS instance, as reported back by its provider's REST API.</summary>
public sealed record CloudVpsServer(string Id, string? PublicIp, string Status);
