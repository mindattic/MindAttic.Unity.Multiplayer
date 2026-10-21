using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// An <see cref="IMatchAllocator"/>'s provider refused a request, or could not be reached. Every
    /// adapter wraps its provider-specific errors in this, never leaking an HTTP or SDK exception type.
    /// </summary>
    public sealed class MatchAllocationException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public MatchAllocationException(string message, bool isTransient = false, Exception inner = null)
            : base(message, inner)
        {
            IsTransient = isTransient;
        }

        /// <summary>True when trying again later may work (the provider was unreachable, rate-limited, or had a transient error).</summary>
        public bool IsTransient { get; }
    }
}
