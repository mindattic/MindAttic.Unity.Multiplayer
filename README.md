# Unity.Multiplayer

The one-stop shop for multiplayer Unity 6 games. With its two sibling packages, [MindAttic Components](https://github.com/mindattic/MindAttic.Components)
(the menus and HUD) and [Unity.Shared](https://github.com/mindattic/Unity.Shared) (player profiles), it is one kit that
covers the whole game: attribute-decorated RPCs and synced fields, a listen server or a dedicated server from the same
code, joining by IP:port or a short join code, menus, player names and colours, votes, objects found by key, bots, a
test kit, and one command to build, deploy and smoke-test a server. **MindAttic ▸ Multiplayer ▸ New Game…** turns an
empty Unity project into a working networked game in minutes.

![Unity](https://img.shields.io/badge/unity-6000.4-black)
![Netcode for GameObjects](https://img.shields.io/badge/netcode-2.x-blue)
![.NET](https://img.shields.io/badge/dotnet-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-brightgreen)
![Status](https://img.shields.io/badge/status-v2.0.0-brightgreen)

| Package | What it gives a game |
|---|---|
| **Unity.Multiplayer** (`com.mindattic.unity.multiplayer`, this repo) | Networking, roster, votes, keyed objects, bots, the test kit, the server package and `mp-host` |
| **MindAttic Components** (`com.mindattic.components.unity`) | The drop-in menus and vote overlay, and the widgets for your own HUD |
| **Unity.Shared** (`com.mindattic.unity.shared`) | The player's name and colour (and settings) from their MindAttic profile |

See [The kit](https://mindattic.github.io/Unity.Multiplayer/articles/kit.html) for which piece does which job, and
[Case study: Unity.Cursory](https://mindattic.github.io/Unity.Multiplayer/articles/case-study-cursory.html) for a full
game built on all three.

The whole networked surface of a game object is usually three declarations:

```csharp
public partial class MinimalPlayer : NetEntity
{
    [ToServer(RateLimit = 20)]
    public partial void SetDirection(Vector2 direction);

    [ToClients]
    public partial void Bumped();

    [Synced] private Vector2 _position;
}
```

Try it: the full docs site, with a 10-minute getting-started guide, every attribute's rules, and the complete
hosting story, is at [mindattic.github.io/Unity.Multiplayer](https://mindattic.github.io/Unity.Multiplayer/).

> **Status: v2.0.0**, the one-stop shop release (the New Game wizard and template, the kit docs). See Roadmap below
> for what's built, and [CHANGELOG.md](CHANGELOG.md) for what shipped in each release. The design and build order are in
> [docs/rfc/0001-architecture.md](docs/rfc/0001-architecture.md); the rules are in
> [docs/BIBLE.md](docs/BIBLE.md).

## Why

- Start a new multiplayer game from a working one: menus, roster, votes, keyed objects, a bot, tests and a deploy
  config from one wizard, all built from the kit's own pieces, not a demo to throw away.
- Network a new game object in three attributes, not a day of hand-rolled `NetworkVariable`s and RPCs.
- Ship a listen server for tonight's game and a dedicated server for a persistent world from the same game
  code — the topology is one setting, never a fork.
- A player who disconnects gets the same seat and the same player object back if they reconnect within a
  minute, with no special-casing in game code.
- Players join by typing a short, typo-tolerant code, whether the host is behind a home router (Unity Relay)
  or a dedicated box you rented (the self-hosted rendezvous service).
- An ownership mistake, a missing handler or a bad RPC shape is a compile error from the package's own source
  generator and analyzer, never a runtime surprise.
- One `mp-host` CLI and one server package deploy to fifteen-plus hosting providers, from a $4 VPS to AWS
  GameLift, without a hand-written Dockerfile per host.

## Features

- **New Game wizard** — **MindAttic ▸ Multiplayer ▸ New Game…** checks `Packages/manifest.json` and names (or adds)
  the exact lines the kit needs, then makes `Assets/<YourGame>`: a scene with `NetBootstrap`, `NetFrontEnd` and
  `NetVotes`; a player with a `[ToServer]` move and a `[Synced]` position in its roster name and colour; crates spawned
  by key; a restart vote; an `IBotDriver`; EditMode and PlayMode tests on the test kit; and `mp-host.json` plus a nightly
  smoke workflow. See [Getting started](https://mindattic.github.io/Unity.Multiplayer/articles/getting-started.html).
- **Attribute RPCs and synced state** — `[ToServer]`, `[ToClients]`, `[ToOwner]`, `[ToClient]`, `[Synced]`,
  with per-RPC rate limiting, ownership checks and a change handler, all generated at compile time. See
  [Attributes reference](https://mindattic.github.io/Unity.Multiplayer/articles/attributes.html).
- **Objects found by name** — a `NetKey` on a prefab and a key from the server (`"block:12"`, `"door:A"`), and every
  peer finds that object with `NetSession.Objects.TryGet(key, out T)`, late joiners included, whatever order things
  spawned in. See [Networked objects by key](https://mindattic.github.io/Unity.Multiplayer/articles/objects.html).
- **Any topology, same code** — listen server, dedicated server or client from one `NetConfig`. See
  [Topologies](https://mindattic.github.io/Unity.Multiplayer/articles/topologies.html).
- **Join codes and reconnect** — Unity Relay or a self-hosted rendezvous service behind one interface, and
  seats that survive a drop. See [Join codes](https://mindattic.github.io/Unity.Multiplayer/articles/join-codes.html)
  and [Reconnect and seats](https://mindattic.github.io/Unity.Multiplayer/articles/reconnect-seats.html).
- **Drop-in front end** — one `NetFrontEnd` component (**MindAttic ▸ Multiplayer ▸ Add Front End**) gives any
  game a main menu (Host, Join by IP, Join by code, Settings), a lobby with the join code and roster, connection
  dialogs in plain words, automatic reconnect to the same seat and an in-game Esc menu, built from MindAttic
  Components and optional. See
  [Drop-in front end](https://mindattic.github.io/Unity.Multiplayer/articles/front-end.html).
- **Votes** — restart, change-level, kick or any vote a game defines: one `NetVotes` component, a `VoteKind` per
  vote with its effect on the server, two-thirds quorum, timeouts and early rejection, and a ready-made overlay with
  the front end. See [Votes](https://mindattic.github.io/Unity.Multiplayer/articles/votes.html).
- **Editor tooling** — one-click scene setup, a prefab validator, and a Linux Dedicated Server build, all from
  the **MindAttic ▸ Multiplayer** menu.
- **A hosting pipeline** — the same server package deploys to any GenericVps-shaped host, RunPod, or a
  per-match allocator (Edgegap, Gameye, AWS GameLift) through one `mp-host` CLI. See
  [Hosting](https://mindattic.github.io/Unity.Multiplayer/articles/hosting.html).
- **Bad-connection testing** — simulate latency, jitter and packet loss per peer, in the editor or a
  development build, without touching the network.

## Quick start

Prerequisites: Unity 6000.4.3f1, and the kit's three repos cloned side by side with your game's folder:

```powershell
git clone https://github.com/mindattic/Unity.Multiplayer
git clone https://github.com/mindattic/MindAttic.Components
git clone https://github.com/mindattic/Unity.Shared
```

Add the package to the game's `Packages/manifest.json` as a local package, with Netcode for GameObjects listed as a
testable (the package's test kit builds on NGO's test helpers and does not compile without it):

```json
"dependencies": {
  "com.mindattic.unity.multiplayer": "file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer",
  ...
},
"testables": [ "com.unity.netcode.gameobjects" ]
```

It pulls in Netcode for GameObjects 2.x and Unity Transport 2.x. Install `com.unity.services.multiplayer` as
well for Unity Relay join codes.

**A new game:** run **MindAttic ▸ Multiplayer ▸ New Game…**, click **Add missing lines to Packages/manifest.json**,
type a name and click **Create game**, then open its `Game.unity`, press Play and click **Host**. That is a whole game
with menus, votes, a bot and tests; `mp-host game local` runs its dedicated server in Docker and `mp-host game smoke`
runs it with two bots. [Getting started](https://mindattic.github.io/Unity.Multiplayer/articles/getting-started.html)
walks through every step and every file.

**Just the samples:** in **Window > Package Manager**, select **MindAttic Multiplayer**, open **Samples** and import
**Minimal**. Open its `Minimal.unity` scene, press Play and click **Host**; a second copy (Multiplayer Play
Mode, or a build) clicks **Join by IP** or **Join by code**. Each player's square is moved by the server
through one `[ToServer]` call, one `[ToClients]` call and one `[Synced]` field — the sample's own
[README](unity/com.mindattic.unity.multiplayer/Samples~/Minimal/README.md) walks through every line, and the
docs site's [Getting started in 10 minutes](https://mindattic.github.io/Unity.Multiplayer/articles/getting-started.html)
covers the same ground with more detail.

For your own scene with menus in two minutes: add MindAttic Components (and optionally Unity.Shared, for the
player's profile name and colour) to the manifest, run **MindAttic ▸ Multiplayer ▸ Add Front End** and press Play —
the **Front End** sample is that, done. See
[Drop-in front end](https://mindattic.github.io/Unity.Multiplayer/articles/front-end.html).

Without the menus: run **MindAttic ▸ Multiplayer ▸ Set Up Scene**, give your player prefab a `NetworkObject`
and a `partial class` deriving from `NetEntity`, mark methods and fields with the attributes above, and run
**MindAttic ▸ Multiplayer ▸ Validate Prefabs** before you ship. See
[Porting a game](https://mindattic.github.io/Unity.Multiplayer/articles/porting.html) for moving an existing
project onto the package.

## Project layout

| Path | What |
|---|---|
| `unity/com.mindattic.unity.multiplayer/` | The Unity package (`package.json`, Runtime, Editor, Tests); `Runtime/Generators/` holds the built generator DLL |
| `unity/com.mindattic.unity.multiplayer/Runtime/UI/` | The optional drop-in front end (`Unity.Multiplayer.UI`, compiled only when MindAttic Components is installed) |
| `unity/com.mindattic.unity.multiplayer/Samples~/Minimal/` | The Minimal sample Package Manager imports (scene, player prefab, menu, line-by-line README) |
| `unity/com.mindattic.unity.multiplayer/Samples~/FrontEnd/` | The Front End sample: the same arena with the drop-in front end and its game hooks |
| `unity/com.mindattic.unity.multiplayer/Samples~/GameTemplate/` | The game template **New Game…** copies (`Editor/Setup/NewGame*.cs`): scene, player, keyed crates, votes, bot, tests |
| `src/Generators/` | The source generator, analyzers and suppressor (netstandard2.0, Roslyn 4.3.1) |
| `src/Hosting.Cli/` | `mp-host`: builds a server package and deploys it to any hosting profile |
| `server/Rendezvous/` | The rendezvous service (ASP.NET Core .NET 10 minimal API + Dockerfile): join code to a dedicated server's IP:port |
| `tests/` | `Core.Tests`, `Generators.Tests`, `Rendezvous.Tests`, `Hosting.Cli.Tests` (plain `dotnet test`); `Core.Build` compiles Core the way Unity does; `PlayModeHost`, `MinimalSample` and `FrontEndSample` run the Unity PlayMode/EditMode suites |
| `smoke/` | The package's own nightly Docker Compose smoke test: rendezvous, a dedicated server and two headless bot clients joining by code (games use `mp-host game smoke`, see [Bots](docs/articles/bots.md)) |
| `docs/` | DocFX site (`docfx.json`, `articles/`), BIBLE, RFCs, user stories |

## Run a dedicated server

Add a `NetBootstrap` to the NetworkManager's GameObject. A headless launch serves by itself:

```bash
Game.x86_64 -batchmode -nographics -port 7777 -transport udp
```

`-mode host|server|client`, `-address`, `-tickrate`, `-maxplayers`, `-name` and `-password` are read too.
**MindAttic ▸ Multiplayer ▸ Build Dedicated Server (Linux)** makes that build and writes its Dockerfile.
**MindAttic ▸ Multiplayer ▸ Build Server Package** adds `server.json`, `start.sh`, a health endpoint and
per-profile hosting files into `Builds/ServerPackage/<profile>/`. To build and run it with no script of your own,
install `mp-host` once (`powershell -File tools/install-mp-host.ps1`, a .NET tool) and, in the Unity project, run
`mp-host game local` (this PC, Docker) or `mp-host game deploy --profile <GenericVps|RunPod|...>`; it finds the
editor from `ProjectVersion.txt` and the server scene from Build Settings. Full detail, every setting and every hosting
provider: [Topologies](https://mindattic.github.io/Unity.Multiplayer/articles/topologies.html) and
[Hosting](https://mindattic.github.io/Unity.Multiplayer/articles/hosting.html) on the docs site.

## Run the rendezvous service

A dedicated server registers its public IP:port and gets a join code; players resolve the code to connect.

```bash
docker build -f server/Rendezvous/Dockerfile -t mindattic-rendezvous .
docker run -p 8080:8080 -e Rendezvous__RegistrationSecret=<secret> mindattic-rendezvous
```

It will not start without a registration secret (`Rendezvous__AllowOpenRegistration=true` is for local
development only). See [Join codes](https://mindattic.github.io/Unity.Multiplayer/articles/join-codes.html) for
the full protocol, TLS and secrets.

## Test on a bad connection

`NetConfig.Conditions` (or the Net Bootstrap's Inspector fields, or `-netsim`) makes this peer's link slow and
lossy: `Broadband`, `Mobile4G`, `Mobile3G`, `Lossy` or `Custom` (`-latency 150 -jitter 10 -loss 5`). Editor and
development builds only. Details:
[Topologies § Testing a bad connection](https://mindattic.github.io/Unity.Multiplayer/articles/topologies.html#testing-a-bad-connection).

## Test

```bash
dotnet test tests/Core.Tests
dotnet test tests/Rendezvous.Tests
dotnet test tests/Generators.Tests          # UPDATE_SNAPSHOTS=1 rewrites Snapshots/ after an intended change
dotnet test tests/Hosting.Cli.Tests
powershell -File tests/run-playmode.ps1     # PlayMode suite in Unity 6000.4.3f1 batch mode
powershell -File tests/run-editmode.ps1     # EditMode suite (editor tooling, one real Linux server build; -SkipBuild leaves it out)
powershell -File tests/run-sample.ps1       # imports the Minimal sample through Package Manager and runs its PlayMode tests (-Author rebuilds its scene and prefabs)
powershell -File tests/run-sample.ps1 -Sample FrontEnd   # the same for the Front End sample (needs the sibling MindAttic.Components and Unity.Shared repos)
powershell -File tests/run-new-game.ps1     # a brand-new project in %TEMP%: New Game's manifest check, create, the game's EditMode + PlayMode tests (-Smoke adds mp-host game smoke; -Author rebuilds the template's scene and prefabs)
powershell -File tests/run-smoke.ps1        # builds the nightly smoke test's Linux player (server + 2 bots, same build, role picked by MINDATTIC_ROLE)
```

CI (`.github/workflows/ci.yml`) runs the `dotnet test` suites and the PlayMode/EditMode suites through GameCI's
`unity-test-runner`, and needs a `UNITY_LICENSE` repository secret. A nightly job
(`.github/workflows/nightly-smoke.yml`) runs the Docker Compose smoke test above end to end.
`.github/workflows/docs.yml` publishes the docs site. See
[Testing](https://mindattic.github.io/Unity.Multiplayer/articles/testing.html) for what each suite covers.

After changing `src/Generators`, run `dotnet build src/Generators -c Release`. It copies the DLL into
`Runtime/Generators/`, where Unity loads it; commit that DLL along with the source change.

## Roadmap

Built and verified (each backed by a test suite — see [docs/BIBLE.md](docs/BIBLE.md) for exactly which one):
session and config, join codes, connection approval and seats, the NGO adapter and headless bootstrap, every
RPC direction and synced fields, the source generator and its analyzer, the rendezvous service, network
condition simulation, the editor tooling, the Minimal sample, CI and the nightly smoke test, the full hosting
pipeline (`mp-host` plus every provider's profile), and this docs site.

**v1.0.0** is tagged and released (UM-T20). **v2.0.0** (UM-T59) adds the drop-in front end, the roster, votes, keyed
objects, bots, the test kit, `mp-host game` and the New Game wizard: everything the kit needs to be a one-stop shop.
See [CHANGELOG.md](CHANGELOG.md) for everything that shipped and the
[GitHub releases page](https://github.com/mindattic/Unity.Multiplayer/releases) for the releases themselves.

## Documentation

- [Docs site](https://mindattic.github.io/Unity.Multiplayer/) — guides (getting started, the kit, the Unity.Cursory
  case study, front end, players, votes, objects, topologies, attributes, join codes, reconnect and seats, testing,
  bots, hosting, porting, troubleshooting) and the API reference generated from this repo's XML doc comments.
- [CHANGELOG.md](CHANGELOG.md) — what shipped in each release.
- [docs/BIBLE.md](docs/BIBLE.md) — the project's rules and verified state.
- [docs/rfc/0001-architecture.md](docs/rfc/0001-architecture.md) — the design and build order.
- [docs/USER_STORIES.md](docs/USER_STORIES.md) — every ticket and its status.
- [AGENTS.md](AGENTS.md) — agent and contributor instructions for working in this repo.

## License

MIT. See [LICENSE](LICENSE).

Part of [MindAttic](https://mindattic.com) — see more projects at
[github.com/mindattic](https://github.com/mindattic). Related: [Unity.Cursory](https://github.com/mindattic/Unity.Cursory)
(the first game built on this package), [Unity.Shared](https://github.com/mindattic/Unity.Shared) (player
profiles every MindAttic Unity game shares) and [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault)
(where `mp-host` keeps hosting credentials).
