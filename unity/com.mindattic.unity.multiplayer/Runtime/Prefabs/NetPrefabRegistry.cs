using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// The prefabs a session can spawn over the network: a thin, checked layer over NGO's prefab registration. Get it from
    /// <see cref="NetSession.Prefabs"/>.
    /// </summary>
    /// <remarks>
    /// <para>Every peer must register the same prefabs before its session starts (NGO's ForceSamePrefabs, on by default);
    /// registering while a session is active throws instead of failing obscurely later. Registering a prefab twice is
    /// harmless. Prefabs in NGO NetworkPrefabsList assets already on the NetworkManager count as registered.</para>
    /// </remarks>
    public sealed class NetPrefabRegistry
    {
        private readonly NetworkManager _manager;

        internal NetPrefabRegistry(NetworkManager manager)
        {
            _manager = manager;
        }

        /// <summary>Every registered prefab: the NetworkManager's prefab lists plus those added at runtime.</summary>
        public IReadOnlyList<GameObject> Prefabs
        {
            get
            {
                var all = new List<GameObject>();
                NetworkPrefabs prefabs = Config().Prefabs;
                foreach (NetworkPrefab prefab in prefabs.Prefabs)
                {
                    AddDistinct(all, prefab.Prefab);
                }

                foreach (NetworkPrefabsList list in prefabs.NetworkPrefabsLists)
                {
                    if (list == null)
                    {
                        continue;
                    }

                    foreach (NetworkPrefab prefab in list.PrefabList)
                    {
                        AddDistinct(all, prefab.Prefab);
                    }
                }

                return all;
            }
        }

        /// <summary>True when <paramref name="prefab"/> is registered.</summary>
        public bool Contains(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            NetworkPrefabs prefabs = Config().Prefabs;
            if (prefabs.Contains(prefab))
            {
                return true;
            }

            foreach (NetworkPrefabsList list in prefabs.NetworkPrefabsLists)
            {
                if (list != null && list.Contains(prefab))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Registers one prefab.</summary>
        /// <returns>True when it was added; false when it was already registered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="prefab"/> is null.</exception>
        /// <exception cref="ArgumentException">The prefab has no NetworkObject on its root.</exception>
        /// <exception cref="InvalidOperationException">A session is running and every peer must register prefabs before it starts.</exception>
        public bool Register(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                throw new ArgumentException("Prefab '" + prefab.name + "' has no NetworkObject on its root, so it cannot be spawned over the network.", nameof(prefab));
            }

            if (Contains(prefab))
            {
                return false;
            }

            EnsureNotRunning(prefab.name);
            _manager.AddNetworkPrefab(prefab);
            return true;
        }

        /// <summary>Registers every prefab in a <see cref="NetPrefabList"/> (null entries are skipped).</summary>
        /// <returns>How many prefabs were newly added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
        /// <exception cref="ArgumentException">A listed prefab has no NetworkObject (nothing after it is registered).</exception>
        /// <exception cref="InvalidOperationException">A session is running.</exception>
        public int Register(NetPrefabList list)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            int added = 0;
            foreach (GameObject prefab in list.Prefabs)
            {
                if (prefab != null && Register(prefab))
                {
                    added++;
                }
            }

            return added;
        }

        /// <summary>
        /// Adds an NGO NetworkPrefabsList asset to the NetworkManager's lists (for projects that already keep one, such as
        /// NGO's DefaultNetworkPrefabs).
        /// </summary>
        /// <returns>True when it was added; false when the manager already had it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A session is running.</exception>
        public bool Register(NetworkPrefabsList list)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            NetworkPrefabs prefabs = Config().Prefabs;
            if (prefabs.NetworkPrefabsLists.Contains(list))
            {
                return false;
            }

            EnsureNotRunning(list.name);
            prefabs.NetworkPrefabsLists.Add(list);
            return true;
        }

        private NetworkConfig Config()
        {
            if (_manager.NetworkConfig == null)
            {
                _manager.NetworkConfig = new NetworkConfig();
            }

            return _manager.NetworkConfig;
        }

        private void EnsureNotRunning(string what)
        {
            if (_manager.IsListening && Config().ForceSamePrefabs)
            {
                throw new InvalidOperationException(
                    "Cannot register '" + what + "' while a session is running: every peer must register the same prefabs before it starts.");
            }
        }

        private static void AddDistinct(List<GameObject> all, GameObject prefab)
        {
            if (prefab != null && !all.Contains(prefab))
            {
                all.Add(prefab);
            }
        }
    }
}
