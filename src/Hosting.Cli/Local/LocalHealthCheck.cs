using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>
/// Polls the health port directly over loopback HTTP, the local-hosting equivalent of
/// <see cref="Provisioning.CloudVm.CloudVmSshOperations.WaitForHealthyAsync"/> -- there is no SSH hop
/// needed when mp-host and the container are on the same machine.
/// </summary>
public static class LocalHealthCheck
{
    /// <summary>Polls <c>GET http://127.0.0.1:&lt;healthPort&gt;/healthz</c> until it answers 200, or <paramref name="timeout"/> elapses.</summary>
    public static async Task<bool> WaitForHealthyAsync(HttpClient http, ServerPackageManifest manifest, TimeSpan timeout, IDeployLog log, CancellationToken ct)
    {
        ManifestPort? health = manifest.HealthPort;
        if (health is null)
        {
            log.Info("No health port declared; skipping the health check.");
            return true;
        }

        string url = $"http://127.0.0.1:{health.Port}{health.Path ?? "/healthz"}";
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    log.Info("The local server is healthy.");
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Still starting up; retry until the timeout.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        log.Warn($"The local server did not report healthy within {timeout}.");
        return false;
    }
}
