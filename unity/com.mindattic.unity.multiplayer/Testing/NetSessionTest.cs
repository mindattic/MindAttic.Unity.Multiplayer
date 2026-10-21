using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;

namespace Unity.Multiplayer.Testing
{
    /// <summary>
    /// A PlayMode fixture for a networked game's tests: a host (or a dedicated server) and N clients, each a
    /// <see cref="NetSession"/> over its own NetworkManager, talking over a real Unity Transport on loopback, on a port
    /// free on this machine. Built on NGO's <c>NetcodeIntegrationTest</c>, which creates and destroys the
    /// NetworkManagers; the sessions start and stop them.
    /// </summary>
    /// <remarks>
    /// <para>Fresh managers, sessions and port for every test. Set the sessions up (identities, prefabs) in
    /// <see cref="OnSessionsCreated"/>, then in the test <c>yield return</c> <see cref="StartAndJoinAll"/> (or
    /// <see cref="StartServer"/> and <see cref="Join"/>) and the waits (<see cref="WaitUntil(Func{bool}, string, float?)"/>,
    /// <see cref="WaitForSpawn{T}"/>, <see cref="WaitForCopy{T}"/>, <see cref="WaitForSynced{T}"/>, <see cref="WaitFor(Task, string)"/>),
    /// each of which fails the test with a message saying what never happened.</para>
    /// <para>The fixture owns the session lifecycle: it creates the sessions in <c>OnServerAndClientsCreated</c> and
    /// disposes them (and any <see cref="StartRendezvous"/> service) in <c>OnTearDown</c>, both sealed; override
    /// <see cref="OnSessionsCreated"/> and <see cref="OnSessionsTearDown"/> instead.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public class ScoreTests : NetSessionTest
    /// {
    ///     private GameObject _prefab;
    ///
    ///     public ScoreTests() : base(Topology.Host, clients: 1) { }
    ///
    ///     protected override void OnSessionsCreated() =&gt; _prefab = CreatePrefab&lt;ScoreBoard&gt;("ScoreBoard");
    ///
    ///     [UnityTest]
    ///     public IEnumerator ClientSeesTheScore()
    ///     {
    ///         yield return StartAndJoinAll();
    ///         ScoreBoard onServer = Spawn&lt;ScoreBoard&gt;(_prefab);
    ///         var onClient = WaitForCopy(Client, onServer);
    ///         yield return onClient;
    ///         onServer.AddPoint();
    ///         yield return WaitForSynced(() =&gt; onClient.Result.Score, 1, "the client's score");
    ///     }
    /// }
    /// </code>
    /// </example>
    public abstract class NetSessionTest : NetcodeIntegrationTest
    {
        private readonly int _clientCount;
        private readonly List<IDisposable> _disposeAtTearDown = new List<IDisposable>();
        private NetSession _server;
        private NetSession[] _clients = new NetSession[0];

        /// <param name="topology"><see cref="Core.Topology.Host"/> (a listen server, which is also a player) or <see cref="Core.Topology.Server"/> (a dedicated server).</param>
        /// <param name="clients">How many client sessions to make besides the server; 0 or more.</param>
        /// <param name="transport">The transport every session uses; the free port is picked for it.</param>
        protected NetSessionTest(Topology topology, int clients = 1, TransportKind transport = TransportKind.Udp)
            : base(ToHarness(topology))
        {
            if (clients < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clients), "A fixture needs zero or more clients.");
            }

