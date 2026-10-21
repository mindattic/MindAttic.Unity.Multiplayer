# Changelog

All notable changes to Unity.Multiplayer are documented here. The package follows
[HOUSE-LAW-1](https://github.com/mindattic/MindAttic/blob/main/MindAttic.HouseRules.md#house-law-1--whole-number-versioning)
whole-number versioning (`1`, `2`, `3` — never `1.1.0`); `unity/com.mindattic.unity.multiplayer/package.json`
carries the package's own version.

## [Unreleased]

### Added

- **Votes kit** (UM-T54) — server-authoritative votes any game can offer, promoted from Unity.Cursory. Core (engine-free)
  gets `VoteBox` (one vote at a time over a roster of client ids; passes when Yes reaches the quorum, rejected as soon
  as it cannot, lapses on the `ITickClock`; a leaver is dropped and the quorum recomputed), `VoteRules` (quorum fraction,
  default 2/3 rounded up; timeout, default 15 s; optional per-starter cooldown; who may start; the starter's own Yes),
  `VoteKind` (id, question and description worded from the request, `Check`, `MayVote`, the server-side `Passed`
  effect, per-kind quorum and time), `VoteRequest` (an optional `long` and `string` payload), `VoteStatus`,
  `VoteResult` and `VoteText` (the overlay's words). The adapter gets `VoteBoard`, a `NetEntity` spawned from the
  package's own prefab (`RegisterPrefab`, `Spawn`, `AddKind`, `StartVote`, `CastVote`, `Status`, `LocalChoice`,
  `VoteChanged`, `VoteEnded`, `VoteRefused`) with a built-in kick vote through `NetSession.Kick`
  (`AddKickKind`, `StartKickVote`), and `NetVotes`, the one component that sets it all up beside a `NetBootstrap`. The
  front end gets `VoteOverlay` (question, tally, quorum bar, Yes/No, result toasts), wired by itself when a `NetFrontEnd`
  has menus. See [Votes](docs/articles/votes.md).
- **Player roster and identity** (UM-T55) — `NetSession.Roster` (`NetRoster`, also `NetRoster.Current`) is who is in
  the game, on every peer: one `NetPlayer` per seat with `Seat`, `ClientId`, `DisplayName`, `Color`, `IsHost`,
  `IsConnected` (false while a dropped player's seat is held), `PingMs` and `IsLocal`; `All`, `Local`,
  `TryGet(clientId)`, `TryGetBySeat(seat)`, and `Joined` / `Left` / `Changed` (`NetPlayerChange` says what changed).
  The server fills it from the connection approval payload (names cleaned, an unpicked colour resolved to the seat's
  palette colour), sends each connection the whole roster once it is in (late joiners and reconnects too; a reconnect
  keeps seat and identity) and then only changes, over named messages, and measures every client's round trip once a
  second. `NetRoster.RequestChange(name, colour)` changes a player's name or colour at runtime: the server checks it
  (`PlayerNames.Validate`, `PlayerColors.Validate`, 5 at once then 1 a second) and answers with a
  `PlayerChangeResult`; an accepted change reaches every peer and is kept with the seat and in `NetSession.Identity`.
  `NetFrontEnd`'s lobby shows the roster, its Settings screen renames mid-game through the server, and an accepted
  change of the local player is saved back to its `IPlayerIdentitySource` (the MindAttic profile by default). Core
  gains `NetPlayer`, `PlayerNames`, `PlayerColors`, `PlayerRoster`, `RosterUpdate`, `RosterWire`,
  `PlayerChangeRequest` and `SeatRegistry.Rename`; the adapter `PlayerColorExtensions` (`ToColor32`, `ToPlayerColor`,
  `UnityColor`). See [Players, names and colours](docs/articles/players.md).
- **Front-end test kit** (UM-T62) — `Unity.Multiplayer.UI.Testing` (editor and test builds only, and only with
  MindAttic Components) lets a game's own PlayMode tests drive its `NetFrontEnd`: `FrontEndTesting.ForceMenus` builds
  the menus in a batch-mode run, `Bind`, `MainScreen`/`LobbyScreen`/`LobbyCode`/`LobbyRoster`/`BusyDialog`/
  `MessageDialog`, `Click`/`Buttons`/`HasButton`, `CleanUpUi`, and `FixedIdentitySource` (an in-memory name and colour).
  Before it, the menus override and `Bind` were internal to the package's own tests, so a game got no menus to test.
- **`mp-host game`** (UM-T58) — deploy any Unity.Multiplayer game with no per-game script:
  `mp-host game local|down|deploy|update|status|logs|destroy [--project <dir>] [--profile <p>] [--scene <path>] [--dry-run]`
  finds the editor from `ProjectVersion.txt` (Unity Hub's folders, editor list and install path, or `--unity` /
  `UNITY_EDITOR`), picks the server scene (`--scene`, `mp-host.json`, else the first Build Settings scene), defaults
  name/image/address/rendezvous from the project and vault, remembers deployed server ids, and runs the existing
  pipeline. `ServerPackageBuild.BuildFromCommandLine` takes `-serverScene` (else opens the first Build Settings scene
  in a fresh batch editor). `mp-host` packs as the `MindAttic.Unity.Multiplayer.Hosting.Cli` .NET tool
  (`tools/install-mp-host.ps1`). `mp-host local up --dry-run` no longer launches Unity, and every profile builds the
  Unity package profile it runs (`GenericVps` for the VPS/cloud providers and Local).
- **Drop-in front end** (UM-T48) — `NetFrontEnd`, in the new optional `Unity.Multiplayer.UI` assembly (compiled only
  when `com.mindattic.components.unity` is installed), gives any game its multiplayer menus from one component:
  a main menu (Host, Join by IP, Join by code, Settings with name and colour, Quit), a host lobby with the grouped
  join code, a Copy button and a live roster with colour swatches and remove, a cancellable connecting dialog, a
  plain-words dialog for every disconnect reason, an automatic reconnect to the same seat with progress, and an
  in-game Esc menu (Resume, Invite players, Settings, Leave). The player's name and colour come from an
  `IPlayerIdentitySource` (the MindAttic profile through `com.mindattic.unity.shared`, else PlayerPrefs) and go in
  the connection approval payload. Hooks: `SessionStarted`, `GameStarted`, `Left`, `Reconnected`, `StateChanged`,
  `AddScreen`, `AddMainMenuItems`, `AddPauseMenuItems`, `AddSettingsItems`, `MenusVisible`; every button is also a
  method. A headless or `-mode` launch never creates UI. Optional `NetFrontEndSettings` asset. **MindAttic ▸
  Multiplayer ▸ Add Front End** networks the scene and adds it, or names the exact manifest lines that are missing.
  Core gains `DisconnectText`, `ReconnectPolicy`, `HostAddress` and `NetLaunch.ShowsMenus`. New **Front End**
  sample; see [Drop-in front end](docs/articles/front-end.md).
- **Test kit: the `Unity.Multiplayer.Testing` assembly** (UM-T56) — what a game references from its own PlayMode
  test assembly to test its networking over a real connection. `NetSessionTest(Topology.Host | Server, clients,
  transport)` is a fixture (on NGO's `NetcodeIntegrationTest`) with a host or dedicated server and N client
  `NetSession`s on a free port, `StartServer` / `Join` / `StartAndJoinAll`, `CreatePrefab<T>` / `RegisterPrefab` /
  `SetPlayerPrefab` / `Spawn<T>`, and waits that fail saying what never happened: `WaitUntil`, `WaitForSpawn<T>`,
  `WaitForCopy<T>`, `WaitForSynced<T>`, `WaitFor(Task)`. Also usable alone: `FreePort.Udp()` / `Tcp()` / `For()` /
  `Assign()` (a port free right now, instead of a fixed one that collides with a parallel run),
  `TestSessions.Create(NetworkManager)` (a `NetSession` without reflecting into its internal constructor), `NetAwait`
  (the same waits as statics, plus `UntilAsync` / `ForSpawnAsync` / `ForSyncedAsync`), and
  `InProcessRendezvousServer` (the rendezvous API served in-process for join-by-code tests, now public, with
  `ForServer` / `ForClient` config helpers and a configurable secret). Its define constraint, `UNITY_EDITOR ||
  UNITY_INCLUDE_TESTS`, keeps it out of player builds; a game lists NGO, not this package, under `testables`. The package's own PlayMode
  suites and the Minimal sample's tests now run on it, each on a free port. See
  [Testing](docs/articles/testing.md#the-test-kit).

- **`NetBootstrap.Configure`** (UM-T33) — sets the bootstrap's default `NetConfig` in code (topology, address,
  port, transport, tick rate, max players, game version, join codes, conditions, …) before the session starts,
  instead of reflecting into its private serialized fields. `Configure(NetConfig)` copies and validates the
  config; `Configure(Action<NetConfig>)` edits a copy of the current defaults; both are refused while the
  session runs. `DefaultConfig` reads the defaults back. The environment and command line still override them.

### Fixed

- **A host with the default Join Codes uses a configured rendezvous URL** (UM-T63) — with `JoinCodes` left at `Auto`,
  `NetFrontEnd`'s Host and `NetSession.HostWithCode` tried only Unity Relay, so a game with a rendezvous URL but no
  Relay hosted with no code. `Auto` for a listen server is now Relay when it is installed, then the rendezvous service
  when a URL is set (`JoinCodeDefaults.HostingOrder` / `ForHosting`); the front end tries each in turn before hosting
  for joining by IP, and `PublishCode` on a running host uses the URL.
- **In-game menu items run once** (UM-T64) — `NetFrontEnd.AddPauseMenuItems` callbacks ran again on every refill of the
  in-game menu (each game entered, each join code published) on the same screen, so a `Shown` handler they hooked
  piled up one more each time. The in-game menu is now built once: each callback runs exactly once, its items stay,
  and only the built-in parts (subtitle, Invite players for a host, End game / Leave game) follow the session.

## [1.0.0] — 2026-10-21

First stable release. Everything below was built across UM-T2 through UM-T19 and UM-T23 through UM-T46; full
per-ticket detail lives in [docs/USER_STORIES.md](docs/USER_STORIES.md) and [docs/BIBLE.md](docs/BIBLE.md), and
the design in [docs/rfc/0001-architecture.md](docs/rfc/0001-architecture.md).

### Core networking

- **Attribute-decorated RPCs and synced state** — `[ToServer]`, `[ToClients]`, `[ToOwner]`, `[ToClient]` and
  `[Synced]` on a `NetEntity` subclass; the package's own Roslyn source generator (netstandard2.0,
  `IIncrementalGenerator`) writes every send, stub and `NetworkVariable` binding, and its analyzer rejects a
  client write to shared state, a non-partial handler, an unsendable parameter or a generic `NetEntity` at
  compile time (diagnostics MAMP001–MAMP013).
- **Any topology, same code** — `NetConfig` and `SessionStateMachine` drive a listen server, dedicated server
  or client from one game codebase; `NetSession` wraps NetworkManager + Unity Transport (UDP or WebSocket) and
  a headless bootstrap with command-line and environment overrides.
- **Join codes** — a 9-character, typo-tolerant `JoinCode` (`ABC-EFG-HJK`, a Damm check digit that catches
  every single substitution and neighbour swap) resolved through Unity Relay for a listen server or the
  package's own self-hosted ASP.NET Core rendezvous service for a dedicated server, behind one
  `IJoinCodeService` interface.
- **Reconnect and seats** — `SeatRegistry` holds a dropped player's seat and player object for a configurable
  grace period, reclaims it on the same token's return, and can take over a stale connection; a kicked or
  replaced client is always told why before the server closes its connection.
- **Connection approval** — a versioned `ConnectionPayload` (game version, protocol hash, player token,
  display name, colour) is checked before a client is seated, with a typed rejection (version mismatch,
  protocol mismatch, banned, full) and a human-readable reason on the wire.
- **Network condition simulation** — built-in profiles (`Broadband`, `Mobile4G`, `Mobile3G`, `Lossy`, `Custom`)
  retune Unity Transport's simulator pipeline at runtime, from the command line or a live `NetSession`, for
  testing on a bad connection without leaving the editor.

### Tooling and samples

- **Editor tooling** — **MindAttic ▸ Multiplayer ▸ Set Up Scene**, **Validate Prefabs** and
  **Build Dedicated Server (Linux)** turn an un-networked scene into a validated, buildable dedicated server in
  one menu.
- **Minimal sample** — a 10-minute, Package Manager–importable sample (host, join by IP, join by code) built
  from one `[ToServer]` call, one `[ToClients]` call and one `[Synced]` field.
- **CI** — `dotnet test` across every engine-free assembly plus GameCI PlayMode/EditMode runs on every push, and
  a nightly Docker Compose smoke test exercising a real dedicated server, rendezvous service and two bot
  clients end to end.

### Hosting

- **`mp-host`** — one deploy pipeline (build the server package, push to GHCR, provision, open the firewall,
  health-check, read back the self-registered join code) now reaches fifteen-plus providers: a generic VPS over
  SSH + Docker Compose, RunPod, Hetzner Cloud, Vultr, DigitalOcean, Linode/Akamai, OVHcloud, AWS EC2, AWS
  Lightsail, Google Compute Engine, Azure VM, Fly.io, Oracle Cloud's Always Free tier, and free local/LAN
  hosting with best-effort UPnP port mapping — plus match-allocation platforms Edgegap, Gameye and AWS
  GameLift through a shared `IMatchAllocator` contract. Every credential resolves through `MindAttic.Vault`,
  never the repo; every provider call is covered by NUnit mocks, with opt-in `[Explicit]` smoke tests against
  the real APIs.
- **[Hosting guide](https://mindattic.github.io/Unity.Multiplayer/articles/hosting.html)** ties the whole
  provider table together, with one page per provider covering setup, sizing and cost.

### Documentation

- **[Docs site](https://mindattic.github.io/Unity.Multiplayer/)** — a DocFX site published to GitHub Pages:
  a 10-minute getting-started guide, topologies, the attributes reference, join codes, reconnect and seats,
  testing, hosting, porting an existing game, troubleshooting, and an API reference generated from this
  repository's XML doc comments.
- **README** rewritten to the house `README-STYLE.md` format and linked to the docs site.

### Fixes rolled into this release

A bug-hunt pass (UM-T36) and standalone reports found and fixed, each verified by a test:

- `mp-host`: a malformed ARM request URL in the Azure VM client (UM-T37); the generated Compose file mounting
  saves/logs at the wrong container path (UM-T38); `--dry-run` still blocking on a real health/join-code
  timeout over SSH (UM-T39); `--dry-run` still launching a real Unity batch-mode build (UM-T40); batch-mode
  server builds ignoring the open scene's port/transport (UM-T41); a literal `$` in a secret or environment
  value corrupted by Compose interpolation (UM-T42); SSH passphrase-protected keys silently failing under
  `BatchMode=yes` (UM-T43); `--dry-run status` always reporting `Stopped` over SSH (UM-T44); a missing
  credential file crashing the CLI with a raw stack trace instead of a clear message (UM-T45).
- Package: `Unity.Multiplayer.asmdef` missing its `Unity.Services.Relay` reference, breaking a
  `MINDATTIC_UGS` build (UM-T46).

### Known limits

- `noEngineReferences` keeps `Unity.Multiplayer.Core` free of `UnityEngine` and `Unity.Netcode`
  ([UM-LAW-2](docs/BIBLE.md#um-law-2--nothing-above-the-ngo-adapter-imports-unitynetcode)); everything above the
  NGO adapter sees only MindAttic types.
- A live Unity Relay allocation needs a linked Unity Gaming Services project and is not exercised by the test
  suite; `MINDATTIC_UGS` compiles and is covered by the rest of the PlayMode suite.
- Ampere/ARM hosts (Oracle Cloud's Always Free A1 shape) have no matching Unity dedicated-server build target
  today, so `OciProvisioner` defaults to the AMD shape (UM-T31).
