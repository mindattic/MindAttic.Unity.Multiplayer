namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Lightsail;

/// <summary>
/// The slice of Lightsail's API <see cref="LightsailProvisioner"/> needs. The only seam between the
/// provisioner and the network, so NUnit mocks it directly instead of faking signed HTTP/JSON.
/// </summary>
public interface ILightsailApiClient
{
    /// <summary>Creates one instance from a blueprint (OS image) + bundle (size) and waits for Lightsail to accept the request.</summary>
    Task<LightsailInstance> CreateInstanceAsync(LightsailCreateInstanceRequest request, CancellationToken ct);

    /// <summary>The instance's current state and public IP.</summary>
    Task<LightsailInstance> GetInstanceAsync(string instanceName, CancellationToken ct);

    /// <summary>
    /// Replaces the instance's open public ports with exactly <paramref name="ports"/> -- Lightsail
    /// has no separate security-group resource; the firewall lives on the instance itself.
    /// </summary>
    Task OpenPublicPortsAsync(string instanceName, IReadOnlyList<LightsailPortRange> ports, CancellationToken ct);

    /// <summary>Deletes an instance (and its firewall with it). Safe to call on an already-deleted one.</summary>
    Task DeleteInstanceAsync(string instanceName, CancellationToken ct);
}
