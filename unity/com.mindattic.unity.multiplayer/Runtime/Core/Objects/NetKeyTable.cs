using System;
using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>What <see cref="NetKeyTable{T}.Add"/> did.</summary>
    public enum NetKeyAddResult
    {
        /// <summary>The item now holds the key.</summary>
        Added,

        /// <summary>The item already held the key; nothing changed.</summary>
        AlreadyHeld,

        /// <summary>Another item holds the key; the table is unchanged (<see cref="NetKeys.DuplicateMessage"/>).</summary>
        Duplicate,

        /// <summary>The key breaks <see cref="NetKeys.Validate"/>; the table is unchanged.</summary>
        InvalidKey,
    }

    /// <summary>
    /// Live objects by their network key (UM-T60), the engine-free core of the adapter's <c>NetRegistry</c>: at most one
    /// item per key, a second claim on a held key refused, and a removal honoured only for the item that holds the key
    /// (so a late despawn of an old object never unregisters the new one that took its key).
    /// </summary>
    /// <remarks>Not thread-safe; the adapter uses it from Unity's main thread.</remarks>
    public sealed class NetKeyTable<T> where T : class
    {
        private readonly Dictionary<string, T> _byKey = new Dictionary<string, T>(StringComparer.Ordinal);

        /// <summary>How many keys are held.</summary>
        public int Count
        {
            get { return _byKey.Count; }
        }

        /// <summary>Every held key (a copy).</summary>
        public IReadOnlyList<string> Keys
        {
            get { return new List<string>(_byKey.Keys); }
        }

        /// <summary>Every item that holds a key (a copy).</summary>
        public IReadOnlyList<T> Items
        {
            get { return new List<T>(_byKey.Values); }
        }

        /// <summary>Gives <paramref name="item"/> <paramref name="key"/> unless the key is invalid or another item holds it.</summary>
        /// <param name="key">The key to claim.</param>
        /// <param name="item">The item claiming it.</param>
        /// <param name="holder">The item holding the key afterwards (the existing one on <see cref="NetKeyAddResult.Duplicate"/>); null for an invalid key.</param>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
        public NetKeyAddResult Add(string key, T item, out T holder)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (!NetKeys.IsValid(key))
            {
                holder = null;
                return NetKeyAddResult.InvalidKey;
            }

            T existing;
            if (_byKey.TryGetValue(key, out existing))
            {
                holder = existing;
                return ReferenceEquals(existing, item) ? NetKeyAddResult.AlreadyHeld : NetKeyAddResult.Duplicate;
            }

            _byKey.Add(key, item);
            holder = item;
            return NetKeyAddResult.Added;
        }

        /// <summary>Frees <paramref name="key"/> if <paramref name="item"/> holds it.</summary>
        /// <returns>True when the key was freed; false when it is unknown or held by another item.</returns>
        public bool Remove(string key, T item)
        {
            T existing;
            if (key == null || item == null || !_byKey.TryGetValue(key, out existing) || !ReferenceEquals(existing, item))
            {
                return false;
            }

            return _byKey.Remove(key);
        }

        /// <summary>The item holding <paramref name="key"/>.</summary>
        public bool TryGet(string key, out T item)
        {
            if (key == null)
            {
                item = null;
                return false;
            }

            return _byKey.TryGetValue(key, out item);
        }

        /// <summary>The item holding <paramref name="key"/>.</summary>
        /// <exception cref="KeyNotFoundException">No item holds it (<see cref="NetKeys.NotFoundMessage"/>).</exception>
        public T Get(string key)
        {
            T item;
            if (!TryGet(key, out item))
            {
                throw new KeyNotFoundException(NetKeys.NotFoundMessage(key));
            }

            return item;
        }

        /// <summary>True when some item holds <paramref name="key"/>.</summary>
        public bool Contains(string key)
        {
            return key != null && _byKey.ContainsKey(key);
        }

        /// <summary>Forgets every key.</summary>
        public void Clear()
        {
            _byKey.Clear();
        }
    }
}
