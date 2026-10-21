# Unity.Multiplayer
Reusable Unity 6 multiplayer for MindAttic games: attribute RPCs, host or dedicated server, join by IP or code.

> **Status: early.** A game can host, serve and join by IP:port through `NetSession` / `NetBootstrap`, with
> approval, seats, `NetEntity` RPCs in every direction, synced fields and server-driven scene changes. The RPC
> and synced-field plumbing is written by the package's source generator. The self-hosted rendezvous service
> (`server/Rendezvous`) is built, and a game hosts and joins by join code (`NetSession.HostWithCode` /
> `JoinByCode`; Unity Relay for listen servers, the rendezvous service for dedicated servers). The design and build order are in
> [docs/rfc/0001-architecture.md](docs/rfc/0001-architecture.md); the rules are in
> [docs/BIBLE.md](docs/BIBLE.md). This README is rewritten to README-STYLE, with a link to the
> docs site, in UM-T19.

## Use it from a Unity project
Add the package to the game's `Packages/manifest.json` as a local package:

```json
"com.mindattic.unity.multiplayer": "file:../../MindAttic.Unity.Multiplayer/unity/com.mindattic.unity.multiplayer"
```

It pulls in Netcode for GameObjects 2.x and Unity Transport 2.x. Install
`com.unity.services.multiplayer` as well to enable Unity Relay join codes.

Then use **MindAttic > Multiplayer > Set Up Scene**. It adds a NetworkManager with Unity Transport and a `NetBootstrap`
to the open scene, and assigns a `NetPrefabList`: the project's own list, or a new `Assets/NetPrefabs.asset` filled
with the project's prefabs that have a NetworkObject. You can run it again; it adds only what is missing.
**MindAttic > Multiplayer > Validate Prefabs** checks every network prefab under Assets. Each needs a NetworkObject on
its root and a prefab list that registers it, and no `NetEntity` on it may be generic or derive from a generic class.

## Try the Minimal sample
In **Window > Package Manager**, select MindAttic Multiplayer, open **Samples** and import **Minimal**. Open its
`Minimal.unity`, press Play and click **Host**; a second copy (Multiplayer Play Mode or a build) clicks **Join by IP**
or **Join by code**. Each player's square is moved by the server through one `[ToServer]` call, one `[ToClients]` call
and one `[Synced]` field. The sample's
[README](unity/com.mindattic.unity.multiplayer/Samples~/Minimal/README.md) is the 10-minute walkthrough of every line.

## Layout
| Path | What |
|---|---|
| `unity/com.mindattic.unity.multiplayer/` | The Unity package (`package.json`, Runtime, Editor, Tests); `Runtime/Generators/` holds the built generator DLL |
| `unity/com.mindattic.unity.multiplayer/Samples~/Minimal/` | The Minimal sample Package Manager imports (scene, player prefab, menu, line-by-line README) |
| `src/Generators/` | The source generator, analyzers and suppressor (netstandard2.0, Roslyn 4.3.1) |
| `server/Rendezvous/` | The rendezvous service (ASP.NET Core .NET 10 minimal API + Dockerfile): join code to a dedicated server's IP:port |
| `tests/Rendezvous.Tests/` | NUnit tests that host the service in-process with `WebApplicationFactory` |
| `tests/Generators.Tests/` | Snapshot and diagnostic tests for the generator |
| `tests/Core.Tests/` | NUnit tests for the engine-free Core, compiled straight from `Runtime/Core` |
| `tests/Core.Build/` | Compiles `Runtime/Core` as netstandard2.1 / C# 9, the way Unity does |
| `tests/MinimalSample/` | The batch-mode importer and PlayMode tests `run-sample.ps1` runs against the imported Minimal sample; its `Runtime/SmokeDriver.cs` and `Editor/SmokeHostBuild.cs` build the nightly smoke test's Linux player |
| `tests/PlayModeHost/` | A bare Unity 6000.4.3f1 project that lists the package in `testables`, for the PlayMode and EditMode suites |
| `smoke/` | The nightly Docker Compose smoke test: the rendezvous service, a dedicated server and two headless bot clients joining by code (`.github/workflows/nightly-smoke.yml`, `tests/run-smoke.ps1` locally) |
| `docs/` | BIBLE, RFCs, user stories |

