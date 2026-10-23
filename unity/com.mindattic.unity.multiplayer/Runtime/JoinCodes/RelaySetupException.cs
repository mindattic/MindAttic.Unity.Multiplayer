using System;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer
{
    /// <summary>
    /// Unity Relay could not be used, with a message a developer (or player) can act on (<see cref="RelaySetup"/>): the
    /// project is not linked to a Unity Cloud project, Relay is off for it, the environment does not exist, or Unity
    /// Gaming Services could not be reached. The Unity Services exception, when there was one, is the inner exception.
    /// </summary>
    public sealed class RelaySetupException : InvalidOperationException
    {
        /// <summary>Creates the exception.</summary>
        public RelaySetupException(RelayReadiness readiness, string message, Exception inner = null)
            : base(message, inner)
        {
            Readiness = readiness;
        }

        /// <summary>Where the project stood when it failed (<see cref="RelayReadiness.Ready"/> for a failure past setup).</summary>
        public RelayReadiness Readiness { get; }
    }
}
