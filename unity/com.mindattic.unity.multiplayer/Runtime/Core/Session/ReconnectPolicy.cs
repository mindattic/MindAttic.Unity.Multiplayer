using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// When a client that lost its game should try to get back in, how often, and how long to wait between tries. A
    /// reconnect sends the same player token, so within the server's <see cref="NetConfig.ReconnectGraceSeconds"/> the
    /// player gets the same seat and player object back (see <see cref="SeatRegistry"/>).
    /// </summary>
    /// <remarks>
    /// Only a connection that was lost is retried (<see cref="DisconnectReason.TimedOut"/>,
    /// <see cref="DisconnectReason.TransportFailure"/>). A server that shut down, a kick, a refusal and the player's own
    /// leave are final: retrying them would only fail again or undo what someone chose.
    /// </remarks>
    public sealed class ReconnectPolicy
    {
        /// <summary>Default <see cref="MaxAttempts"/>.</summary>
        public const int DefaultMaxAttempts = 5;

        /// <summary>Default <see cref="FirstDelaySeconds"/>.</summary>
        public const double DefaultFirstDelaySeconds = 1.0;

        /// <summary>Default <see cref="MaxDelaySeconds"/>.</summary>
        public const double DefaultMaxDelaySeconds = 8.0;

        /// <summary>Tries before giving up; 0 turns reconnecting off.</summary>
        public int MaxAttempts { get; set; } = DefaultMaxAttempts;

        /// <summary>Seconds before the first try; each later try waits twice as long as the one before.</summary>
        public double FirstDelaySeconds { get; set; } = DefaultFirstDelaySeconds;

        /// <summary>The longest wait between two tries.</summary>
        public double MaxDelaySeconds { get; set; } = DefaultMaxDelaySeconds;

        /// <summary>
        /// True when a client whose session ended with <paramref name="disconnection"/> should try to reconnect:
        /// it had been connected (<paramref name="wasConnected"/>; a join that never got in is not retried), reconnecting
        /// is on, and the connection was lost rather than closed on purpose.
        /// </summary>
        public bool ShouldReconnect(Disconnection disconnection, bool wasConnected)
        {
            return MaxAttempts > 0 && wasConnected && IsLostConnection(disconnection.Reason);
        }

        /// <summary>
        /// True when a failed reconnect try (<paramref name="disconnection"/>) is worth another: the server could not be
        /// reached yet or the link failed again. A refusal (full, version, banned) or a kick ends the retries.
        /// </summary>
        public static bool IsRetryable(Disconnection disconnection)
        {
            return disconnection.Reason == DisconnectReason.ConnectFailed || IsLostConnection(disconnection.Reason);
        }

        /// <summary>True for the reasons that mean the link was lost: <see cref="DisconnectReason.TimedOut"/> and <see cref="DisconnectReason.TransportFailure"/>.</summary>
        public static bool IsLostConnection(DisconnectReason reason)
        {
            return reason == DisconnectReason.TimedOut || reason == DisconnectReason.TransportFailure;
        }

        /// <summary>Seconds to wait before try <paramref name="attempt"/> (1-based): doubling from <see cref="FirstDelaySeconds"/>, capped at <see cref="MaxDelaySeconds"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempt"/> is less than 1.</exception>
        public double DelayBefore(int attempt)
        {
            if (attempt < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempts count from 1.");
            }

            double delay = Math.Max(0.0, FirstDelaySeconds);
            for (int i = 1; i < attempt && delay < MaxDelaySeconds; i++)
            {
                delay *= 2.0;
            }

            return Math.Min(delay, Math.Max(0.0, MaxDelaySeconds));
        }
    }
}
