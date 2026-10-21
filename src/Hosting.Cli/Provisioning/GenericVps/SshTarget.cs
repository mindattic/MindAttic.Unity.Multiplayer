namespace Unity.Multiplayer.Hosting.Cli.Provisioning.GenericVps;

/// <summary>A bring-your-own VPS reached over SSH, resolved from <see cref="Credentials.HostingCredentialStore"/>.</summary>
public sealed record SshTarget(string Host, int Port, string User, string PrivateKeyPath, string? Passphrase);
