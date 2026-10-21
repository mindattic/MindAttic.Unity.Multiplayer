using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer
{
    /// <summary>How a networked scene load treats the scenes already open.</summary>
    public enum NetSceneMode
    {
        /// <summary>Replaces every open scene (a level change).</summary>
        Single = 0,

        /// <summary>Opens alongside the current scenes (a sub-area, a lobby overlay); unload it with <see cref="NetSceneFlow.Unload"/>.</summary>
        Additive = 1,
    }

    /// <summary>What happened to a <see cref="NetSceneFlow.Load"/> or <see cref="NetSceneFlow.Unload"/> request.</summary>
    public enum SceneRequestResult
    {
        /// <summary>The server began the change; every client follows. Watch <see cref="NetSceneFlow.Loaded"/> / <see cref="NetSceneFlow.Unloaded"/>.</summary>
        Started = 0,

        /// <summary>Only the server changes scenes; clients follow it.</summary>
        NotServer = 1,

        /// <summary>No session is running.</summary>
        NotRunning = 2,

        /// <summary>The NetworkManager has scene management turned off, so scenes are not synchronised.</summary>
        SceneManagementDisabled = 3,

        /// <summary>The scene is not in the build's scene list (File ▸ Build Profiles).</summary>
        UnknownScene = 4,

        /// <summary>Another scene change is still in progress; try again when it completes.</summary>
        Busy = 5,

        /// <summary>Unload: the scene is not loaded.</summary>
        NotLoaded = 6,

        /// <summary>The scene was refused by a verification callback.</summary>
        Refused = 7,

        /// <summary>NGO reported an internal error; see the log.</summary>
        Failed = 8,
    }

    /// <summary>One completed networked scene change, as every peer sees it.</summary>
    public sealed class SceneTransition
    {
        internal SceneTransition(string sceneName, NetSceneMode mode, IReadOnlyList<ulong> completed, IReadOnlyList<ulong> timedOut)
        {
            SceneName = sceneName;
            Mode = mode;
            Completed = completed;
            TimedOut = timedOut;
        }

        /// <summary>The scene loaded or unloaded.</summary>
        public string SceneName { get; }

        /// <summary>Single or additive (loads only; unloads are always additive scenes).</summary>
        public NetSceneMode Mode { get; }

        /// <summary>The clients (the server's own id included) that finished the change.</summary>
        public IReadOnlyList<ulong> Completed { get; }

        /// <summary>The clients that did not finish in time (NGO's LoadSceneTimeOut). They are still connected.</summary>
        public IReadOnlyList<ulong> TimedOut { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return SceneName + " (" + Mode + "): " + Completed.Count + " completed, " + TimedOut.Count + " timed out";
        }
    }

    /// <summary>
    /// Server-driven scene flow: the server loads and unloads scenes and every client follows, including clients that join
    /// later (they are synchronised to the server's scenes as they connect). A wrapper over NGO's scene management, so
    /// game code never touches NetworkSceneManager (BIBLE UM-LAW-2). Get it from <see cref="NetSession.Scenes"/>.
    /// </summary>
    /// <remarks>
    /// <para>Needs scene management on the NetworkManager (its default) and every scene in the build's scene list. Scene
    /// objects with a NetworkObject are spawned by the server and matched up on clients.</para>
    /// <para>One change at a time: a request while another is in progress returns <see cref="SceneRequestResult.Busy"/>.
    /// Events are raised on Unity's main thread; a handler's exception is logged and does not stop the flow.</para>
    /// </remarks>
    public sealed class NetSceneFlow
    {
        private static readonly ulong[] NoClients = new ulong[0];

        private readonly NetworkManager _manager;
        private readonly Dictionary<string, Scene> _serverScenes = new Dictionary<string, Scene>(StringComparer.Ordinal);
        private NetworkSceneManager _attached;

        internal NetSceneFlow(NetworkManager manager)
        {
            _manager = manager;
        }

        /// <summary>Every peer: the server began loading a scene (raised on clients when they are told to follow).</summary>
        public event Action<string, NetSceneMode> LoadStarted;

        /// <summary>Server side: one client (the server's own id included) finished loading a scene.</summary>
        public event Action<ulong, string> ClientLoaded;

        /// <summary>Every peer: a scene load finished on every client (or timed out on some).</summary>
        public event Action<SceneTransition> Loaded;

        /// <summary>Every peer: an additive scene finished unloading everywhere.</summary>
        public event Action<SceneTransition> Unloaded;

        /// <summary>Server side: a client that joined mid-session finished loading the server's current scenes.</summary>
        public event Action<ulong> ClientSynchronized;

        /// <summary>True when the NetworkManager synchronises scenes (scene management on).</summary>
        public bool IsEnabled
        {
            get { return _manager.NetworkConfig != null && _manager.NetworkConfig.EnableSceneManagement; }
        }

        /// <summary>Server side: the scene a change is in progress for, or null when none is.</summary>
        public string PendingScene { get; private set; }

        /// <summary>Server side: true while a load or unload is in progress.</summary>
        public bool IsBusy
        {
            get { return PendingScene != null; }
        }

        /// <summary>
        /// Server only: loads <paramref name="sceneName"/> on the server and every client. <see cref="NetSceneMode.Single"/>
        /// replaces the open scenes (objects that should survive must be DontDestroyOnLoad, as player objects are).
        /// </summary>
        /// <param name="sceneName">The scene's name or path, as in the build's scene list.</param>
        /// <param name="mode">Single (default) or additive.</param>
        /// <exception cref="ArgumentException"><paramref name="sceneName"/> is null or blank.</exception>
        public SceneRequestResult Load(string sceneName, NetSceneMode mode = NetSceneMode.Single)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("A scene name is required.", nameof(sceneName));
            }

            SceneRequestResult ready = CheckReady();
            if (ready != SceneRequestResult.Started)
            {
                return ready;
            }

            if (SceneUtility.GetBuildIndexByScenePath(sceneName) < 0)
            {
                return SceneRequestResult.UnknownScene;
            }

            SceneEventProgressStatus status = _manager.SceneManager.LoadScene(
                sceneName, mode == NetSceneMode.Additive ? LoadSceneMode.Additive : LoadSceneMode.Single);
            SceneRequestResult result = Map(status);
            if (result == SceneRequestResult.Started)
            {
                PendingScene = sceneName;
            }

            return result;
        }

        /// <summary>Server only: unloads an additively loaded scene on the server and every client.</summary>
        /// <param name="sceneName">The scene's name, as it was loaded.</param>
        /// <exception cref="ArgumentException"><paramref name="sceneName"/> is null or blank.</exception>
        public SceneRequestResult Unload(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("A scene name is required.", nameof(sceneName));
            }

            SceneRequestResult ready = CheckReady();
            if (ready != SceneRequestResult.Started)
            {
                return ready;
            }

            Scene scene;
            if (!_serverScenes.TryGetValue(sceneName, out scene) || !scene.IsValid() || !scene.isLoaded)
            {
                scene = SceneManager.GetSceneByName(sceneName);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                return SceneRequestResult.NotLoaded;
            }

            SceneRequestResult result = Map(_manager.SceneManager.UnloadScene(scene));
            if (result == SceneRequestResult.Started)
            {
                PendingScene = sceneName;
            }

            return result;
        }

        /// <summary>Starts listening to the NetworkManager's scene events (a session started).</summary>
        internal void Attach()
        {
            Detach();
            if (_manager.SceneManager != null && IsEnabled)
            {
                _attached = _manager.SceneManager;
                _attached.OnSceneEvent += OnSceneEvent;
            }
        }

        /// <summary>Stops listening and forgets in-progress state (the session ended).</summary>
        internal void Detach()
        {
            if (_attached != null)
            {
                _attached.OnSceneEvent -= OnSceneEvent;
                _attached = null;
            }

            PendingScene = null;
            _serverScenes.Clear();
        }

        private SceneRequestResult CheckReady()
        {
            if (!_manager.IsListening)
            {
                return SceneRequestResult.NotRunning;
            }

            if (!_manager.IsServer)
            {
                return SceneRequestResult.NotServer;
            }

            if (!IsEnabled || _manager.SceneManager == null)
            {
                return SceneRequestResult.SceneManagementDisabled;
            }

            return IsBusy ? SceneRequestResult.Busy : SceneRequestResult.Started;
        }

        private static SceneRequestResult Map(SceneEventProgressStatus status)
        {
            switch (status)
            {
                case SceneEventProgressStatus.Started:
                    return SceneRequestResult.Started;
                case SceneEventProgressStatus.SceneNotLoaded:
                    return SceneRequestResult.NotLoaded;
                case SceneEventProgressStatus.SceneEventInProgress:
                    return SceneRequestResult.Busy;
                case SceneEventProgressStatus.InvalidSceneName:
                    return SceneRequestResult.UnknownScene;
                case SceneEventProgressStatus.SceneFailedVerification:
                    return SceneRequestResult.Refused;
                case SceneEventProgressStatus.SceneManagementNotEnabled:
                    return SceneRequestResult.SceneManagementDisabled;
                case SceneEventProgressStatus.ServerOnlyAction:
                case SceneEventProgressStatus.SessionOwnerOnlyAction:
                    return SceneRequestResult.NotServer;
                default:
                    return SceneRequestResult.Failed;
            }
        }

        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            NetSceneMode mode = sceneEvent.LoadSceneMode == LoadSceneMode.Additive ? NetSceneMode.Additive : NetSceneMode.Single;
            bool local = sceneEvent.ClientId == _manager.LocalClientId;
            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.Load:
                    if (local)
                    {
                        Raise(LoadStarted, sceneEvent.SceneName, mode);
                    }

                    break;

                case SceneEventType.LoadComplete:
                    if (_manager.IsServer)
                    {
                        if (local && sceneEvent.Scene.IsValid())
                        {
                            _serverScenes[sceneEvent.SceneName] = sceneEvent.Scene;
                        }

                        Raise(ClientLoaded, sceneEvent.ClientId, sceneEvent.SceneName);
                    }

                    break;

                case SceneEventType.LoadEventCompleted:
                    if (_manager.IsServer)
                    {
                        PendingScene = null; // one change at a time, so this is the one that was pending
                    }

                    Raise(Loaded, Transition(sceneEvent, mode));
                    break;

                case SceneEventType.UnloadEventCompleted:
                    if (_manager.IsServer)
                    {
                        PendingScene = null;
                        _serverScenes.Remove(sceneEvent.SceneName);
                    }

                    Raise(Unloaded, Transition(sceneEvent, NetSceneMode.Additive));
                    break;

                case SceneEventType.SynchronizeComplete:
                    if (_manager.IsServer && sceneEvent.ClientId != NetworkManager.ServerClientId)
                    {
                        Raise(ClientSynchronized, sceneEvent.ClientId);
                    }

                    break;
            }
        }

        private static SceneTransition Transition(SceneEvent sceneEvent, NetSceneMode mode)
        {
            return new SceneTransition(
                sceneEvent.SceneName,
                mode,
                sceneEvent.ClientsThatCompleted != null ? sceneEvent.ClientsThatCompleted.ToArray() : NoClients,
                sceneEvent.ClientsThatTimedOut != null ? sceneEvent.ClientsThatTimedOut.ToArray() : NoClients);
        }

        private static void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(value);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void Raise<T1, T2>(Action<T1, T2> handler, T1 a, T2 b)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(a, b);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
