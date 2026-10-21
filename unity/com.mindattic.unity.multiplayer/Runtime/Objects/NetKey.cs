using System;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// A networked object's stable, game-given name (UM-T60): put one on a network prefab's root, have the server give
    /// each instance a key before it spawns (a level's block id, "door:A", "chest:3"), and every peer finds that object
    /// by the key in <see cref="NetSession.Objects"/> (<see cref="NetRegistry"/>), whatever order objects spawned in and
    /// whenever this peer joined.
    /// </summary>
    /// <remarks>
    /// <para>The key travels inside the object's spawn (NGO's per-behaviour synchronization data), so a client knows it
    /// before any of the object's <c>OnSpawned</c> runs; late joiners get it the same way. It never changes while the
    /// object is spawned: setting <see cref="Key"/> then throws. Rules for keys are in <see cref="NetKeys"/>.</para>
    /// <para>Spawn keyed objects with <see cref="NetRegistry.Spawn"/>, which refuses a key a live object already holds
    /// before anything spawns. An object spawned some other way with a missing or duplicate key is reported as an error
    /// and left out of the registry (the first object keeps the key).</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetKey : NetEntity
    {
        [SerializeField]
        [Tooltip("The key this object spawns with when none is set in code. Usually left empty and set by the server per instance.")]
        private string _key = string.Empty;

        private NetRegistry _registeredIn;

        /// <summary>
        /// The key. Set it on the server before the object spawns (<see cref="NetRegistry.Spawn"/> does); every peer reads
        /// the same value once spawned.
        /// </summary>
        /// <exception cref="InvalidOperationException">Set while the object is spawned (keys are stable).</exception>
        /// <exception cref="ArgumentException">The value breaks <see cref="NetKeys.Validate"/>.</exception>
        public string Key
        {
            get { return _key; }
            set
            {
                if (IsSpawned)
                {
                    throw new InvalidOperationException(
                        "NetKey " + NetKeys.Describe(_key) + " on " + name + " is already spawned; a key is stable for the object's whole life. Set it before spawning.");
                }

                string problem = NetKeys.Validate(value);
                if (problem != null)
                {
                    throw new ArgumentException(problem, nameof(value));
                }

                _key = value;
            }
        }

        /// <summary>True while this object is the one the registry hands out for <see cref="Key"/>.</summary>
        public bool IsRegistered
        {
            get { return _registeredIn != null; }
        }

        /// <summary>The object for messages: its name, key and network object id.</summary>
        public override string ToString()
        {
            return name + " (key " + NetKeys.Describe(_key) + ", network object " + (IsSpawned ? NetworkObjectId.ToString() : "not spawned") + ")";
        }

        /// <inheritdoc />
        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            string key = _key ?? string.Empty;
            serializer.SerializeValue(ref key);
            if (serializer.IsReader)
            {
                _key = key;
            }
        }

        /// <inheritdoc />
        protected override void OnNetworkPostSpawn()
        {
            base.OnNetworkPostSpawn();

            // After every component of the object has run its OnSpawned, so a Spawned handler sees a ready object.
            NetSession session = Session;
            if (session == null)
            {
                Debug.LogWarning("[MindAttic.Multiplayer] " + this + " spawned on a NetworkManager no NetSession drives; it is not in any NetRegistry.", this);
                return;
            }

            if (session.Objects.Add(this))
            {
                _registeredIn = session.Objects;
            }
        }

        /// <inheritdoc />
        public override void OnNetworkPreDespawn()
        {
            base.OnNetworkPreDespawn();
            Unregister();
        }

        /// <inheritdoc />
        protected override void OnDespawned()
        {
            // OnNetworkPreDespawn normally got here first; this covers a despawn that skipped it.
            Unregister();
        }

        private void Unregister()
        {
            NetRegistry registry = _registeredIn;
            _registeredIn = null;
            if (registry != null)
            {
                registry.Remove(this);
            }
        }
    }
}
