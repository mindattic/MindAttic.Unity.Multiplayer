namespace Unity.Multiplayer.Hosting.Cli.Deploy;

/// <summary>The end-to-end result of one <c>mp-host deploy</c> run.</summary>
public sealed record DeployOutcome(
    string ServerId,
    string PublicAddress,
    bool Healthy,
    string? JoinCode);
