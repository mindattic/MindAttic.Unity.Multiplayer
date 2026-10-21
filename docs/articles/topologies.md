# Topologies

The same game code runs three ways. The topology is configuration (`NetConfig.Topology` / `Topology`), never a
code fork — BIBLE's product promise (UM-§2).

```csharp
public enum Topology
{
    Host,   // this process is the server and a player
    Server, // this process is the server only (headless)
    Client, // this process joins someone else's server
}
```

## Listen server (Host)

One player's game is also the server: `NetSession.StartHost()` (or `NetBootstrap.StartHost()`). Simplest to run
(no separate process, no hosting account), but the host's own frame rate, connection and crashes affect
everyone. Good for small co-op sessions and for `Topology.Host` in the Minimal sample.

```csharp
bootstrap.StartHost();
```

The default join-code provider for a Host is **Unity Relay** (see [Join codes](join-codes.md)): it gets through
a home router's NAT with no port forwarding, which a listen server playing over the internet needs.

## Dedicated server (Server)

A headless build that only serves; every player, including whoever started it, joins as a client.

```csharp
bootstrap.StartServer();     // in-process
// or, from a built player:
// Game.x86_64 -batchmode -nographics -mode server -port 7777 -transport udp
```

`NetCommandLine` reads `-mode host|server|client`, `-address`/`-ip`, `-port`, `-transport`, `-tickrate`,
`-maxplayers`, `-name` and `-password`; `NetLaunch` decides what actually starts: an explicit `-mode` wins, else
a headless process (`-batchmode`, `-nographics`, or a Dedicated Server build) serves by itself, else
`NetBootstrap`'s **Auto Start** applies. The headless bootstrap also turns off vsync and sets
`targetFrameRate` to the configured tick rate, since nothing is rendering.

The default join-code provider for a Server is the **rendezvous service** (self-hosted; see
[Join codes](join-codes.md)), since Relay isn't needed when the server already has a reachable address.

### Building the Linux player

**MindAttic > Multiplayer > Build Dedicated Server (Linux)** validates every network prefab first (see
[Attributes reference](attributes.md#generator-diagnostics) and the [Troubleshooting](troubleshooting.md) guide
for what that catches) and stops on errors. It then builds the open scene's enabled Build Settings scenes as a
headless Linux x86_64 Dedicated Server into `Builds/LinuxServer/`, and writes a `Dockerfile` and `.dockerignore`
beside it:

```bash
cd Builds/LinuxServer
docker build -t game-server .
docker run --rm -p 7777:7777/udp -e MINDATTIC_RENDEZVOUS_SECRET=<secret> game-server -rendezvous https://rendezvous.example.com
```

Arguments after the image name are appended to the server's command line. CI builds the same way:

```bash
Unity -batchmode -quit -projectPath <game> -executeMethod Unity.Multiplayer.Editor.DedicatedServerBuild.BuildFromCommandLine
```

with the optional `-serverOutput <dir> -port <n> -transport udp|websocket -serverBackend mono|il2cpp
-serverDevelopment`.

### Packaging for a host

**MindAttic > Multiplayer > Build Server Package > GenericVps (default)** or **> RunPod** builds the same
Linux server plus everything a hosting provider needs (`server.json`, `start.sh`, a health endpoint, logs and
saves directories, a Dockerfile) into `Builds/ServerPackage/<profile>/`. See [Hosting](hosting.md) for the full
provider table and how `mp-host` deploys a package.

## Client

Joins someone else's Host or Server.

```csharp
bootstrap.JoinByAddress("203.0.113.10", 7777);
// or
await bootstrap.JoinByCode("ABC-DEF-GHJ");
```

A Client never writes shared game state directly — it calls `[ToServer]` methods and reads `[Synced]` state the
server already wrote (UM-LAW-1; see [Attributes reference](attributes.md)). The generator and analyzers reject
code that writes a `[Synced]` field from anywhere but the server, so a mistake here is a compile error, not a
runtime bug.

## Picking one

| You want | Topology |
|---|---|
| Quick co-op session, no extra process to run | Host (listen server) |
| A server that outlives any one player, or that you rent on a host | Server (dedicated) |
| Playing someone else's game | Client |

`NetSession` exposes `IsHost` / `IsServer` / `IsClient` so game UI (like `MinimalMenu`) can read which one is
live without re-deriving it from `Topology`.

## Testing a bad connection

Every topology can simulate latency, jitter and packet loss on its own link: `NetConfig.Conditions` (or the
`NetBootstrap` Inspector fields, or `-netsim`) sets a profile — `Broadband` (20 ms), `Mobile4G` (60 ms, 1%
loss), `Mobile3G` (200 ms, 3% loss), `Lossy` (150 ms, 5% loss) or `Custom`
(`-latency 150 -jitter 10 -loss 5`). It only works in the editor and development builds, and
`NetSession.SetConditions` can change it while the game runs. See [Testing](testing.md) for the PlayMode suite
that exercises this (`LossyNetworkTests`).
