using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>What the receiving side decided about one RPC call.</summary>
    public enum RpcVerdict
    {
        /// <summary>The call passes; its handler runs.</summary>
        Accepted = 0,

        /// <summary>A <see cref="ToServerAttribute"/> call arrived somewhere that is not the server.</summary>
        NotServer = 1,

        /// <summary>A <see cref="ToServerAttribute"/> call that requires ownership came from a client that does not own the entity.</summary>
        NotOwner = 2,

        /// <summary>The sender went over the RPC's <see cref="ToServerAttribute.RateLimit"/>.</summary>
        RateLimited = 3,

        /// <summary>A server-to-client call (<see cref="ToClientsAttribute"/>, <see cref="ToOwnerAttribute"/>, <see cref="ToClientAttribute"/>) did not come from the server.</summary>
        NotFromServer = 4,

        /// <summary>Game code outside the server tried to send a server-to-client call; nothing was sent.</summary>
        SentFromClient = 5,
    }

    /// <summary>One RPC call that was dropped instead of handled, and why.</summary>
    public readonly struct RpcRejection
    {
        /// <summary>Creates the record.</summary>
        public RpcRejection(string rpc, ulong senderClientId, ulong objectId, RpcVerdict verdict)
        {
            Rpc = rpc ?? string.Empty;
            SenderClientId = senderClientId;
            ObjectId = objectId;
            Verdict = verdict;
        }

        /// <summary>The RPC's key: the declaring type and method name, e.g. <c>CursorAvatar.Move</c>.</summary>
        public string Rpc { get; }

        /// <summary>The client id that sent (or tried to send) the call.</summary>
        public ulong SenderClientId { get; }

        /// <summary>The network object id of the entity the call was for.</summary>
        public ulong ObjectId { get; }

        /// <summary>Why it was dropped.</summary>
        public RpcVerdict Verdict { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Rpc + " from client " + SenderClientId + " on object " + ObjectId + ": " + Verdict;
        }
    }

    /// <summary>
    /// The engine-free checks every generated RPC stub runs before calling the game's handler: on the server, a
    /// <see cref="ToServerAttribute"/> call must come from the entity's owner (when required) and be within the sender's
    /// rate; on a client, a server-to-client call must come from the server.
    /// </summary>
    /// <remarks>One gate per server session; the adapter keeps it with the session and forgets a sender when it leaves.</remarks>
    public sealed class RpcGate
    {
        /// <summary>A gate with its own, empty rate limiter.</summary>
        public RpcGate()
            : this(new RpcRateLimiter())
        {
        }

        /// <summary>A gate over <paramref name="limiter"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="limiter"/> is null.</exception>
        public RpcGate(RpcRateLimiter limiter)
        {
            Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        }

        /// <summary>The per-sender buckets behind <see cref="ToServerAttribute.RateLimit"/>.</summary>
        public RpcRateLimiter Limiter { get; }

        /// <summary>Decides one incoming <see cref="ToServerAttribute"/> call.</summary>
        /// <param name="rpc">The RPC key (declaring type and method name).</param>
        /// <param name="isServer">True when this process is the server.</param>
        /// <param name="sender">The calling client id.</param>
        /// <param name="owner">The entity's owner client id.</param>
        /// <param name="requireOwnership">The RPC's <see cref="ToServerAttribute.RequireOwnership"/>.</param>
        /// <param name="rateLimit">The RPC's <see cref="ToServerAttribute.RateLimit"/>; 0 for none.</param>
        /// <param name="burst">The RPC's <see cref="ToServerAttribute.Burst"/>; 0 for one second's worth.</param>
        /// <param name="now">The current time in seconds.</param>
        /// <remarks>
        /// The server itself (<see cref="NetSender.ServerClientId"/>, which on a host is also the host's own player) passes
        /// the ownership check: the authority may always act. Ownership is checked before the rate, so a refused
        /// non-owner spends none of the owner's budget.
        /// </remarks>
        public RpcVerdict CheckToServer(string rpc, bool isServer, ulong sender, ulong owner, bool requireOwnership, int rateLimit, int burst, double now)
        {
            if (!isServer)
            {
                return RpcVerdict.NotServer;
            }

            if (requireOwnership && sender != owner && sender != NetSender.ServerClientId)
            {
                return RpcVerdict.NotOwner;
            }

            if (!Limiter.TryAcquire(sender, rpc, rateLimit, burst, now))
            {
                return RpcVerdict.RateLimited;
            }

            return RpcVerdict.Accepted;
        }

        /// <summary>Decides one incoming server-to-client call: it must come from the server.</summary>
        public static RpcVerdict CheckFromServer(ulong sender)
        {
            return sender == NetSender.ServerClientId ? RpcVerdict.Accepted : RpcVerdict.NotFromServer;
        }

        /// <summary>Forgets a sender's rate buckets (it disconnected).</summary>
        public void Forget(ulong sender)
        {
            Limiter.Forget(sender);
        }

        /// <summary>Forgets everything (the session ended).</summary>
        public void Clear()
        {
            Limiter.Clear();
        }
    }
}