            Topology = topology;
            Transport = transport;
            _clientCount = clients;
        }

        /// <summary>Host or Server: what <see cref="StartServer"/> starts.</summary>
        protected Topology Topology { get; }

        /// <summary>The transport every session uses by default (<see cref="Config"/>).</summary>
        protected TransportKind Transport { get; }

        /// <summary>The port this test serves on: free on this machine when the test started.</summary>
        protected ushort Port { get; private set; }

        /// <summary>The host or dedicated server's session.</summary>
        protected NetSession Server
        {
            get { return _server; }
        }

        /// <summary>The client sessions, in order.</summary>
        protected IReadOnlyList<NetSession> Clients
        {
            get { return _clients; }
        }

        /// <summary>The first client's session.</summary>
        /// <exception cref="InvalidOperationException">The fixture has no clients.</exception>
        protected NetSession Client
        {
            get
            {
                if (_clients.Length == 0)
                {
                    throw new InvalidOperationException("This fixture was made with no clients.");
                }

                return _clients[0];
            }
        }

        /// <summary>The server and every client.</summary>
        protected IEnumerable<NetSession> Sessions
        {
            get
            {
                if (_server != null)
                {
                    yield return _server;
                }

                foreach (NetSession client in _clients)
                {
                    yield return client;
                }
            }
        }

        /// <summary>How long every wait gives up after. Raise it for a slow or lossy link.</summary>
        protected virtual float TimeoutSeconds
        {
            get { return NetAwait.DefaultTimeoutSeconds; }
        }

        /// <summary>
        /// Whether NGO's scene management is on. Off by default: the sessions start NGO themselves, so the harness never
        /// registers the test-runner scene with it, and connecting needs no scene sync. Turn it on to test scene flow.
        /// </summary>
        protected virtual bool SceneManagement
        {
            get { return false; }
        }

        /// <summary>The game version every session sends by default (<see cref="Config"/>).</summary>
        protected virtual string GameVersion
        {
            get { return "test"; }
        }

        /// <summary>How many clients the fixture was made with.</summary>
        protected sealed override int NumberOfClients
        {
            get { return _clientCount; }
        }

        /// <summary>The managers are always started by the sessions, never by the harness.</summary>
        protected sealed override bool CanStartServerAndClients()
        {
            return false;
        }

        /// <summary>
        /// A config for this test: <see cref="Port"/>, <see cref="Transport"/> and <see cref="GameVersion"/>. A new object
        /// every call, so a test can change it freely; override to change every session's default.
        /// </summary>
        protected virtual NetConfig Config()
        {
            return new NetConfig { Port = Port, Transport = Transport, GameVersion = GameVersion };
        }

        /// <summary>
        /// Called once the sessions exist and before anything starts: set identities and protocol hashes, register
        /// prefabs (<see cref="CreatePrefab{T}"/>, <see cref="RegisterPrefab"/>), choose a player prefab
        /// (<see cref="SetPlayerPrefab"/>), subscribe to events.
        /// </summary>
        protected virtual void OnSessionsCreated()
        {
        }

        /// <summary>Called at the end of each test, before the sessions are disposed.</summary>
        protected virtual IEnumerator OnSessionsTearDown()
        {
            yield break;
        }

        /// <inheritdoc />
        protected sealed override void OnServerAndClientsCreated()
        {
            Port = FreePort.For(Transport);
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.EnableSceneManagement = SceneManagement;
                FreePort.Assign(manager, Port);
            }

            _server = TestSessions.Create(m_ServerNetworkManager);
            _clients = new NetSession[m_ClientNetworkManagers.Length];
            for (int i = 0; i < _clients.Length; i++)
            {
                _clients[i] = TestSessions.Create(m_ClientNetworkManagers[i]);
            }

            OnSessionsCreated();
            base.OnServerAndClientsCreated();
        }

        /// <inheritdoc />
        protected sealed override IEnumerator OnTearDown()
        {
            try
            {
                yield return OnSessionsTearDown();
            }
            finally
            {
                foreach (NetSession client in _clients)
                {
                    client.Dispose();
                }

                if (_server != null)
                {
                    _server.Dispose();
                }

                foreach (IDisposable disposable in _disposeAtTearDown)
                {
                    disposable.Dispose();
                }

                _disposeAtTearDown.Clear();
                _clients = new NetSession[0];
                _server = null;
            }

            yield return null;
        }

        /// <summary>Starts <see cref="Server"/> as a host or dedicated server and waits until it is running.</summary>
        /// <param name="config">The config to start with; <see cref="Config"/> when null.</param>
        protected IEnumerator StartServer(NetConfig config = null)
        {
            NetConfig start = config ?? Config();
            bool started = Topology == Topology.Host ? _server.StartHost(start) : _server.StartServer(start);
            Assert.That(started, Is.True, (Topology == Topology.Host ? "StartHost" : "StartServer") + " refused to start: " + NetAwait.Describe(_server));
            yield return WaitUntil(() => _server.State == SessionState.Running, () => "the server never reached Running: " + NetAwait.Describe(_server));
        }

        /// <summary>Joins <paramref name="client"/> to the server on loopback and waits until it is connected.</summary>
        /// <param name="config">The config to join with; <see cref="Config"/> when null. Its port is the one dialled.</param>
        protected IEnumerator Join(NetSession client, NetConfig config = null)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            NetConfig join = config ?? Config();
            Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, join.Port, join), Is.True, "JoinByAddress refused to start: " + NetAwait.Describe(client));
            yield return WaitUntil(() => client.State == SessionState.Connected, () => "the client never connected: " + NetAwait.Describe(client));
        }

        /// <summary>
        /// Starts the server, joins every client and waits until the server sees them all (and, on a host, itself).
        /// </summary>
        /// <param name="config">The config every session uses; <see cref="Config"/> when null.</param>
        protected IEnumerator StartAndJoinAll(NetConfig config = null)
        {
            yield return StartServer(config);
            foreach (NetSession client in _clients)
            {
                yield return Join(client, config);
            }

            int expected = _clients.Length + (Topology == Topology.Host ? 1 : 0);
            yield return WaitUntil(
                () => _server.ConnectedClientIds.Count == expected,
                () => "the server never saw all " + expected + " connections (it sees " + _server.ConnectedClientIds.Count + ")");
        }

        /// <summary>
        /// A network prefab carrying a <typeparamref name="T"/>, made in code and registered with every session. Call it from
        /// <see cref="OnSessionsCreated"/>, before the sessions start.
        /// </summary>
        protected GameObject CreatePrefab<T>(string name = null) where T : Component
        {
            var prefab = new GameObject(name ?? typeof(T).Name);
            NetworkObject networkObject = prefab.AddComponent<NetworkObject>();
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(networkObject);
            prefab.AddComponent<T>();
            RegisterPrefab(prefab);
            return prefab;
        }

        /// <summary>
        /// Registers <paramref name="prefab"/> (a prefab asset, or a GameObject with a NetworkObject made into a test prefab)
        /// with every session. Call it before the sessions start.
        /// </summary>
        protected void RegisterPrefab(GameObject prefab)
        {
            foreach (NetSession session in Sessions)
            {
                session.Prefabs.Register(prefab);
            }
        }

        /// <summary>
        /// Makes <paramref name="prefab"/> the player object every connection gets, on every peer, and registers it.
        /// Call it before the sessions start.
        /// </summary>
        protected void SetPlayerPrefab(GameObject prefab)
        {
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.PlayerPrefab = prefab;
            }

            RegisterPrefab(prefab);
        }

        /// <summary>
        /// Spawns a new instance of <paramref name="prefab"/> on the running server, owned by <paramref name="owner"/>'s
        /// client (the server itself when null), and returns the server's <typeparamref name="T"/> on it.
        /// </summary>
        protected T Spawn<T>(GameObject prefab, NetSession owner = null) where T : Component
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            Assert.That(_server.IsServer, Is.True, "Spawn needs a running server: " + NetAwait.Describe(_server));
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null, prefab.name + " has no NetworkObject");

            // The harness's own spawn: it points the instance at the server's NetworkManager (several share this process).
            GameObject instance = SpawnObject(prefab, owner != null ? owner.Manager : m_ServerNetworkManager);
            T component = instance.GetComponentInChildren<T>(true);
            Assert.That(component, Is.Not.Null, prefab.name + " has no " + typeof(T).Name);
            return component;
        }

        /// <summary>
        /// Starts an in-process rendezvous service for join-by-code tests; it is disposed when the test ends. Point the
        /// server at it with <see cref="InProcessRendezvousServer.ForServer"/>, clients with <see cref="InProcessRendezvousServer.ForClient"/>.
        /// </summary>
        protected InProcessRendezvousServer StartRendezvous(int ttlSeconds = 60, string secret = null)
        {
            var rendezvous = new InProcessRendezvousServer(ttlSeconds, secret);
            _disposeAtTearDown.Add(rendezvous);
            return rendezvous;
        }

        /// <summary>Disposes <paramref name="disposable"/> when the test ends, after the sessions.</summary>
        protected T DisposeAtTearDown<T>(T disposable) where T : IDisposable
        {
            _disposeAtTearDown.Add(disposable);
            return disposable;
        }

        /// <summary>Waits until <paramref name="condition"/> holds; fails with <paramref name="failure"/> after the timeout.</summary>
        /// <param name="timeoutSeconds">Overrides <see cref="TimeoutSeconds"/> for this wait.</param>
        protected IEnumerator WaitUntil(Func<bool> condition, string failure, float? timeoutSeconds = null)
        {
            return NetAwait.Until(condition, failure, timeoutSeconds ?? TimeoutSeconds);
        }

        /// <summary>Waits until <paramref name="condition"/> holds; on a timeout fails with the message <paramref name="failure"/> builds then.</summary>
        /// <param name="timeoutSeconds">Overrides <see cref="TimeoutSeconds"/> for this wait.</param>
        protected IEnumerator WaitUntil(Func<bool> condition, Func<string> failure, float? timeoutSeconds = null)
        {
            return NetAwait.Until(condition, failure, timeoutSeconds ?? TimeoutSeconds);
        }

        /// <summary>
        /// Waits until <paramref name="peer"/> has a spawned <typeparamref name="T"/>, optionally owned by
        /// <paramref name="owner"/> and matching <paramref name="match"/>; <c>yield return</c> it, then read its <c>Result</c>.
        /// </summary>
        protected Awaiting<T> WaitForSpawn<T>(NetSession peer, ulong? owner = null, Func<T, bool> match = null) where T : Component
        {
            return NetAwait.ForSpawn(peer, owner, match, TimeoutSeconds);
        }

        /// <summary>Waits until <paramref name="peer"/> has its own copy of <paramref name="original"/>; <c>yield return</c> it, then read its <c>Result</c>.</summary>
        protected Awaiting<T> WaitForCopy<T>(NetSession peer, T original) where T : Component
        {
            return NetAwait.ForCopy(peer, original, TimeoutSeconds);
        }

        /// <summary>Waits until <paramref name="read"/> equals <paramref name="expected"/>; fails naming <paramref name="what"/> and the last value seen.</summary>
        protected IEnumerator WaitForSynced<T>(Func<T> read, T expected, string what)
        {
            return NetAwait.ForSynced(read, expected, what, TimeoutSeconds);
        }

        /// <summary>Waits for <paramref name="task"/>; rethrows what it threw.</summary>
        protected IEnumerator WaitFor(Task task, string what = null)
        {
            return NetAwait.ForTask(task, what, TimeoutSeconds);
        }

        /// <summary>Waits for <paramref name="task"/>; <c>yield return</c> it, then read its <c>Result</c>.</summary>
        protected Awaiting<T> WaitFor<T>(Task<T> task, string what = null)
        {
            return NetAwait.ForTask(task, what, TimeoutSeconds);
        }

        private static HostOrServer ToHarness(Topology topology)
        {
            switch (topology)
            {
                case Topology.Host:
                    return HostOrServer.Host;
                case Topology.Server:
                    return HostOrServer.Server;
                default:
                    throw new ArgumentException("A NetSessionTest is run by a Host or a Server, not " + topology + ".", nameof(topology));
            }
        }
    }
}
