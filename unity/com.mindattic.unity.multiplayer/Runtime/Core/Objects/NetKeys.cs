using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The rules for a networked object's key (UM-T60): the stable name a game gives an object on the server (a level's
    /// block id, a door's name, "chest:3") that every peer looks the object up by, instead of guessing it from spawn
    /// order or a network object id. The adapter's <c>NetKey</c> carries it and <c>NetRegistry</c> indexes it.
    /// </summary>
    /// <remarks>Keys are compared ordinally (case matters) and are unique among the objects spawned at any one time.</remarks>
    public static class NetKeys
    {
        /// <summary>The longest key, in characters.</summary>
        public const int MaxLength = 128;

        /// <summary>Why <paramref name="key"/> cannot be a key, or null when it can.</summary>
        public static string Validate(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "A network key cannot be empty.";
            }

            if (key.Length > MaxLength)
            {
                return "A network key is at most " + MaxLength + " characters; " + Describe(key) + " has " + key.Length + ".";
            }

            if (char.IsWhiteSpace(key[0]) || char.IsWhiteSpace(key[key.Length - 1]))
            {
                return "A network key cannot start or end with whitespace: " + Describe(key) + ".";
            }

            foreach (char c in key)
            {
                if (char.IsControl(c))
                {
                    return "A network key cannot hold control characters: " + Describe(key) + ".";
                }
            }

            return null;
        }

        /// <summary>True when <paramref name="key"/> can be a key (<see cref="Validate"/> finds nothing wrong).</summary>
        public static bool IsValid(string key)
        {
            return Validate(key) == null;
        }

        /// <summary>The error for a second object spawned with a key a live object already has.</summary>
        /// <param name="key">The key both claim.</param>
        /// <param name="holder">The object that has it (for the message).</param>
        /// <param name="newcomer">The object that was refused (for the message).</param>
        public static string DuplicateMessage(string key, string holder, string newcomer)
        {
            return "Network key " + Describe(key) + " is already used by " + holder + "; " + newcomer
                + " was not given it. Keys must be unique among spawned objects: despawn the old object first, or give this one another key.";
        }

        /// <summary>The error for a lookup of a key no spawned object has.</summary>
        public static string NotFoundMessage(string key)
        {
            return "No spawned object has network key " + Describe(key) + " on this peer (not spawned yet, already despawned, or never keyed).";
        }

        /// <summary><paramref name="key"/> quoted for a message (null shown as such).</summary>
        public static string Describe(string key)
        {
            return key == null ? "(null)" : "\"" + key + "\"";
        }
    }
}
