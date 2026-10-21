using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer.Rendezvous;

/// <summary>One live registration: a code, the server it leads to and the token that owns it.</summary>
public sealed record Registration(JoinCode Code, string Token, JoinTarget Target, DateTimeOffset ExpiresAt);

/// <summary>
/// The in-memory code table. Thread-safe. A registration lives for <see cref="RendezvousOptions.Ttl"/>
/// after it was made or last renewed; an expired one is gone at once for every caller (it is treated
/// as absent on read) and is swept out of memory by <see cref="ExpirySweeper"/>.
/// </summary>
/// <remarks>
/// Nothing is persisted: a restart forgets every code, and dedicated servers re-register on their
/// next heartbeat's 404 (UM-T13).
/// </remarks>
public sealed class RegistrationStore
{
    private const int MaxGenerationAttempts = 64;
    private const int TokenBytes = 32;

    private readonly Dictionary<JoinCode, Registration> registrations = new();
    private readonly object gate = new();
    private readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();
    private readonly TimeProvider time;
    private readonly RendezvousOptions options;

    /// <summary>Creates a store over <paramref name="options"/>, reading the clock from <paramref name="time"/>.</summary>
    public RegistrationStore(IOptions<RendezvousOptions> options, TimeProvider time)
    {
        this.options = options.Value;
        this.time = time;
    }

    /// <summary>Live and not-yet-swept registrations.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return registrations.Count;
            }
        }
    }

    /// <summary>
    /// Mints a fresh code for <paramref name="target"/>. Null when the store already holds
    /// <see cref="RendezvousOptions.MaxRegistrations"/> live registrations.
    /// </summary>
    public Registration? Register(JoinTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        DateTimeOffset now = time.GetUtcNow();

        lock (gate)
        {
            if (registrations.Count >= options.MaxRegistrations)
            {
                SweepLocked(now);
                if (registrations.Count >= options.MaxRegistrations)
                {
                    return null;
                }
            }

            for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
            {
                JoinCode code = JoinCode.Generate(options.CodeDataLength, rng);
                if (registrations.TryGetValue(code, out Registration? existing) && !IsExpired(existing, now))
                {
                    continue;
                }

                var registration = new Registration(code, NewToken(), target, now + options.Ttl);
                registrations[code] = registration;
                return registration;
            }
        }

        throw new InvalidOperationException(
            "Could not mint an unused join code in " + MaxGenerationAttempts + " attempts; raise CodeDataLength.");
    }

    /// <summary>
    /// Renews <paramref name="code"/> for another TTL. Null when it is unknown, expired, or
    /// <paramref name="token"/> is not its token (the three look the same to the caller).
    /// </summary>
    public Registration? Heartbeat(JoinCode code, string? token)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            Registration? current = FindOwnedLocked(code, token, now);
            if (current == null)
            {
                return null;
            }

            Registration renewed = current with { ExpiresAt = now + options.Ttl };
            registrations[code] = renewed;
            return renewed;
        }
    }

    /// <summary>The live registration for <paramref name="code"/>, or null.</summary>
    public Registration? Resolve(JoinCode code)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            return FindLiveLocked(code, now);
        }
    }

    /// <summary>Deletes <paramref name="code"/> if <paramref name="token"/> owns it. False otherwise.</summary>
    public bool Remove(JoinCode code, string? token)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            return FindOwnedLocked(code, token, now) != null && registrations.Remove(code);
        }
    }

    /// <summary>Drops every expired registration and returns how many went.</summary>
    public int SweepExpired()
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (gate)
        {
            return SweepLocked(now);
        }
    }

    private Registration? FindLiveLocked(JoinCode code, DateTimeOffset now)
    {
        if (code.IsEmpty || !registrations.TryGetValue(code, out Registration? registration))
        {
            return null;
        }

        if (IsExpired(registration, now))
        {
            registrations.Remove(code);
            return null;
        }

        return registration;
    }

    private Registration? FindOwnedLocked(JoinCode code, string? token, DateTimeOffset now)
    {
        Registration? registration = FindLiveLocked(code, now);
        return registration != null && TokensMatch(registration.Token, token) ? registration : null;
    }

    private int SweepLocked(DateTimeOffset now)
    {
        List<JoinCode>? expired = null;
        foreach (KeyValuePair<JoinCode, Registration> pair in registrations)
        {
            if (IsExpired(pair.Value, now))
            {
                (expired ??= new List<JoinCode>()).Add(pair.Key);
            }
        }

        if (expired == null)
        {
            return 0;
        }

        foreach (JoinCode code in expired)
        {
            registrations.Remove(code);
        }

        return expired.Count;
    }

    private static bool IsExpired(Registration registration, DateTimeOffset now) => now >= registration.ExpiresAt;

    private static string NewToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TokensMatch(string expected, string? actual)
    {
        if (string.IsNullOrEmpty(actual))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }
}
