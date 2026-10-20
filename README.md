# MindAttic.Unity.Multiplayer
Reusable Unity 6 multiplayer for MindAttic games: attribute RPCs, host or dedicated server, join by IP or code.

> **Status: early.** A game can host, serve and join by IP:port through `NetSession` / `NetBootstrap`, with
> approval, seats, `NetEntity` RPCs in every direction, synced fields and server-driven scene changes. The RPC
> and synced-field plumbing is written by the package's source generator; join codes are still to come. The design and build order are in
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

## Layout
| Path | What |
|---|---|
| `unity/com.mindattic.unity.multiplayer/` | The Unity package (`package.json`, Runtime, Editor, Tests); `Runtime/Generators/` holds the built generator DLL |
| `src/Generators/` | The source generator, analyzers and suppressor (netstandard2.0, Roslyn 4.3.1) |
| `tests/Generators.Tests/` | Snapshot and diagnostic tests for the generator |
| `tests/Core.Tests/` | NUnit tests for the engine-free Core, compiled straight from `Runtime/Core` |
| `tests/Core.Build/` | Compiles `Runtime/Core` as netstandard2.1 / C# 9, the way Unity does |
| `tests/PlayModeHost/` | A bare Unity 6000.4.3f1 project that lists the package in `testables`, for the PlayMode suite |
| `docs/` | BIBLE, RFCs, user stories |

## Run a dedicated server
Add a `NetBootstrap` to the NetworkManager's GameObject. A headless launch serves by itself:

```
Game.x86_64 -batchmode -nographics -port 7777 -transport udp
```

`-mode host|server|client`, `-address`, `-tickrate` and `-maxplayers` are read too.

## Test
```
dotnet test tests/Core.Tests
dotnet test tests/Generators.Tests          # UPDATE_SNAPSHOTS=1 rewrites Snapshots/ after an intended change
powershell -File tests/run-playmode.ps1     # PlayMode suite in Unity 6000.4.3f1 batch mode
```

After changing `src/Generators`, run `dotnet build src/Generators -c Release`. It copies the DLL into
`Runtime/Generators/`, where Unity loads it; commit that DLL along with the source change.

## License
MIT. See [LICENSE](LICENSE).
