namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

/// <summary>
/// The one <c>flyctl</c>-shaped operation <see cref="FlyProvisioner"/> needs that the Machines API
/// has no simple REST equivalent for: recent application logs (Fly ships these over its own
/// NATS-based log stream, which <c>flyctl logs</c> already knows how to read). Everything else
/// (provisioning, status, update, destroy) goes through <see cref="IFlyApiClient"/> instead.
/// </summary>
public interface IFlyCli
{
    /// <summary>The app's recent stdout/stderr across its machines (newest last), up to <paramref name="tailLines"/> lines.</summary>
    Task<string> GetLogsAsync(string appName, int tailLines, CancellationToken ct);
}
