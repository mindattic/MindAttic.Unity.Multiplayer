using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// An <see cref="IJoinCodeService"/> that keeps its codes in this process: for tests, local play
    /// and as the reference behaviour the networked implementations match. Thread-safe.
    /// </summary>
    /// <remarks>Codes never expire here; they live until <see cref="RevokeAsync"/>.</remarks>
    public sealed class InMemoryJoinCodeService : IJoinCodeService, IDisposable
    {
        private const int MaxGenerationAttempts = 64;

        private readonly Dictionary<JoinCode, JoinTarget> targets = new Dictionary<JoinCode, JoinTarget>();
        private readonly object gate = new object();
        private readonly RandomNumberGenerator rng;
        private readonly bool ownsRng;
        private readonly int dataLength;

        /// <summary>Mints <see cref="JoinCodeFormat.DefaultDataLength"/>-character codes from a cryptographic RNG.</summary>
        public InMemoryJoinCodeService()
            : this(JoinCodeFormat.DefaultDataLength, null)
        {
        }

        /// <summary>
        /// Mints codes with <paramref name="dataLength"/> data characters from <paramref name="rng"/>
        /// (a cryptographic RNG when null; the service does not dispose one it was given).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="dataLength"/> is out of range.</exception>
        public InMemoryJoinCodeService(int dataLength, RandomNumberGenerator rng)
        {
            if (dataLength < JoinCodeFormat.MinDataLength || dataLength > JoinCodeFormat.MaxDataLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dataLength), dataLength,
                    "Data length must be between " + JoinCodeFormat.MinDataLength + " and " + JoinCodeFormat.MaxDataLength + ".");
            }

            this.dataLength = dataLength;
            ownsRng = rng == null;
            this.rng = rng ?? RandomNumberGenerator.Create();
        }

        /// <summary>How many codes are live.</summary>
        public int Count
        {
            get
            {
                lock (gate)
                {
                    return targets.Count;
                }
            }
        }

        /// <inheritdoc />
        public Task<JoinCode> PublishAsync(JoinTarget target, CancellationToken cancellationToken = default)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
                {
                    JoinCode code = JoinCode.Generate(dataLength, rng);
                    if (!targets.ContainsKey(code))
                    {
                        targets.Add(code, target);
                        return Task.FromResult(code);
                    }
                }
            }

            throw new InvalidOperationException(
                "Could not mint an unused join code in " + MaxGenerationAttempts + " attempts; the code space is exhausted or the RNG is not random.");
        }

        /// <inheritdoc />
        public Task<JoinTarget> ResolveAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JoinTarget target;
            lock (gate)
            {
                targets.TryGetValue(code, out target);
            }

            return Task.FromResult(target);
        }

        /// <inheritdoc />
        public Task<bool> RevokeAsync(JoinCode code, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool removed;
            lock (gate)
            {
                removed = targets.Remove(code);
            }

            return Task.FromResult(removed);
        }

        /// <summary>Disposes the RNG if this service created it.</summary>
        public void Dispose()
        {
            if (ownsRng)
            {
                rng.Dispose();
            }
        }
    }
}
