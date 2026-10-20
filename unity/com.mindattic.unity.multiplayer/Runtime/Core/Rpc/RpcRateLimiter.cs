using System;
using System.Collections.Generic;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// A token bucket: holds up to <see cref="Capacity"/> tokens, refills at <see cref="RatePerSecond"/>, and each
    /// accepted call takes one. Starts full, so a sender may burst <see cref="Capacity"/> calls and then
    /// <see cref="RatePerSecond"/> a second after that.
    /// </summary>
    /// <remarks>Engine-free: time is passed in, in seconds, on any clock that never runs backwards.</remarks>
    public sealed class TokenBucket
    {
        private double _tokens;
        private double _last;

        /// <summary>A full bucket of <paramref name="capacity"/> tokens refilled at <paramref name="ratePerSecond"/>, as of <paramref name="now"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The rate is not positive (or is NaN or infinite) or the capacity is below one.</exception>
        public TokenBucket(double ratePerSecond, int capacity, double now)
        {
            if (double.IsNaN(ratePerSecond) || double.IsInfinity(ratePerSecond) || ratePerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ratePerSecond), ratePerSecond, "The refill rate must be a positive number.");
            }

            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A bucket holds at least one token.");
            }

            RatePerSecond = ratePerSecond;
            Capacity = capacity;
            _tokens = capacity;
            _last = now;
        }

        /// <summary>Tokens added per second.</summary>
        public double RatePerSecond { get; }

        /// <summary>The most tokens the bucket holds.</summary>
        public int Capacity { get; }

        /// <summary>Tokens available as of the last call (refilled lazily by <see cref="TryTake"/> and <see cref="Available"/>).</summary>
        public double Tokens
        {
            get { return _tokens; }
        }

        /// <summary>The tokens available at <paramref name="now"/>.</summary>
        public double Available(double now)
        {
            Refill(now);
            return _tokens;
        }

        /// <summary>Takes one token if there is one at <paramref name="now"/>.</summary>
        /// <returns>True when the call is allowed.</returns>
        public bool TryTake(double now)
        {
            Refill(now);
            if (_tokens < 1.0)
            {
                return false;
            }

            _tokens -= 1.0;
            return true;
        }

        private void Refill(double now)
        {
            if (now > _last)
            {
                _tokens = Math.Min(Capacity, _tokens + (now - _last) * RatePerSecond);
                _last = now;
            }
        }
    }

    /// <summary>
    /// The server's per-sender rate limiter for <see cref="ToServerAttribute"/> calls: one <see cref="TokenBucket"/> per
    /// (sender, RPC), so a client flooding one RPC is throttled without touching other clients or its other RPCs.
    /// </summary>
    /// <remarks>
    /// <para>The RPC key is any stable string; the adapter uses the declaring type and method name, so the limit is per
    /// sender across every entity of that type (spawning more entities buys no extra rate).</para>
    /// <para>Not thread-safe; the adapter calls it from Unity's main thread. Call <see cref="Forget"/> when a client leaves.</para>
    /// </remarks>
    public sealed class RpcRateLimiter
    {
        private readonly Dictionary<Key, TokenBucket> _buckets = new Dictionary<Key, TokenBucket>();
        private readonly Dictionary<ulong, long> _rejected = new Dictionary<ulong, long>();

        /// <summary>The number of live buckets (one per sender and limited RPC it has called).</summary>
        public int BucketCount
        {
            get { return _buckets.Count; }
        }

        /// <summary>
        /// Lets one <paramref name="rpc"/> call from <paramref name="sender"/> through at <paramref name="now"/>, or not.
        /// </summary>
        /// <param name="sender">The calling client id.</param>
        /// <param name="rpc">A stable key for the RPC.</param>
        /// <param name="ratePerSecond">The limit; 0 or less means unlimited (always true, no bucket kept).</param>
        /// <param name="burst">The bucket size; 0 or less means <paramref name="ratePerSecond"/> (at least 1).</param>
        /// <param name="now">The current time in seconds.</param>
        /// <returns>True when the call is within the limit.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rpc"/> is null.</exception>
        public bool TryAcquire(ulong sender, string rpc, double ratePerSecond, int burst, double now)
        {
            if (rpc == null)
            {
                throw new ArgumentNullException(nameof(rpc));
            }

            if (ratePerSecond <= 0 || double.IsNaN(ratePerSecond))
            {
                return true;
            }

            int capacity = burst > 0 ? burst : Math.Max(1, (int)Math.Ceiling(ratePerSecond));
            var key = new Key(sender, rpc);
            TokenBucket bucket;
            if (!_buckets.TryGetValue(key, out bucket) || bucket.Capacity != capacity || bucket.RatePerSecond != ratePerSecond)
            {
                bucket = new TokenBucket(ratePerSecond, capacity, now);
                _buckets[key] = bucket;
            }

            if (bucket.TryTake(now))
            {
                return true;
            }

            long count;
            _rejected.TryGetValue(sender, out count);
            _rejected[sender] = count + 1;
            return false;
        }

        /// <summary>How many calls from <paramref name="sender"/> this limiter has refused since it last forgot the sender.</summary>
        public long RejectedCount(ulong sender)
        {
            long count;
            return _rejected.TryGetValue(sender, out count) ? count : 0;
        }

        /// <summary>Drops every bucket and count for <paramref name="sender"/> (it disconnected).</summary>
        public void Forget(ulong sender)
        {
            _rejected.Remove(sender);
            var stale = new List<Key>();
            foreach (Key key in _buckets.Keys)
            {
                if (key.Sender == sender)
                {
                    stale.Add(key);
                }
            }

            foreach (Key key in stale)
            {
                _buckets.Remove(key);
            }
        }

        /// <summary>Drops everything (the session ended).</summary>
        public void Clear()
        {
            _buckets.Clear();
            _rejected.Clear();
        }

        private readonly struct Key : IEquatable<Key>
        {
            public Key(ulong sender, string rpc)
            {
                Sender = sender;
                Rpc = rpc;
            }

            public ulong Sender { get; }

            public string Rpc { get; }

            public bool Equals(Key other)
            {
                return Sender == other.Sender && string.Equals(Rpc, other.Rpc, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Sender.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(Rpc);
                }
            }
        }
    }
}
