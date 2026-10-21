using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Turns a server's <see cref="JoinTarget"/> into a short <see cref="JoinCode"/> players can type,
    /// and back. RFC 0001 has two implementations in the adapter (UM-T13): Unity Relay (the default
    /// for listen servers, since it gets through NAT) and the self-hosted rendezvous service (the
    /// default for dedicated servers). <see cref="InMemoryJoinCodeService"/> is the in-process one.
    /// </summary>
    /// <remarks>
    /// Contract every implementation keeps:
    /// <list type="bullet">
    /// <item><see cref="PublishAsync"/> returns a non-empty code that <see cref="ResolveAsync"/> maps
    /// back to an equal target until it is revoked or expires.</item>
    /// <item>Codes are unique among live publications: two live publications never share a code.</item>
    /// <item><see cref="ResolveAsync"/> returns null (it does not throw) for a code that was never
    /// published, was revoked or expired. It throws only when the service itself cannot be reached.</item>
    /// <item><see cref="RevokeAsync"/> is idempotent: revoking an unknown or already revoked code
    /// returns false and changes nothing.</item>
    /// <item>A code is a <see cref="JoinCode"/>, so it has already passed the format check; parse
    /// player input with <see cref="JoinCode.TryParse(string, out JoinCodeParseResult)"/> first.</item>
    /// </list>
    /// </remarks>
    public interface IJoinCodeService
    {
        /// <summary>Publishes <paramref name="target"/> and returns the code players join with.</summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="System.NotSupportedException">This service cannot publish that <see cref="JoinTarget.Kind"/>.</exception>
        Task<JoinCode> PublishAsync(JoinTarget target, CancellationToken cancellationToken = default);

        /// <summary>The target <paramref name="code"/> leads to, or null when it is unknown, revoked or expired.</summary>
        Task<JoinTarget> ResolveAsync(JoinCode code, CancellationToken cancellationToken = default);

        /// <summary>Withdraws <paramref name="code"/> so it no longer resolves. True when it was live.</summary>
        Task<bool> RevokeAsync(JoinCode code, CancellationToken cancellationToken = default);
    }
}
