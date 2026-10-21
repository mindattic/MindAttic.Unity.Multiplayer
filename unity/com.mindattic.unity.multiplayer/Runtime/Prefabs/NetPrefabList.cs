using System.Collections.Generic;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// An asset listing the prefabs a game spawns over the network. Assign it to <see cref="NetBootstrap"/> (or pass it to
    /// <see cref="NetPrefabRegistry.Register(NetPrefabList)"/>) and every prefab in it is registered before the session
    /// starts, on server and clients alike. Each prefab needs a NetworkObject on its root.
    /// </summary>
    /// <remarks>The MindAttic counterpart of NGO's NetworkPrefabsList, so game code never names an NGO type (BIBLE UM-LAW-2).</remarks>
    [CreateAssetMenu(menuName = "MindAttic/Multiplayer/Net Prefab List", fileName = "NetPrefabs")]
    public sealed class NetPrefabList : ScriptableObject
    {
        [Tooltip("Prefabs spawned over the network. Each needs a NetworkObject on its root.")]
        [SerializeField] private List<GameObject> prefabs = new List<GameObject>();

        /// <summary>The listed prefabs (nulls included, as the Inspector holds them).</summary>
        public IReadOnlyList<GameObject> Prefabs
        {
            get { return prefabs; }
        }

        /// <summary>Adds <paramref name="prefab"/> to the list (for code that builds a list at runtime).</summary>
        public void Add(GameObject prefab)
        {
            if (prefab != null && !prefabs.Contains(prefab))
            {
                prefabs.Add(prefab);
            }
        }
    }
}
