using System;
using System.Threading;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A clean shutdown, requested once from any thread (a SIGTERM or SIGINT handler, the <c>stop</c> admin command, a
    /// quit) and carried out on the game's thread: stop taking players, let the game save, close the session, quit,
    /// with a deadline after which the process quits anyway.
    /// </summary>
    public sealed class ServerShutdown
    {
        private int _requested;
        private string _reason;
        private double _requestedAt;

        /// <summary>True once <see cref="Request"/> has been called.</summary>
        public bool IsRequested
        {
            get { return Volatile.Read(ref _requested) != 0; }
        }

        /// <summary>Why the first request came; null before one.</summary>
        public string Reason
        {
            get { return Volatile.Read(ref _reason); }
        }

        /// <summary>When the first request came, on the clock passed to <see cref="Request"/>.</summary>
        public double RequestedAt
        {
            get { return Volatile.Read(ref _requestedAt); }
        }

        /// <summary>Asks for a shutdown. Only the first call counts; it returns true, later ones false.</summary>
        /// <param name="reason">Why, for the log (<c>SIGTERM</c>, <c>admin stop</c>).</param>
        /// <param name="now">The time on the caller's clock, for <see cref="IsOverdue"/>.</param>
        public bool Request(string reason, double now)
        {
            if (Interlocked.CompareExchange(ref _requested, 1, 0) != 0)
            {
                return false;
            }

            Volatile.Write(ref _requestedAt, now);
            Volatile.Write(ref _reason, string.IsNullOrWhiteSpace(reason) ? "requested" : reason);
            return true;
        }

        /// <summary>True when a shutdown was requested more than <paramref name="graceSeconds"/> before <paramref name="now"/>.</summary>
        public bool IsOverdue(double now, double graceSeconds)
        {
            return IsRequested && now - RequestedAt >= Math.Max(0.0, graceSeconds);
        }
    }
}
