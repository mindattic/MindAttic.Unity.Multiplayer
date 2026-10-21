namespace Unity.Multiplayer.Rendezvous;

/// <summary>
/// <c>--healthcheck</c>: GETs the running service's <c>/healthz</c> and exits 0 when it answers 200,
/// 1 otherwise. The probe URL is <c>RENDEZVOUS_HEALTH_URL</c>, else <c>http://localhost:{first
/// ASPNETCORE_HTTP_PORTS port, or 8080}/healthz</c>.
/// </summary>
internal static class HealthProbe
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        string url = Environment.GetEnvironmentVariable("RENDEZVOUS_HEALTH_URL") ?? DefaultUrl();
        using var handler = new HttpClientHandler
        {
            // Probing our own port; a self-signed certificate on localhost is fine here.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using HttpResponseMessage response = await client.GetAsync(url);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine("Health probe of " + url + " failed: " + ex.Message);
            return 1;
        }
    }

    private static string DefaultUrl()
    {
        string? ports = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        string port = ports?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "8080";
        return "http://localhost:" + port + Core.RendezvousProtocol.HealthPath;
    }
}
