// A crate: a networked object the server spawns with a key ("crate:0", "crate:1", ...) through
// NetSession.Objects.Spawn. Its prefab carries a NetKey beside the NetworkObject, so every peer, late joiners included,
// finds it by that key (session.Objects.TryGet("crate:0", out Crate crate)) instead of by spawn order.
using Unity.Multiplayer;      // NetEntity
using Unity.Multiplayer.Core; // [Synced]
using UnityEngine;

namespace MyGame
{
    public partial class Crate : NetEntity
    {
        // Pushed by players on the server (Arena.Push); every peer draws it where the server says.
        [Synced] private Vector2 _position;

        private NetKey _key;

        /// <summary>The key the server spawned this crate with.</summary>
        public string Key
        {
            get
            {
                if (_key == null)
                {
                    _key = GetComponent<NetKey>();
                }

                return _key != null ? _key.Key : null;
            }
        }

        protected override void OnSpawned()
        {
            Shapes.Square(gameObject, 0.8f, -1).color = new Color32(176, 132, 84, 255);
            if (IsServer && Position == Vector2.zero)
            {
                Position = Arena.CrateStart(Arena.CrateIndex(Key));
            }
        }

        private void Update()
        {
            if (IsSpawned)
            {
                transform.position = Position;
            }
        }
    }
}
