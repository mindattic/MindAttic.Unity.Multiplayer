using System;
using System.Collections.Generic;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// The spawned objects that carry a <see cref="NetKey"/>, by key, on every peer (UM-T60). The server gives an object
    /// its key when it spawns it (<see cref="Spawn"/>); each peer looks it up with <see cref="TryGet{T}"/> /
    /// <see cref="Get{T}"/> and hears of arrivals and departures through <see cref="Spawned"/> / <see cref="Despawned"/>.
    /// A client therefore never has to match objects up by spawn order or network object id: late joiners, a different
    /// spawn order and objects arriving one by one all give the same answer.
    /// </summary>
    /// <remarks>
    /// <para>Get it from <see cref="NetSession.Objects"/> (or <see cref="Current"/>). A key is held by at most one spawned
    /// object at a time; <see cref="Spawn"/> refuses a key in use before anything spawns. Events are raised on Unity's main
    /// thread, after every component of the object has run its <c>OnSpawned</c> (and before any runs its
    /// <c>OnDespawned</c>); a handler that throws is logged and does not stop the others.</para>
    /// <para>The registry empties when its session starts and ends, without raising <see cref="Despawned"/> for what it
    /// forgets.</para>
    /// </remarks>
    public sealed class NetRegistry
    {
        private readonly NetSession _session;
        private readonly NetKeyTable<NetKey> _table = new NetKeyTable<NetKey>();

        internal NetRegistry(NetSession session)
        {
            _session = session;
        }

        /// <summary>The registry of <see cref="NetBootstrap.Instance"/>'s session; null when there is none.</summary>
        public static NetRegistry Current
        {
            get
            {
                NetBootstrap bootstrap = NetBootstrap.Instance;
                return bootstrap != null && bootstrap.Session != null ? bootstrap.Session.Objects : null;
            }
        }

        /// <summary>A keyed object spawned on this peer (for a late joiner: each object already in the game, as it arrives).</summary>
        public event Action<NetKey> Spawned;

        /// <summary>A keyed object is despawning on this peer; its key is free again once handlers return.</summary>
        public event Action<NetKey> Despawned;

        /// <summary>How many keyed objects are spawned on this peer.</summary>
        public int Count
        {
            get { return _table.Count; }
        }

        /// <summary>Every key held on this peer (a copy).</summary>
        public IReadOnlyList<string> Keys
        {
            get { return _table.Keys; }
        }

        /// <summary>Every keyed object spawned on this peer (a copy).</summary>
        public IReadOnlyList<NetKey> All
        {
            get { return _table.Items; }
        }

        /// <summary>True when a spawned object holds <paramref name="key"/> on this peer.</summary>
        public bool Contains(string key)
        {
            return _table.Contains(key);
        }

        /// <summary>
        /// The spawned object holding <paramref name="key"/>, as its <typeparamref name="T"/> component (the
        /// <see cref="NetKey"/> itself, or any component on the object or its children).
        /// </summary>
        /// <returns>False when no object holds the key on this peer, or it has no <typeparamref name="T"/>.</returns>
        public bool TryGet<T>(string key, out T value) where T : Component
        {
            NetKey holder;
            if (!_table.TryGet(key, out holder) || holder == null)
            {
                value = null;
                return false;
            }

            value = holder as T;
            if (value == null)
            {
                value = holder.GetComponentInChildren<T>(true);
            }

            return value != null;
        }

        /// <summary>The <see cref="GameObject"/> of the spawned object holding <paramref name="key"/>.</summary>
        public bool TryGet(string key, out GameObject value)
        {
            NetKey holder;
            bool found = TryGet(key, out holder);
            value = found ? holder.gameObject : null;
            return found;
        }

        /// <summary>The spawned object holding <paramref name="key"/>, as its <typeparamref name="T"/> component.</summary>
        /// <exception cref="KeyNotFoundException">No object holds the key on this peer.</exception>
        /// <exception cref="InvalidOperationException">The object has no <typeparamref name="T"/>.</exception>
        public T Get<T>(string key) where T : Component
        {
            NetKey holder = _table.Get(key);
            T value;
            if (!TryGet(key, out value))
            {
                throw new InvalidOperationException(holder + " has no " + typeof(T).Name + ".");
            }

            return value;
        }

        /// <summary>
        /// Server: spawns an instance of <paramref name="prefab"/> (a registered network prefab with a <see cref="NetKey"/> on
        /// its root) holding <paramref name="key"/>, at <paramref name="position"/> under <paramref name="parent"/>, owned by
        /// <paramref name="ownerClientId"/> (default the server). Every peer finds it under that key once it arrives.
        /// </summary>
        /// <returns>The spawned instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="prefab"/> is null.</exception>
        /// <exception cref="ArgumentException">The key breaks <see cref="NetKeys.Validate"/>, a spawned object already holds it,
        /// or the prefab has no <see cref="NetworkObject"/> or no <see cref="NetKey"/> on its root.</exception>
        /// <exception cref="InvalidOperationException">This peer is not a running Host or Server.</exception>
        public GameObject Spawn(GameObject prefab, string key, Vector3 position = default(Vector3), Quaternion? rotation = null,
            Transform parent = null, ulong? ownerClientId = null)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            string problem = NetKeys.Validate(key);
            if (problem != null)
            {
                throw new ArgumentException(problem, nameof(key));
            }

            NetKey existing;
            if (_table.TryGet(key, out existing))
            {
                throw new ArgumentException(NetKeys.DuplicateMessage(key, existing.ToString(), "a new " + prefab.name), nameof(key));
            }

            if (prefab.GetComponent<NetworkObject>() == null || prefab.GetComponent<NetKey>() == null)
            {
                throw new ArgumentException(prefab.name + " needs a NetworkObject and a NetKey on its root to be spawned with a key.", nameof(prefab));
            }

            NetworkManager manager = _session.Manager;
            if (!_session.IsServer || manager == null || !manager.IsListening)
            {
                throw new InvalidOperationException("NetRegistry.Spawn runs on a running Host or Server.");
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation ?? Quaternion.identity, parent);
            instance.GetComponent<NetKey>().Key = key;
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();

            // NetworkObject.Spawn uses NetworkManager.Singleton for an instance no manager owns yet; make it this session's
            // (a test process runs several) and put back whichever was the singleton before.
            NetworkManager previous = NetworkManager.Singleton;
            if (previous != manager)
            {
                manager.SetSingleton();
            }

            try
            {
                if (ownerClientId.HasValue && ownerClientId.Value != NetworkManager.ServerClientId)
                {
                    networkObject.SpawnWithOwnership(ownerClientId.Value);
                }
                else
                {
                    networkObject.Spawn();
                }
            }
            catch
            {
                UnityEngine.Object.Destroy(instance);
                throw;
            }
            finally
            {
                if (previous != null && previous != manager)
                {
                    previous.SetSingleton();
                }
            }

            return instance;
        }

        /// <summary>Server: despawns and destroys the object holding <paramref name="key"/>.</summary>
        /// <returns>False when no object holds it.</returns>
        /// <exception cref="InvalidOperationException">This peer is not the server.</exception>
        public bool Despawn(string key)
        {
            if (!_session.IsServer)
            {
                throw new InvalidOperationException("NetRegistry.Despawn runs on the Host or Server.");
            }

            NetKey holder;
            if (!_table.TryGet(key, out holder) || holder == null || holder.NetworkObject == null || !holder.NetworkObject.IsSpawned)
            {
                return false;
            }

            holder.NetworkObject.Despawn(true);
            return true;
        }

        /// <summary>A keyed object spawned: index it, or report why not.</summary>
        /// <returns>True when it now holds its key here.</returns>
        internal bool Add(NetKey item)
        {
            NetKey holder;
            switch (_table.Add(item.Key, item, out holder))
            {
                case NetKeyAddResult.Added:
                    Raise(Spawned, item);
                    return true;
                case NetKeyAddResult.AlreadyHeld:
                    return true;
                case NetKeyAddResult.Duplicate:
                    Debug.LogError("[MindAttic.Multiplayer] " + NetKeys.DuplicateMessage(item.Key, holder.ToString(), item.ToString()), item);
                    return false;
                default:
                    Debug.LogError("[MindAttic.Multiplayer] " + item + " spawned without a usable network key: " + NetKeys.Validate(item.Key)
                        + " Give it one before spawning (NetRegistry.Spawn does).", item);
                    return false;
            }
        }

        /// <summary>A keyed object is despawning: raise <see cref="Despawned"/>, then free its key.</summary>
        internal void Remove(NetKey item)
        {
            NetKey holder;
            if (!_table.TryGet(item.Key, out holder) || !ReferenceEquals(holder, item))
            {
                return;
            }

            try
            {
                Raise(Despawned, item);
            }
            finally
            {
                _table.Remove(item.Key, item);
            }
        }

        /// <summary>Forgets everything (the session started or ended).</summary>
        internal void Reset()
        {
            _table.Clear();
        }

        private static void Raise(Action<NetKey> handler, NetKey value)
        {
            if (handler == null)
            {
                return;
            }

            foreach (Delegate each in handler.GetInvocationList())
            {
                try
                {
                    ((Action<NetKey>)each)(value);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
