# Testing

A networked game is only as good as its tests over a real connection, so the package ships a **test kit** — the
`Unity.Multiplayer.Testing` assembly — that every game uses for its own PlayMode tests, and the package tests
itself with the same kit. This page starts with the kit (what a game needs), then covers the package's own suites.

## The test kit

One fixture gives a test a host (or a dedicated server) and any number of clients, each a real `NetSession` over a
real Unity Transport on loopback, on a port that is free on this machine. Waits fail with a message saying what
never happened, not a bare timeout.

### Set up

1. In `Packages/manifest.json`, list `com.unity.netcode.gameobjects` under `"testables"`. The fixture is built on
   NGO's `NetcodeIntegrationTest`, which only compiles when NGO is testable. You do **not** need to list
   `com.mindattic.unity.multiplayer` there, so the package's own suites stay out of your Test Runner.

   ```json
   "testables": [ "com.unity.netcode.gameobjects" ]
   ```

2. Reference the kit from your PlayMode test assembly. Its define constraint is `UNITY_EDITOR ||
   UNITY_INCLUDE_TESTS`: it compiles in the Editor and in test builds, and it (with NUnit and NGO's test helpers)
   never reaches a player build. It deliberately does not use `UNITY_INCLUDE_TESTS` alone, which Unity honours only
   for packages listed under `testables`; like Unity's own Performance Testing package, it is a test utility a game
   uses without making the package's own tests part of its Test Runner.

   ```json
   {
       "name": "MyGame.PlayMode.Tests",
       "references": [
           "MyGame",
           "Unity.Multiplayer",
           "Unity.Multiplayer.Core",
           "Unity.Multiplayer.Testing",
           "Unity.Netcode.Runtime",
           "Unity.Netcode.Runtime.Tests",
           "UnityEngine.TestRunner",
           "UnityEditor.TestRunner"
       ],
       "overrideReferences": true,
       "precompiledReferences": [ "nunit.framework.dll" ],
       "autoReferenced": false,
       "defineConstraints": [ "UNITY_INCLUDE_TESTS" ]
   }
   ```

### A first test

```csharp
using System.Collections;
using NUnit.Framework;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using UnityEngine;
using UnityEngine.TestTools;

public class ScoreBoardTests : NetSessionTest
{
    private GameObject _prefab;

    public ScoreBoardTests() : base(Topology.Host, clients: 2) { }

    protected override void OnSessionsCreated()
    {
        _prefab = CreatePrefab<ScoreBoard>();              // a network prefab, registered on every peer
        Clients[0].Identity = new PlayerIdentity("ann", "Ann", PlayerColor.White);
    }

    [UnityTest]
    public IEnumerator EveryClientSeesTheScore()
    {
        yield return StartAndJoinAll();                      // host up, both clients connected, all on Port

        ScoreBoard onServer = Spawn<ScoreBoard>(_prefab);    // spawned by the server
        var onAnn = WaitForCopy(Clients[0], onServer);       // Ann's copy of the same object
        yield return onAnn;

        onServer.AddPoint();
        yield return WaitForSynced(() => onAnn.Result.Score, 1, "Ann's score");
    }
}
```

The fixture makes fresh NetworkManagers, sessions and port for every test, and disposes them afterwards.

### What `NetSessionTest` gives you

| Member | What it does |
| --- | --- |
| `NetSessionTest(Topology topology, int clients = 1, TransportKind transport = Udp)` | `Topology.Host` (a listen server, which is also a player) or `Topology.Server` (dedicated), plus N clients. Use `[TestFixture(TransportKind.Udp)] [TestFixture(TransportKind.WebSocket)]` to run a suite over both transports. |
| `Server`, `Clients`, `Client`, `Sessions` | The `NetSession`s: the host or server, every client, the first client, all of them. |
| `Port`, `Transport`, `Config()` | The free port and transport for this test, and a fresh `NetConfig` carrying them (plus `GameVersion`). Override `Config()` to change every session's default. |
| `OnSessionsCreated()` | Your setup hook, before anything starts: identities, protocol hashes, prefabs, event handlers. |
| `OnSessionsTearDown()` | Your teardown hook, before the sessions are disposed. |
| `StartServer(config)`, `Join(client, config)`, `StartAndJoinAll(config)` | Start and connect, waiting until each is up; `StartAndJoinAll` also waits until the server sees every connection. |
| `CreatePrefab<T>(name)`, `RegisterPrefab(prefab)`, `SetPlayerPrefab(prefab)` | Network prefabs made in code or loaded from assets, registered with every session; a player prefab every connection gets. |
| `Spawn<T>(prefab, owner)` | Spawns an instance on the running server, owned by a client's session or by the server, and returns the server's component. |
| `WaitUntil(condition, failure)` | Waits for any condition. Pass the failure as `() => "..."` to report state at the moment it fails. |
| `WaitForSpawn<T>(peer, owner, match)` | Waits until a peer has a spawned `T` (optionally owned by a client, matching a predicate). `yield return` it, then read `.Result`. |
| `WaitForCopy<T>(peer, original)` | Waits until a peer has its copy of a spawned object. |
| `WaitForSynced<T>(read, expected, what)` | Waits until a `[Synced]` value as one peer sees it equals `expected`; fails with the last value seen. |
| `WaitFor(task)`, `WaitFor<T>(task)` | Waits for a `Task` (`JoinByCode`, `HostWithCode`, ...), rethrows what it threw, and hands back its result. |
| `StartRendezvous(ttlSeconds)` | An in-process rendezvous service for join-by-code tests, disposed with the test. |
| `TimeoutSeconds`, `SceneManagement`, `GameVersion` | Override to give a slow or lossy test more time (default 10 s), turn NGO scene management on, or change the default game version. |

`OnServerAndClientsCreated`, `OnTearDown`, `NumberOfClients` and `CanStartServerAndClients` are sealed: the fixture
owns the session lifecycle. Everything else of `NetcodeIntegrationTest` (`m_ServerNetworkManager`,
`m_ClientNetworkManagers`, ...) is still there for the odd NGO-level check.

### Pieces you can use on their own

- **`FreePort.Udp()`, `FreePort.Tcp()`, `FreePort.For(transport)`** — a port free right now (bind to 0, read it back,
  release it). Serve every test on one of these instead of a fixed port, so parallel runs on one machine (other
  worktrees, CI jobs, a local server) never collide. For NetworkManagers the NGO harness starts by itself,
  `FreePort.Assign(m_NetworkManagers)` points all their transports at one and returns it.
- **`TestSessions.Create(networkManager)`** — a `NetSession` over a NetworkManager your test made. A game gets its
  session from `NetBootstrap.Session`; the constructor is internal so the runtime API never takes an NGO type
  ([UM-LAW-2](https://github.com/mindattic/Unity.Multiplayer/blob/main/docs/BIBLE.md#um-law-2--nothing-above-the-ngo-adapter-imports-unitynetcode)), and this assembly, which never ships in a player, is the one public way to
  build one. `TestSessions.ManagerOf(session)` goes the other way.
- **`NetAwait`** — the waits, as statics with an explicit timeout: `Until`, `ForSpawn`, `ForCopy`, `ForSynced`,
  `ForTask`, `FindSpawned`, and `UntilAsync`, `ForSpawnAsync`, `ForSyncedAsync` for test code written with
  `async`/`await`. `Awaiting<T>` is what the value-returning waits hand back.
- **`InProcessRendezvousServer`** — the rendezvous service's HTTP API served from the test process on a loopback
  port: the real client, heartbeats and join-by-code without Docker. `ForServer(config)` points a server at it (URL
  and secret; a dedicated server registers itself once running), `ForClient(config)` points a client at it;
  `Find(code)`, `Count`, `Heartbeats` and `ForgetAll()` (a restarted service) let a test see and disturb it.

### Join by code in a test

```csharp
public class JoinByCodeTests : NetSessionTest
{
    private InProcessRendezvousServer _rendezvous;

    public JoinByCodeTests() : base(Topology.Server, clients: 1) { }

    protected override void OnSessionsCreated() => _rendezvous = StartRendezvous();

    [UnityTest]
    public IEnumerator ClientJoinsByTheServersCode()
    {
        yield return StartServer(_rendezvous.ForServer(Config()));
        yield return WaitUntil(() => !Server.PublishedCode.IsEmpty, "the server never registered");

        var joined = WaitFor(Client.JoinByCode(Server.PublishedCodeText, _rendezvous.ForClient()));
        yield return joined;
        Assert.That(joined.Result, Is.True);
        yield return WaitUntil(() => Client.State == SessionState.Connected, "the client never connected");
    }
}
```

### Testing your front end

A game that uses the [drop-in front end](front-end.md) tests its menus with `Unity.Multiplayer.UI.Testing` (add it to
the test assembly's references beside `Unity.Multiplayer.Testing`; it compiles only when MindAttic Components is
installed). A batch-mode run is headless, so `NetFrontEnd` builds no menus there unless the test asks for them:

```csharp
[SetUp]    public void SetUp() => FrontEndTesting.ForceMenus = true;   // before the front end's Start
[TearDown] public void TearDown() { FrontEndTesting.ForceMenus = null; FrontEndTesting.CleanUpUi(); }

frontEnd.Identity = new FixedIdentitySource("Ann", Color.red);     // never touches PlayerPrefs or the real profile
FrontEndTesting.Click(FrontEndTesting.MainScreen(frontEnd), "Host");
yield return WaitUntil(() => frontEnd.State == FrontEndState.Lobby, "never reached the lobby");
string code = FrontEndTesting.LobbyCode(frontEnd).Code;
```

`FrontEndTesting` also has `Bind(frontEnd, session, config)` (drive a `TestSessions.Create` session instead of a
NetBootstrap's), `LobbyScreen`, `LobbyRoster`, `BusyDialog`, `MessageDialog`, `Buttons(root)` and `HasButton`. A front
end beside a real `NetBootstrap` needs no `Bind`: switch the bootstrap off before its Start (so a headless editor does
not serve by itself) and the front end still drives its session.

### A bad connection

Give the clients `NetConfig.Conditions` (latency, jitter, loss through Unity Transport's simulator) in the config you
join with, and raise `TimeoutSeconds`; `LossyNetworkTests` in the package does exactly that, over the
[lossy profile](topologies.md#testing-a-bad-connection).

### Bots: a server and N bots in one test

`NetBotTest` is a `NetSessionTest` with a dedicated server and one client per bot. Each bot is a `NetBotRunner` that
joins by the join code the server writes to a file, through an in-process rendezvous service, and then hands its
session to your `IBotDriver`. `RunBots(role => new MyBot(), "A", "B")` fails the test unless every bot passes. This is
the nightly compose smoke without Docker; see [Bots and end-to-end smoke tests](bots.md#in-process-tests-netbottest).

### Examples to copy

- A game made with **MindAttic ▸ Multiplayer ▸ New Game…** starts with both kinds. `Tests/PlayMode/GameTests.cs` has
  `PlayerTests` (a `[ToServer]` round trip, roster name and colour, server-side clamping), `CrateTests` (objects by
  key, a restart vote through `VoteBoard`) and `BotTests` (`NetBotTest`). `Tests/EditMode/GameSetupTests.cs` checks the
  scene's kit components and runs the prefab validator. Its asmdefs are the ones above. `tests/run-new-game.ps1` runs
  them in a brand-new project.
- `tests/MinimalSample/Tests` — the Minimal sample's player prefab tested as a game would: `SetPlayerPrefab`,
  `StartAndJoinAll`, `WaitForSpawn` for each peer's player, `WaitUntil` on synced positions and RPCs.
- The package's own suites under `unity/com.mindattic.unity.multiplayer/Tests/Runtime`: `HostAndClientTests` and
  `ServerAndClientsTests` (both topologies, both transports), `EntityRpcTests`, `ApprovalAndSeatTests`,
  `JoinByCodeTests` (the rendezvous helper), `LossyNetworkTests`, `NetBotTests` (`NetBotTest`: two bots join by code
  and get an RPC answered), and `TestKitTests` (the kit's own guarantees).
- Unity.Cursory's PlayMode suite, the first game built on the package.

## The package's own suites

The package is tested at two levels: plain `dotnet test` for everything engine-free, and Unity PlayMode/EditMode
suites for everything that needs the engine or real networking. Both run in CI
(`.github/workflows/ci.yml`); a nightly job additionally runs a real two-client smoke test end to end.

### dotnet test

```bash
dotnet test tests/Core.Tests          # Unity.Multiplayer.Core: config, join codes, seats, approval, rpc gate, rendezvous client...
dotnet test tests/Rendezvous.Tests    # server/Rendezvous, hosted in-process with WebApplicationFactory
dotnet test tests/Generators.Tests    # the source generator and its analyzer diagnostics
dotnet test tests/Hosting.Cli.Tests   # mp-host: every provisioner/allocator call mocked
```

No Unity install is needed for any of these. `Unity.Multiplayer.Core` is engine-free by design (`UM-LAW-2`,
`noEngineReferences`): `tests/Core.Tests` compiles the package's own `Runtime/Core/**/*.cs` straight in
(`<Compile Include="..\..\unity\...\Runtime\Core\**\*.cs">`), so the code under test is exactly the code Unity
imports, not a copy. `tests/Core.Build` compiles the same sources as netstandard2.1 / C# 9 — the way Unity
actually compiles them — so a language feature Core.Tests' net10 TFM would silently allow still fails the way
it would in Unity.

#### Generators.Tests

Snapshot-tests the generator's output: the package's own `TestAvatar` and one test per member shape (every RPC
direction, every `[Synced]` type), each compiled clean as C# 9. One test per diagnostic in
[Attributes reference](attributes.md#generator-diagnostics) asserts it fires for the broken input it's meant to
catch. After an intentional change to the generator's output shape:

```bash
UPDATE_SNAPSHOTS=1 dotnet test tests/Generators.Tests
```

rewrites `Snapshots/` — review the diff before committing it, since it's the thing the test is meant to pin.

#### Hosting.Cli.Tests

Every `mp-host` provider call (Hetzner, Vultr, DigitalOcean, AWS, GCE, Azure, Fly, RunPod, Edgegap, Gameye,
GameLift, ...) is mocked with NUnit, so this suite runs with no cloud credentials and touches no real API. Each
provider also has opt-in `[Explicit]` smoke tests that exercise the real API and always tear down what they
create — run those only with real credentials loaded (`mp-host credentials set-...`), never in CI. See
[Hosting](hosting.md#secrets) for where those credentials live.

### Unity suites

```powershell
powershell -File tests/run-playmode.ps1     # PlayMode: the test kit's fixture over real Unity Transport, loopback
powershell -File tests/run-editmode.ps1     # EditMode: editor tooling, one real Linux server build (-SkipBuild to leave it out)
powershell -File tests/run-sample.ps1       # imports the Minimal sample through Package Manager, runs its PlayMode tests
powershell -File tests/run-sample.ps1 -Sample FrontEnd   # the same for the Front End sample
powershell -File tests/run-smoke.ps1        # builds the nightly smoke test's Linux player
```

These need Unity 6000.4.3f1 installed locally (`tests/PlayModeHost` is the bare project both suites run
against). The host also references MindAttic Components and Unity.Shared by `file:` path, as sibling repos of this one,
so the optional front end compiles and its PlayMode suite (`Unity.Multiplayer.UI.Tests`) runs beside the main one;
`tests/prepare-host.ps1`, called by both scripts, imports TextMesh Pro's Essential Resources on a clean checkout first. PlayMode suites cover what only means something with real networking: hosting and joining over UDP
and WebSocket, reconnect taking back the same seat, a killed client's restart taking over a stale connection,
scene flow and late joiners, every RPC direction and rate limiting, join codes against the in-process rendezvous
service, the whole flow again under [simulated latency and loss](topologies.md#testing-a-bad-connection)
(`LossyNetworkTests`), and the [drop-in front end](front-end.md#where-it-is-tested) driven through its menus.
Every suite serves on a free port, so several runs can share a machine. EditMode covers
the setup wizard, the prefab validator and the dedicated-server build — including a real Mono Linux build inside
the suite, skipped only in CI (GameCI's editor image has no Linux Dedicated Server build-support module) and
still run by `run-editmode.ps1` locally, per HOUSE-LAW-8 (done is verified, not asserted).

`run-sample.ps1` builds a throwaway host project (`tests/SampleHost`, git-ignored), imports the Minimal sample
through `UnityEditor.PackageManager.UI.Sample` the same way a game developer would from the Package Manager
window, and runs `tests/MinimalSample/Tests` against the imported copy — so the sample that ships is the sample
that's tested, not a copy kept in sync by hand. Its manifest lists only NGO as testable, as a game's does, so it
also proves the test kit compiles for a consumer. `-Author` regenerates its scene, prefab and prefab list.

### The nightly smoke test

`.github/workflows/nightly-smoke.yml` builds one Linux player (`tests/MinimalSample/Editor/SmokeHostBuild.cs`)
that plays every role — rendezvous service, dedicated server, or bot client — picked at run time by the
`MINDATTIC_ROLE` environment variable (`tests/MinimalSample/Runtime/SmokeDriver.cs`), and runs all three under
Docker Compose (`smoke/docker-compose.yml`). The server registers with rendezvous and publishes a join code to a
shared volume; two bot clients read it, join through the rendezvous service, and drive their squares into the
arena wall. `smoke/wait-for-smoke.sh` watches for both bots' `OK:` log lines. This is the one test that proves
the join-code flow and an RPC round trip end to end, against the actual built player, not a PlayMode stand-in.
Run it locally with `powershell -File tests/run-smoke.ps1`. A game gets the same smoke with no code of its own from
`mp-host game smoke` and the reusable `game-smoke.yml` workflow ([Bots](bots.md)).

### CI

`.github/workflows/ci.yml` runs the four `dotnet test` suites above as a matrix job, plus GameCI's
`unity-test-runner` for EditMode and PlayMode (`-runTests -batchmode`, filtered to one test assembly per mode).
Both Unity jobs need a `UNITY_LICENSE` repository secret. `.github/workflows/docs.yml` (this site) and
`.github/workflows/nightly-smoke.yml` are separate workflows — see [Hosting](hosting.md) for what the smoke test
proves about a real deploy, and the repository root `README.md` for the exact commands.
