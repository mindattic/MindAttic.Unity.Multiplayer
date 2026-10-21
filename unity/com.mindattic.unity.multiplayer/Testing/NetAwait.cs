using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer.Testing
{
    /// <summary>
    /// Waits for a networked test, each with a timeout and a failure message that says what never happened. The
    /// <see cref="IEnumerator"/> forms are for <c>[UnityTest]</c> coroutines (<c>yield return</c> them); the
    /// <c>...Async</c> forms are the same waits as <see cref="Task"/>s, for async test code. A timeout fails the test through
    /// NUnit's <c>Assert.Fail</c>.
    /// </summary>
    /// <remarks>
    /// Time is real time (<see cref="Time.realtimeSinceStartup"/>), checked once a frame. <see cref="NetSessionTest"/> has
    /// the same waits as instance methods, timed by its own <c>TimeoutSeconds</c>; use these directly in any other test.
    /// </remarks>
    public static class NetAwait
    {
        /// <summary>How long a wait gives up after unless told otherwise: room for a few round trips on a loaded CI machine.</summary>
        public const float DefaultTimeoutSeconds = 10f;

        /// <summary>Waits until <paramref name="condition"/> holds; fails with <paramref name="failure"/> after the timeout.</summary>
        public static IEnumerator Until(Func<bool> condition, string failure, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            return Until(condition, () => failure, timeoutSeconds);
        }

        /// <summary>
        /// Waits until <paramref name="condition"/> holds; on a timeout fails with the message <paramref name="failure"/>
        /// builds then, so it can report the state things were left in.
        /// </summary>
        public static IEnumerator Until(Func<bool> condition, Func<string> failure, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            return Poll(condition, failure, timeoutSeconds);
        }

        /// <summary>
        /// Waits until <paramref name="peer"/> has a spawned network object carrying a <typeparamref name="T"/>, optionally owned
        /// by <paramref name="owner"/> and matching <paramref name="match"/>, and ends with that component.
        /// </summary>
        public static Awaiting<T> ForSpawn<T>(NetSession peer, ulong? owner = null, Func<T, bool> match = null, float timeoutSeconds = DefaultTimeoutSeconds)
            where T : Component
        {
            if (peer == null)
            {
                throw new ArgumentNullException(nameof(peer));
            }

            return new Awaiting<T>(set => Poll(
                () =>
                {
                    T found = FindSpawned(peer, owner, match);
                    set(found);
                    return found != null;
                },
                () => NeverSpawned<T>(peer, owner, match != null),
                timeoutSeconds));
        }

        /// <summary>
        /// Waits until <paramref name="peer"/> has its own copy of <paramref name="original"/> (a component on a spawned network
        /// object, usually the server's), matched by network object id, and ends with that copy.
        /// </summary>
        public static Awaiting<T> ForCopy<T>(NetSession peer, T original, float timeoutSeconds = DefaultTimeoutSeconds) where T : Component
        {
            if (original == null)
            {
                throw new ArgumentNullException(nameof(original));
            }

            NetworkObject networkObject = original.GetComponentInParent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned)
            {
                throw new ArgumentException(original.name + " is not on a spawned network object.", nameof(original));
            }

            ulong id = networkObject.NetworkObjectId;
            return ForSpawn<T>(peer, null, candidate => candidate.GetComponentInParent<NetworkObject>().NetworkObjectId == id, timeoutSeconds);
        }

        /// <summary>
        /// Waits until <paramref name="read"/> (a <c>[Synced]</c> field as one peer sees it) equals <paramref name="expected"/>;
        /// on a timeout fails naming <paramref name="what"/>, the value expected and the last value seen.
        /// </summary>
        public static IEnumerator ForSynced<T>(Func<T> read, T expected, string what, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            T last = default(T);
            return Poll(
                () => EqualityComparer<T>.Default.Equals(last = read(), expected),
                () => (what ?? "the synced value") + " never became " + Show(expected) + " (last " + Show(last) + ")",
                timeoutSeconds);
        }

        /// <summary>Waits for <paramref name="task"/>; rethrows what it threw, or fails naming <paramref name="what"/> on a timeout.</summary>
        public static IEnumerator ForTask(Task task, string what = null, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            return Finish(task, what, timeoutSeconds, null);
        }

        /// <summary>Waits for <paramref name="task"/> and ends with its result; rethrows what it threw, or fails on a timeout.</summary>
        public static Awaiting<T> ForTask<T>(Task<T> task, string what = null, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            return new Awaiting<T>(set => Finish(task, what, timeoutSeconds, () => set(task.Result)));
        }

        /// <summary>The async form of <see cref="Until(Func{bool}, string, float)"/>.</summary>
        public static Task UntilAsync(Func<bool> condition, string failure, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            return UntilAsync(condition, () => failure, timeoutSeconds);
        }

        /// <summary>The async form of <see cref="Until(Func{bool}, Func{string}, float)"/>.</summary>
        public static async Task UntilAsync(Func<bool> condition, Func<string> failure, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail(TimedOut(failure, timeoutSeconds));
                }

                await Awaitable.NextFrameAsync();
            }
        }

        /// <summary>The async form of <see cref="ForSpawn{T}"/>.</summary>
        public static async Task<T> ForSpawnAsync<T>(NetSession peer, ulong? owner = null, Func<T, bool> match = null, float timeoutSeconds = DefaultTimeoutSeconds)
            where T : Component
        {
            if (peer == null)
            {
                throw new ArgumentNullException(nameof(peer));
            }

            T found = null;
            await UntilAsync(() => (found = FindSpawned(peer, owner, match)) != null, () => NeverSpawned<T>(peer, owner, match != null), timeoutSeconds);
            return found;
        }

        /// <summary>The async form of <see cref="ForSynced{T}"/>.</summary>
        public static Task ForSyncedAsync<T>(Func<T> read, T expected, string what, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            T last = default(T);
            return UntilAsync(
                () => EqualityComparer<T>.Default.Equals(last = read(), expected),
                () => (what ?? "the synced value") + " never became " + Show(expected) + " (last " + Show(last) + ")",
                timeoutSeconds);
        }

        /// <summary>
        /// The first spawned <typeparamref name="T"/> on <paramref name="peer"/> owned by <paramref name="owner"/> (when given)
        /// and matching <paramref name="match"/>, or null. Looks on each spawned object and its children.
        /// </summary>
        public static T FindSpawned<T>(NetSession peer, ulong? owner = null, Func<T, bool> match = null) where T : Component
        {
            if (peer == null)
            {
                throw new ArgumentNullException(nameof(peer));
            }

            NetworkManager manager = peer.Manager;
            if (manager == null || manager.SpawnManager == null)
            {
                return null;
            }

            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null || (owner.HasValue && spawned.OwnerClientId != owner.Value))
                {
                    continue;
                }

                T component = spawned.GetComponentInChildren<T>(true);
                if (component != null && (match == null || match(component)))
                {
                    return component;
                }
            }

            return null;
        }

        /// <summary>A name for <paramref name="peer"/> in a failure message: its NetworkManager's GameObject name.</summary>
        internal static string Name(NetSession peer)
        {
            NetworkManager manager = peer.Manager;
            return manager != null ? manager.name : "a destroyed peer";
        }

        /// <summary>A session's state for a failure message, with why it last ended when it has.</summary>
        internal static string Describe(NetSession session)
        {
            string text = Name(session) + " is " + session.State;
            return session.LastDisconnect.Reason == Core.DisconnectReason.None ? text : text + " (last ended: " + session.LastDisconnect + ")";
        }

        private static IEnumerator Poll(Func<bool> condition, Func<string> failure, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail(TimedOut(failure, timeoutSeconds));
                }

                yield return null;
            }
        }

        private static IEnumerator Finish(Task task, string what, float timeoutSeconds, Action onSuccess)
        {
            string name = what ?? "a task";
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail(TimedOut(() => name + " never finished", timeoutSeconds));
                }

                yield return null;
            }

            if (task.IsFaulted)
            {
                ExceptionDispatchInfo.Capture(task.Exception.GetBaseException()).Throw();
            }

            if (task.IsCanceled)
            {
                Assert.Fail(name + " was cancelled");
            }

            if (onSuccess != null)
            {
                onSuccess();
            }
        }

        private static string NeverSpawned<T>(NetSession peer, ulong? owner, bool matched)
        {
            NetworkManager manager = peer.Manager;
            int count = manager != null && manager.SpawnManager != null ? manager.SpawnManager.SpawnedObjectsList.Count : 0;
            return Name(peer) + " never saw a spawned " + typeof(T).Name
                + (owner.HasValue ? " owned by client " + owner.Value : string.Empty)
                + (matched ? " matching the test's condition" : string.Empty)
                + " (" + peer.State + ", " + count + " object" + (count == 1 ? string.Empty : "s") + " spawned there)";
        }

        private static string TimedOut(Func<string> failure, float timeoutSeconds)
        {
            string message = failure != null ? failure() : null;
            return "Timed out after " + timeoutSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s: " + (string.IsNullOrEmpty(message) ? "the condition never held" : message);
        }

        private static string Show<T>(T value)
        {
            return value == null ? "null" : value.ToString();
        }
    }
}