## Run a dedicated server
Add a `NetBootstrap` to the NetworkManager's GameObject. A headless launch serves by itself:

```
Game.x86_64 -batchmode -nographics -port 7777 -transport udp
```

`-mode host|server|client`, `-address`, `-tickrate`, `-maxplayers`, `-name` and `-password` are read too.

**MindAttic > Multiplayer > Build Dedicated Server (Linux)** makes that build. You need the editor's *Linux Dedicated
Server Build Support* module. It validates the prefabs first and stops on errors. Then it builds the enabled Build
Settings scenes as a headless Linux x86_64 Dedicated Server into `Builds/LinuxServer/`, and puts a `Dockerfile` and
`.dockerignore` beside the build. The Dockerfile runs the server as a non-root user on the open scene's
`NetBootstrap` port and transport:

```
cd Builds/LinuxServer
docker build -t game-server .
docker run --rm -p 7777:7777/udp -e MINDATTIC_RENDEZVOUS_SECRET=<secret> game-server -rendezvous https://rendezvous.example.com
```

Arguments after the image name are added to the server's command line. CI builds the same way with
`Unity -batchmode -quit -projectPath <game> -executeMethod Unity.Multiplayer.Editor.DedicatedServerBuild.BuildFromCommandLine`
and the optional `-serverOutput <dir> -port <n> -transport udp|websocket -serverBackend mono|il2cpp -serverDevelopment`.

## Build a server package for a host
**MindAttic > Multiplayer > Build Server Package > GenericVps (default)** or **> RunPod** builds the same Linux server
plus everything a host needs, into `Builds/ServerPackage/<profile>/`. The hosting provider is still undecided, so every
host gets the same package and only the profile's extra files differ:

| In the package | What |
|---|---|
| `<Game>.x86_64`, `<Game>_Data/`, `UnityPlayer.so` | The self-contained Linux x86_64 Dedicated Server build |
| `server.json` | The settings (`name`, `password`, `port`, `maxPlayers`, `transport`, `healthPort`, `adminCommands`, `logsDirectory`, `savesDirectory`, ...) |
| `start.sh` | Starts the server; arguments override `server.json`: `./start.sh -port 7778 -maxPlayers 4 -name "Friday game" -password hunter2` |
| `server-package.json` | The manifest a deploy tool reads: profile, executable, declared ports, health path, stop signal |
| `Logs/`, `Saves/` | One log file per run (the newest 10 kept), and the game's persistent data (`ServerHost.Current.Paths.SavesDirectory`) |
| GenericVps: `Dockerfile`, `docker-compose.yml`, `HOSTING.txt` | The server image plus the rendezvous service on any UDP-capable VPS |
| RunPod: `Dockerfile`, `HOSTING.txt` | WebSocket transport on a mapped TCP port (RunPod passes TCP/HTTP only) |

