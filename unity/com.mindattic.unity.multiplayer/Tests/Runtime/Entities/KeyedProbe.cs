namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T60: a game component beside a <see cref="NetKey"/>. It records the key as it stood when its own
    /// <c>OnSpawned</c> ran, and whether the registry already had the object then, to pin down what a game may rely on.
    /// </summary>
    public sealed class KeyedProbe : NetEntity
    {
        /// <summary>The object's key when this component's OnSpawned ran; null before.</summary>
        public string KeyAtSpawn { get; private set; }

        /// <summary>How many times OnDespawned ran.</summary>
        public int Despawns { get; private set; }

        protected override void OnSpawned()
        {
            NetKey key = GetComponent<NetKey>();
            KeyAtSpawn = key != null ? key.Key : null;
        }

        protected override void OnDespawned()
        {
            Despawns++;
        }
    }
}
