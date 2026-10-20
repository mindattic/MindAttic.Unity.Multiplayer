using System.ComponentModel.DataAnnotations;
using MindAttic.Unity.Multiplayer.Core;

namespace MindAttic.Unity.Multiplayer.Rendezvous;

/// <summary>
/// The service's settings, bound from the <c>Rendezvous</c> configuration section (appsettings.json,
/// or environment variables such as <c>Rendezvous__RegistrationSecret</c>).
/// </summary>
public sealed class RendezvousOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Rendezvous";

    /// <summary>
    /// The shared secret a dedicated server must send to register. Required unless
    /// <see cref="AllowOpenRegistration"/> is set; the service refuses to start without one.
    /// </summary>
    public string? RegistrationSecret { get; set; }

    /// <summary>Lets anyone register with no secret. For local development only.</summary>
    public bool AllowOpenRegistration { get; set; }

    /// <summary>How long a registration lives without a heartbeat. Default 60 s.</summary>
    [Range(5, 86400)]
    public int TtlSeconds { get; set; } = 60;

    /// <summary>Data characters per code (5 bits each, plus a check character). Default 8 (40 bits).</summary>
    [Range(JoinCodeFormat.MinDataLength, JoinCodeFormat.MaxDataLength)]
    public int CodeDataLength { get; set; } = JoinCodeFormat.DefaultDataLength;

    /// <summary>Most live registrations the store holds; a register beyond it gets 503. Default 10 000.</summary>
    [Range(1, 10_000_000)]
    public int MaxRegistrations { get; set; } = 10_000;

    /// <summary>How often expired registrations are swept out. Default 15 s.</summary>
    [Range(1, 3600)]
    public int SweepIntervalSeconds { get; set; } = 15;

    /// <summary>Per-IP limits: requests per window for each route group.</summary>
    public RateLimitOptions RateLimits { get; set; } = new();

    /// <summary>
    /// Honour <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> from a reverse proxy that terminates
    /// TLS. Only proxies in <see cref="KnownProxies"/> are trusted (loopback always is).
    /// </summary>
    public bool UseForwardedHeaders { get; set; }

    /// <summary>IP addresses of trusted reverse proxies, for <see cref="UseForwardedHeaders"/>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Redirect HTTP to HTTPS and send HSTS. Turn on when Kestrel itself serves TLS.</summary>
    public bool RequireHttps { get; set; }

    /// <summary>The <see cref="TtlSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Ttl => TimeSpan.FromSeconds(TtlSeconds);
}

/// <summary>Per-IP fixed-window limits.</summary>
public sealed class RateLimitOptions
{
    /// <summary>Window length in seconds. Default 60.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Registrations per IP per window. Default 10.</summary>
    [Range(1, 1_000_000)]
    public int RegisterPerWindow { get; set; } = 10;

    /// <summary>Heartbeats and deletes per IP per window. Default 120.</summary>
    [Range(1, 1_000_000)]
    public int HeartbeatPerWindow { get; set; } = 120;

    /// <summary>
    /// Resolves per IP per window. Default 60. This is what makes guessing codes hopeless: with 40-bit
    /// codes an IP would need billions of windows to find one live code among thousands.
    /// </summary>
    [Range(1, 1_000_000)]
    public int ResolvePerWindow { get; set; } = 60;
}
