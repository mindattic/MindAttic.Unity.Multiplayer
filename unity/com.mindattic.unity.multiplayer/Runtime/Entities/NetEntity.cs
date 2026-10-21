using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// The base class for every networked game component. A game derives a <c>partial</c> class from it, declares
    /// <see cref="ToServerAttribute"/> / <see cref="ToClientsAttribute"/> / <see cref="ToOwnerAttribute"/> /
    /// <see cref="ToClientAttribute"/> partial methods and <see cref="SyncedAttribute"/> fields, and writes the
    /// <c>On&lt;Name&gt;</c> handlers; the plumbing is generated (RFC 0001).
    /// </summary>
    /// <remarks>
    /// <para>Not generic, and a game's subclasses must not be generic either: NGO's IL post-processor cannot give a
    /// generic class stable RPC ids. The generator rejects a generic subclass at compile time (MAMP005), and one that
    /// slips through is reported as an error when it spawns.</para>
    /// <para>Override <see cref="OnSpawned"/> and <see cref="OnDespawned"/> instead of NGO's
    /// <c>OnNetworkSpawn</c>/<c>OnNetworkDespawn</c>, which this class seals so synced-field change handlers are always
    /// wired.</para>
    /// <para>The <c>Net*</c> members marked <see cref="EditorBrowsableState.Never"/> are for generated code only: the
    /// RPC stubs call them to check ownership, rate and direction before a handler runs (a refused call is dropped,
    /// logged and raised as <see cref="NetSession.RpcRejected"/>).</para>
    /// </remarks>
    public abstract class NetEntity : NetworkBehaviour
    {
        /// <summary>Rate and ownership state for NetworkManagers no <see cref="NetSession"/> wraps (keyed by manager).</summary>
        private static readonly Dictionary<NetworkManager, RpcGate> s_LooseGates = new Dictionary<NetworkManager, RpcGate>();

        /// <summary>The session running this entity's NetworkManager, or null when NGO is driven without one.</summary>
        public NetSession Session
        {
            get { return NetSession.For(NetworkManager); }
        }

        /// <summary>The owning client as a <see cref="NetSender"/> (client id, seat and name when the session seats players).</summary>
        public NetSender Owner
        {
            get { return SenderFor(OwnerClientId); }
        }

        /// <summary>Called on every peer when the entity has spawned and its synced fields hold their first values.</summary>
        protected virtual void OnSpawned()
        {
        }

        /// <summary>Called on every peer just before the entity despawns.</summary>
        protected virtual void OnDespawned()
        {
        }

        /// <inheritdoc />
        public sealed override void OnNetworkSpawn()
        {
            if (GetType().IsGenericType)
            {
                Debug.LogError("[MindAttic.Multiplayer] " + GetType().Name + " is generic; NetEntity subclasses must not be generic (their RPCs would have no stable ids).", this);
            }

            NetBindSynced();
            OnSpawned();
        }

        /// <inheritdoc />
        public sealed override void OnNetworkDespawn()
        {
            try
            {
                OnDespawned();
            }
            finally
            {
                NetUnbindSynced();
            }
        }

        /// <summary>The <see cref="NetSender"/> for <paramref name="clientId"/>: its seat and name when a session seats it.</summary>
        protected NetSender SenderFor(ulong clientId)
        {
            NetSession session = Session;
            return session != null ? session.GetSender(clientId) : new NetSender(clientId, -1, string.Empty);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Generated-code surface. The generator (UM-T11) emits calls to these; games do not call them.
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>Generated code: subscribes every <see cref="SyncedAttribute.OnChanged"/> handler. Overrides call base first.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void NetBindSynced()
        {
        }

        /// <summary>Generated code: unsubscribes what <see cref="NetBindSynced"/> subscribed. Overrides call base last.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void NetUnbindSynced()
        {
        }

        /// <summary>
        /// Generated code, server side of a <see cref="ToServerAttribute"/> stub: checks the call and resolves its sender.
        /// </summary>
        /// <returns>True when the handler should run; false when the call was dropped (and reported).</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool NetReceiveToServer(string rpc, ulong senderClientId, bool requireOwnership, int rateLimit, int burst, out NetSender sender)
        {
            RpcGate gate = GateFor(NetworkManager);
            RpcVerdict verdict = gate.CheckToServer(rpc, IsServer, senderClientId, OwnerClientId, requireOwnership, rateLimit, burst, Time.unscaledTimeAsDouble);
            if (verdict != RpcVerdict.Accepted)
            {
                sender = default(NetSender);
                Reject(new RpcRejection(rpc, senderClientId, NetworkObjectId, verdict), gate);
                return false;
            }

            sender = SenderFor(senderClientId);
            return true;
        }

        /// <summary>
        /// Generated code, sending body of a server-to-client call: only the server may send one.
        /// </summary>
        /// <returns>True when this process is the server; otherwise nothing is sent and the attempt is reported.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool NetCanSendToClients(string rpc)
        {
            if (IsServer)
            {
                return true;
            }

            Reject(new RpcRejection(rpc, NetworkManager != null ? NetworkManager.LocalClientId : 0, NetworkObjectId, RpcVerdict.SentFromClient), null);
            return false;
        }

        /// <summary>Generated code, receiving side of a server-to-client stub: the call must come from the server.</summary>
        /// <returns>True when the handler should run.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool NetReceiveFromServer(string rpc, ulong senderClientId)
        {
            RpcVerdict verdict = RpcGate.CheckFromServer(senderClientId);
            if (verdict == RpcVerdict.Accepted)
            {
                return true;
            }

            Reject(new RpcRejection(rpc, senderClientId, NetworkObjectId, verdict), null);
            return false;
        }

        /// <summary>
        /// Generated code, setter of a <see cref="SyncedAttribute"/> property: only the server writes synced state
        /// (BIBLE UM-LAW-1). Before the entity spawns, any side may set the initial value.
        /// </summary>
        /// <exception cref="InvalidOperationException">A spawned entity's synced value was written somewhere other than the server.</exception>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected void NetWriteSynced<T>(NetworkVariable<T> variable, T value, string name)
        {
            if (IsSpawned && !IsServer)
            {
                throw new InvalidOperationException(
                    GetType().Name + "." + name + " is synced state: only the server writes it. Send the server a [ToServer] call instead.");
            }

            variable.Value = value;
        }

        /// <summary>The gate for <paramref name="manager"/>: its session's, else a loose one kept per manager.</summary>
        internal static RpcGate GateFor(NetworkManager manager)
        {
            NetSession session = NetSession.For(manager);
            if (session != null)
            {
                return session.Gate;
            }

            if (manager == null)
            {
                return new RpcGate();
            }

            RpcGate gate;
            if (!s_LooseGates.TryGetValue(manager, out gate))
            {
                PruneLooseGates();
                gate = new RpcGate();
                s_LooseGates[manager] = gate;
            }

            return gate;
        }

        private static void PruneLooseGates()
        {
            var dead = new List<NetworkManager>();
            foreach (NetworkManager manager in s_LooseGates.Keys)
            {
                if (manager == null)
                {
                    dead.Add(manager);
                }
            }

            foreach (NetworkManager manager in dead)
            {
                s_LooseGates.Remove(manager);
            }
        }

        private void Reject(RpcRejection rejection, RpcGate gate)
        {
            NetSession session = Session;
            if (session != null)
            {
                session.ReportRejected(rejection);
                return;
            }

            // No session: log like the session would (a rate-limited flood logs on the 1st, 10th, 100th... refusal).
            if (rejection.Verdict != RpcVerdict.RateLimited || NetSession.ShouldLogRateLimited(gate.Limiter.RejectedCount(rejection.SenderClientId)))
            {
                Debug.LogWarning("[MindAttic.Multiplayer] Dropped RPC " + rejection);
            }
        }
    }
}
