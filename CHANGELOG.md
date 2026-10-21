# Changelog

All notable changes to Unity.Multiplayer are documented here. The package follows
[HOUSE-LAW-1](https://github.com/mindattic/MindAttic/blob/main/MindAttic.HouseRules.md#house-law-1--whole-number-versioning)
whole-number versioning (`1`, `2`, `3` — never `1.1.0`); `unity/com.mindattic.unity.multiplayer/package.json`
carries the package's own version.

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