Settings layer as `server.json`, then the environment (`MINDATTIC_SERVER_PASSWORD`, `MINDATTIC_RENDEZVOUS_URL` / `_SECRET`,
`MINDATTIC_PUBLIC_ADDRESS`), then the command line. Bad settings stop the server with exit code 1. The running server
answers `GET /healthz` on its health port (default 8081: 200 while running, 503 while starting or stopping). With
`adminCommands` on it reads `help`, `status`, `players`, `kick <clientId> [reason]` and `stop` from standard input. On
SIGTERM or SIGINT it shuts down cleanly: `ServerHost.ShuttingDown` fires (save there), the session closes, and the
process exits. A server password is checked at connection approval; clients set `NetBootstrap.Password` or
`NetConfig.Password`. CI builds a package with
`-executeMethod Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine -serverProfile GenericVps`.
More profiles (G-Portal's Private Server program, Nitrado, Edgegap, GameLift) can be added later as `IHostingProfile`s
without changing games.

## Test on a bad connection
`NetConfig.Conditions` (or the Net Bootstrap's *Network conditions* fields, or `-netsim`) makes this peer's link
slow and lossy: `Broadband` (20 ms), `Mobile4G` (60 ms, 1% loss), `Mobile3G` (200 ms, 3% loss), `Lossy` (150 ms,
5% loss) or `Custom` (`-latency 150 -jitter 10 -loss 5`). Each packet the peer sends and receives is delayed and may
be dropped by Unity Transport's simulator stage; reliable messages are resent, unreliable ones are lost.
`NetSession.SetConditions` changes it while the game runs. It works in the editor and development builds only.

## Run the rendezvous service
A dedicated server registers its public IP:port and gets a join code; players resolve the code to connect.
The routes are in Core's `RendezvousProtocol`. Build the image from the repository root:

```
docker build -f server/Rendezvous/Dockerfile -t mindattic-rendezvous .
docker run -p 8080:8080 -e Rendezvous__RegistrationSecret=<secret> mindattic-rendezvous
```

It will not start without a registration secret (`Rendezvous__AllowOpenRegistration=true` is for local
development). A dedicated server registers itself when it has a URL and the secret:
`-rendezvous https://rendezvous.example.com` (or `MINDATTIC_RENDEZVOUS_URL`) plus `MINDATTIC_RENDEZVOUS_SECRET`;
`-publicaddress` / `-publicport` override what it advertises, and on RunPod it advertises its `RUNPOD_TCP_PORT_*`
mapping (with `-transport websocket`). Codes live 60 s past the last heartbeat (`Rendezvous__TtlSeconds`), every route is rate limited
per IP (`Rendezvous__RateLimits__*`), and `GET /healthz` drives the container's health check. For TLS, put it
behind a reverse proxy (`Rendezvous__UseForwardedHeaders=true`, `Rendezvous__KnownProxies__0=<proxy IP>`) or give
Kestrel a certificate; the Dockerfile header lists the variables. Locally: `dotnet run --project server/Rendezvous`
(Development allows open registration).

## Test
```
dotnet test tests/Core.Tests
dotnet test tests/Rendezvous.Tests
dotnet test tests/Generators.Tests          # UPDATE_SNAPSHOTS=1 rewrites Snapshots/ after an intended change
powershell -File tests/run-playmode.ps1     # PlayMode suite in Unity 6000.4.3f1 batch mode
powershell -File tests/run-editmode.ps1     # EditMode suite (editor tooling, one real Linux server build; -SkipBuild leaves it out)
powershell -File tests/run-sample.ps1       # imports the Minimal sample through Package Manager and runs its PlayMode tests (-Author rebuilds its scene and prefabs)
powershell -File tests/run-smoke.ps1        # builds the nightly smoke test's Linux player (server + 2 bots, same build, role picked by MINDATTIC_ROLE)
```

CI (`.github/workflows/ci.yml`) runs the three `dotnet test` suites and the PlayMode/EditMode suites through
GameCI's `unity-test-runner` (`-runTests -batchmode`, filtered to one assembly per mode), and needs a `UNITY_LICENSE`
repository secret. A nightly job (`.github/workflows/nightly-smoke.yml`) builds the Linux player above and runs it
as a dedicated server plus two headless bot clients under Docker Compose (`smoke/docker-compose.yml`): the server
registers with the rendezvous service and publishes a join code, and each bot joins by that code and drives its
square into the arena wall, proving the join-code flow and an RPC round trip end to end.

After changing `src/Generators`, run `dotnet build src/Generators -c Release`. It copies the DLL into
`Runtime/Generators/`, where Unity loads it; commit that DLL along with the source change.

## License
MIT. See [LICENSE](LICENSE).
