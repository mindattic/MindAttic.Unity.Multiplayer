using Microsoft.Extensions.Options;

namespace Unity.Multiplayer.Rendezvous;

/// <summary>
/// Frees the memory of expired registrations every <see cref="RendezvousOptions.SweepIntervalSeconds"/>.
/// Expiry itself does not wait for it: the store already treats an expired code as absent.
/// </summary>
public sealed class ExpirySweeper : BackgroundService
{
    private readonly RegistrationStore store;
    private readonly TimeProvider time;
    private readonly TimeSpan interval;
    private readonly ILogger<ExpirySweeper> logger;

    /// <summary>Creates the sweeper.</summary>
    public ExpirySweeper(RegistrationStore store, IOptions<RendezvousOptions> options, TimeProvider time, ILogger<ExpirySweeper> logger)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
        interval = TimeSpan.FromSeconds(options.Value.SweepIntervalSeconds);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                int swept = store.SweepExpired();
                if (swept > 0)
                {
                    logger.LogInformation("Swept {Count} expired registration(s); {Live} live.", swept, store.Count);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
