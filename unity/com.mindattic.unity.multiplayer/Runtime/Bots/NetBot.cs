using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// What a game's <see cref="IBotDriver"/> gets (UM-T57): the connected <see cref="Session"/>, the bot's
    /// <see cref="Role"/> and <see cref="Scenario"/>, the verdict (<see cref="Pass"/>, <see cref="Fail"/>) and waits that
    /// resume on the main thread (<see cref="NextFrame"/>, <see cref="Seconds"/>, <see cref="Until"/>,
    /// <see cref="Spawned{T}"/>, <see cref="LocalPlayer{T}"/>). Every wait throws <see cref="OperationCanceledException"/>
    /// once the run has failed or timed out, so a driver never outlives its verdict by accident.
    /// </summary>
    public sealed class NetBot
    {
        private readonly NetBotRunner _runner;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

        internal NetBot(NetBotRunner runner, NetSession session, string role, string scenario)
        {
            _runner = runner;
            Session = session;
            Role = role;
            Scenario = scenario;
        }

        /// <summary>The bot's session: connected when the driver starts.</summary>
        public NetSession Session { get; }

        /// <summary>The role this bot plays (<c>-bot A</c> gives <c>A</c>).</summary>
        public string Role { get; }

        /// <summary>The scenario (<c>-scenario</c>), or null.</summary>
        public string Scenario { get; }

        /// <summary>The code it joined by, or null when it joined by address.</summary>
        public string JoinCode { get; internal set; }

        /// <summary>Cancelled when the run fails or times out.</summary>
        public CancellationToken Cancellation
        {
            get { return _cancel.Token; }
        }

        /// <summary>True once <see cref="Pass"/> was called (and the run had not failed).</summary>
        public bool HasPassed
        {
            get { return _runner.State == NetBotState.Passed; }
        }

        /// <summary>Seconds until the run times out; 0 once it has, infinite after it passed.</summary>
        public float SecondsLeft
        {
            get { return _runner.SecondsLeft; }
        }

        /// <summary>Logs <c>[NetBot:role] message</c>.</summary>
        public void Log(string message)
        {
            Debug.Log(BotLog.Line(Role, message));
        }

        /// <summary>
        /// Passes the run: logs <c>[NetBot:role] OK: message</c> (the line the compose smoke waits for) and raises
        /// <see cref="NetBotRunner.Finished"/>. The driver may keep playing; the timeout no longer applies. Ignored after a
        /// verdict.
        /// </summary>
        public void Pass(string message)
        {
            _runner.Pass(message);
        }

        /// <summary>
        /// Fails the run: logs <c>[NetBot:role] FAIL: message</c>, cancels <see cref="Cancellation"/> and (in a player)
        /// quits with exit code 1. Ignored after a verdict.
        /// </summary>
        public void Fail(string message)
        {
            _runner.Fail(message);
        }

        /// <summary>Resumes on the next frame.</summary>
        public Task NextFrame()
        {
            return _runner.NextFrame(Cancellation);
        }

        /// <summary>Resumes after <paramref name="seconds"/> of real time.</summary>
        public async Task Seconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
            do
            {
                await NextFrame();
            }
            while (Time.realtimeSinceStartup < until);
        }

        /// <summary>
        /// Resumes once <paramref name="condition"/> holds, checked every frame. Throws <see cref="TimeoutException"/>
        /// naming <paramref name="what"/> after <paramref name="timeoutSeconds"/> (default: the rest of the run).
        /// </summary>
        public async Task Until(Func<bool> condition, string what, float? timeoutSeconds = null)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            float until = timeoutSeconds.HasValue ? Time.realtimeSinceStartup + timeoutSeconds.Value : float.PositiveInfinity;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                {
                    throw new TimeoutException("waited " + timeoutSeconds.Value.ToString("0.#") + " s for " + (what ?? "a condition") + ".");
                }

                await NextFrame();
            }
        }

        /// <summary>The first object spawned on this client that carries a <typeparamref name="T"/> (and matches <paramref name="match"/>), or null.</summary>
        public T FindSpawned<T>(Func<T, bool> match = null) where T : Component
        {
            NetworkManager manager = Session.Manager;
            if (manager == null || manager.SpawnManager == null)
            {
                return null;
            }

            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                T component = spawned != null ? spawned.GetComponent<T>() : null;
                if (component != null && (match == null || match(component)))
                {
                    return component;
                }
            }

            return null;
        }

        /// <summary>Every object spawned on this client that carries a <typeparamref name="T"/>.</summary>
        public List<T> FindAllSpawned<T>() where T : Component
        {
            var found = new List<T>();
            NetworkManager manager = Session.Manager;
            if (manager == null || manager.SpawnManager == null)
            {
                return found;
            }

            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                T component = spawned != null ? spawned.GetComponent<T>() : null;
                if (component != null)
                {
                    found.Add(component);
                }
            }

            return found;
        }

        /// <summary>Waits for <see cref="FindSpawned{T}"/> to find one.</summary>
        public async Task<T> Spawned<T>(Func<T, bool> match = null, float? timeoutSeconds = null) where T : Component
        {
            T found = null;
            await Until(() => (found = FindSpawned(match)) != null, "a spawned " + typeof(T).Name, timeoutSeconds);
            return found;
        }

        /// <summary>Waits for this client's own player object and returns its <typeparamref name="T"/>.</summary>
        public async Task<T> LocalPlayer<T>(float? timeoutSeconds = null) where T : Component
        {
            T found = null;
            await Until(() =>
            {
                NetworkManager manager = Session.Manager;
                NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
                found = player != null ? player.GetComponent<T>() : null;
                return found != null;
            }, "this bot's player object (" + typeof(T).Name + ")", timeoutSeconds);
            return found;
        }

        internal void Cancel()
        {
            if (!_cancel.IsCancellationRequested)
            {
                _cancel.Cancel();
            }
        }
    }
}
