---
codex: 1
project: MindAttic.Unity.Multiplayer
code: UM
layer: rfc
status: accepted
updated: 2026-10-20
---

# RFC 0001 — Architecture and build order

> Approved 2026-10-20. Ticket IDs below are the real workflow IDs; the chain is enforced by `requires` links (`workflow ticket show <ID>`).


## Context
Cursory is a co-op physics puzzle game. The server owns the game (CUR-LAW-1), and it runs as ASP.NET Blazor Server with SignalR and Aether.Physics2D. Players drag blocks together, and a block heavier than 1.5 mass units needs two cursors. It has 14 levels, votes, whistles and a circuit puzzle. Ryan wants that gameplay in **Unity 6 (6000.4.3f1, the workspace's version)** with full multiplayer in two forms:
- **dedicated server** (hub-and-spoke): everyone joins a central headless server
- **listen server:** one player hosts

Players join by **IP:port or a join code**.

The main goal is a multiplayer layer that is **reusable in any game with minimal effort**. That layer is a new repo, **MindAttic.Unity.Multiplayer**, built around attribute-decorated RPCs, with full docs and a docs website. All work is filed as **chained workflow tickets**, so it can only be built in the right order.

What exploration found:
- **MindAttic.Unity has no code.** Its design exists only as ticket MAU-T2, which sets the family conventions:
  - a local `file:../../` package
  - an engine-free Core asmdef tested by plain `dotnet test` with NUnit
  - a thin Runtime asmdef
- **Packaging precedent:** the Capture and Telemetry packages use the `unity/com.mindattic.<x>/` layout.
- **No Unity project uses networking or CI.**
- **No repo has a docs site.** README-STYLE.md makes the README the project page, so DocFX is new ground.
- **Workflow tooling:** `workflow ticket link A requires B` gives hard ordering, and `repos rollout tickets` assigns a code to a new repo.

## Decisions (confirmed by the owner)
- **Netcode for GameObjects 2.x and Unity Transport,** hidden behind MindAttic's own API. NGO is the only backend for now; nothing above the adapter references NGO.
- **Join codes: one `IJoinCodeService` interface, two implementations:**
  - Unity Relay: gets through NAT, so it is the default for listen servers
  - a self-hosted rendezvous service that maps a code to IP:port, the default for dedicated servers
- **Docs:** DocFX, published to GitHub Pages at `mindattic.github.io/MindAttic.Unity.Multiplayer`. The README still follows README-STYLE.
- **Approving this plan approves every ticket below** (`orchestrator approve`). The `requires` links stop work from starting out of order.

## Architecture

### MindAttic.Unity.Multiplayer (public, MIT)
```
unity/com.mindattic.unity.multiplayer/      package.json (unity 6000.0; deps netcode.gameobjects 2.x,
                                            transport 2.x; services.multiplayer optional via versionDefines)
  Runtime/Core/   MindAttic.Unity.Multiplayer.Core.asmdef  (noEngineReferences, netstandard2.1)
                  SessionStateMachine, JoinCode format, NetConfig, ITickClock, attributes
                  ([ToServer]/[ToClients]/[ToOwner]/[ToClient]/[Synced]), approval payload,
                  protocol-hash contract, rendezvous wire protocol + client
  Runtime/        MindAttic.Unity.Multiplayer.asmdef  (NGO adapter)
                  NetSession (StartHost/StartServer/JoinByAddress/JoinByCode/Leave), NetEntity
                  (non-generic NetworkBehaviour base), NetSender, transports (UDP | WebSocket),
                  RelayJoinCodeService, RendezvousJoinCodeService, ConnectionApproval
                  (version + protocol hash + player token), SeatRegistry (reconnect → same seat),
                  NetworkTickClock, server-driven scene flow, per-sender rate limiter,
                  headless bootstrap (-batchmode, targetFrameRate=tick), NetBootstrap component
  Runtime/Generators/  MindAttic.Unity.Multiplayer.Generators.dll  (RoslynAnalyzer label)
  Editor/         setup wizard (MindAttic ▸ Multiplayer ▸ Set Up Scene), prefab validator,
                  "Build Dedicated Server (Linux)" menu
  Tests/Runtime/  NetcodeIntegrationTest PlayMode suites (NGO in manifest "testables")
  Samples~/       Minimal (host/join/code UI, one [ToServer], one [Synced], one [ToClients])
src/Generators/   IIncrementalGenerator, netstandard2.0, Microsoft.CodeAnalysis.CSharp 4.3.x, emits C# 9 only
server/Rendezvous/  ASP.NET Core .NET 10 minimal API + Dockerfile (register/heartbeat/resolve,
                  TTL, high-entropy codes, per-IP rate limit, registration secret)
tests/            Core.Tests (net10 NUnit, globs Runtime/Core), Generators.Tests, Rendezvous.Tests
docs/             DocFX: docfx.json, articles/ (guides), api/ (from XML docs); BIBLE.md, USER_STORIES.md
.github/workflows/  docs.yml (Pages), ci.yml (dotnet tests + GameCI PlayMode)
```

### Attribute API: what a game writes
```csharp
public partial class CursorAvatar : NetEntity
{
    [ToServer(Delivery = Delivery.Unreliable, RateLimit = 30)]
    public partial void Move(Vector2 worldPos);                       // call site: Move(p)
    void OnMove(Vector2 worldPos, NetSender sender) { ... }          // runs on server

    [ToClients] public partial void Whistle(Color c);
    void OnWhistle(Color c) { ... }                                  // runs on every client

    [Synced(OnChanged = nameof(MassChanged))] float _mass;          // generator emits NetworkVariable + Mass property
}
```
- **How it works:** these are C# 9 extended partial methods. The generator writes the sending body and the matching NGO `...Rpc` stub, which applies ownership checks, rate limiting and the sender, then calls `On<Name>`.
- **If generation fails,** the build stops with "partial method has no implementation" instead of failing silently.
- **Analyzer errors for:**
  - generic `NetEntity` subclasses
  - parameter types that can't be serialized
  - direct writes to a `[Synced]` backing field
  - a missing `On<Name>` handler
- **Protocol hash:** the generator emits one hash of every RPC and Synced signature. Connection approval rejects a mismatch with a clear reason.
- **Fallback if the spike shows ILPP problems:** hand-written stubs shaped exactly like the generator output. Not reflection.

### Hosting
**The hosting provider is undecided (owner, 2026-10-20) and will be chosen later.** The design keeps it open: UM-T23 adds `IHostingProfile` and a provider-neutral server package. That package is a self-contained Linux build with `server.json` plus command-line overrides, declared ports, `Logs/`, `Saves/`, clean shutdown on SIGTERM and a health endpoint. Every host gets the same package, so choosing one later changes no game code.
- **GenericVps:** the first profile built. It runs the Docker image plus the rendezvous container on a UDP-capable CPU VPS (Hetzner, Vultr, DigitalOcean and similar).
- **RunPod:** pods appear to expose TCP/HTTP only, so this profile uses `UseWebSockets=true` and registers its mapped `RUNPOD_TCP_PORT_*` with the rendezvous service. UM-T18 verifies RunPod's current UDP support first.
- **Later, possibly:** G-Portal (not a default; reachable only through its Private Server program for studios), Nitrado, Edgegap and GameLift. Each would plug in as a new profile.

**The owner's home network allows no port forwarding (an HOA-controlled network).** That has three consequences:
- A listen server hosted from home must use **Relay** join codes.
- Join by IP from home works on the LAN only.
- Internet play by IP needs a dedicated server on a rented host.

The self-hosted rendezvous service maps codes to addresses but cannot get through NAT, so it is for dedicated servers only. Development needs no port forwarding: localhost, LAN, Multiplayer Play Mode and Relay all work behind NAT.

### UnityCursory (public)
Unity 6 2D top-down with URP 2D and an orthographic camera.

**Server simulation:**
- Runs on the server only.
- `Physics2D.simulationMode = Script`, stepped at 1/30 s from `NetworkTickSystem.Tick` (TickRate 30).
- Clients never simulate.

**Mapping from Cursory** (constants from `Cursory.Core/Services/RoomState.cs:49-119`):
- Grab: `FixedMouseJoint` becomes `TargetJoint2D` (maxForce 60, frequency 5, damping 0.7, autoConfigureTarget off).
- Ground friction: the Aether friction joint becomes `FrictionJoint2D` with no connected body (maxForce Mass×40, maxTorque ×1.2).
- Bodies: `rb.mass = Mass×5`, gravity 0, linear damping 1.5, angular damping 2.0.
- Units: keep 100 px per metre.

These values keep "mass > 1.5 needs two players" intact. Level masses stay clear of 1.5 and 3.0.

**Syncing:**
- Server-authoritative NetworkTransform plus NetworkRigidbody2D, syncing x, y and rotation z only.
- Half precision, interpolated.
- Hidden behind an `IBodySync` interface, so a compact snapshot can replace it later.

**Content and features:**
- Levels become ScriptableObjects, one per level, ported from `SeedLevel1..14`.
- Votes use a 2/3 quorum with a 15 s timeout, and early rejection when the vote can no longer pass.
- Whistles, the circuit puzzle, the HUD, the pause menu and the minimap all carry over.
- The cursor is the player object.
- The player's name and color come from the MindAttic.Unity profile (MAU-T2), which replaces Cursory's cookie accounts.
- The cursor-collision and segmented-tether toggles become per-player settings, fixing Cursory's README/code mismatch.
- Out of scope: Cursory's dead `SwitchTile`/`Door` types.

## Execution (Step 0 done 2026-10-20 under MAT-T7, UM-T2, UC-T2)

### Step 0: bootstrap (done by me right after approval, one MindAttic.Toolkit chore ticket)
1. `gh repo create mindattic/MindAttic.Unity.Multiplayer --public` and `mindattic/UnityCursory --public`.
2. Run `mindattic-agent-standard/clone-repo.ps1 -Repo <name>` for each. This clones the repo, tags it `wip` and restarts the Launcher.
3. `workflow repos rollout tickets --apply`, which assigns each repo its code and USER_STORIES.md.
4. `install-commit-hooks.ps1`.
5. Add AGENTS.md/CLAUDE.md forwarders like the sibling repos.
6. File every ticket below with `workflow ticket new`, chain them with `workflow ticket link <later> requires <earlier>`, then run `orchestrator approve` on each.
7. Print the resulting ID map.

### Ticket chain
Each ticket requires the one before it unless it notes otherwise. "MP" means the multiplayer repo and "UC" means UnityCursory; the real IDs are assigned at filing.

**MindAttic.Unity.Multiplayer**
1. **UM-T3 (chore)** Repo skeleton:
   - package.json and asmdefs
   - LICENSE (MIT), .gitattributes (LF, HOUSE-LAW-13), README stub
   - docs/BIBLE.md with the laws: server owns state; nothing above the adapter imports NGO; generator output is C# 9
2. **UM-T4 (feature) Spike:** a throwaway Unity project with one hand-written `[Rpc]` emitted through a toy generator, one NetworkVariable, a host and one client, and an IL2CPP Linux server build. It records go or fallback in an RFC. **This is the gate for UM-T11.**
3. **UM-T5 (feature)** Core: NetConfig, the session state machine, and the `ITickClock` contract, with dotnet NUnit tests.
4. **UM-T6 (feature)** Core: the join-code format (high entropy, unambiguous alphabet, checksum) and the `IJoinCodeService` contract, with tests.
5. **UM-T7 (feature)** Core: the approval payload (game version, protocol hash, player token) and its validation, with tests.
6. **UM-T8 (feature)** Runtime:
   - the NGO adapter
   - NetSession with StartHost, StartServer, JoinByAddress and Leave
   - UDP and WebSocket transport selection
   - NetworkTickClock
   - the headless bootstrap
   - PlayMode tests: host+client, then server+2 clients
7. **UM-T9 (feature)** Runtime:
   - ConnectionApproval
   - SeatRegistry, so a reconnecting player gets the same seat
   - disconnect reasons
   - PlayMode tests for mismatch rejection and reconnect
8. **UM-T10 (feature)** Runtime: NetEntity, NetSender, the per-sender rate limiter, and server-driven scene flow. The RPC stubs are hand-written in the exact shape the generator will emit.
9. **UM-T11 (feature)** Generator:
   - partial-method RPCs for all four directions
   - `[Synced]` fields
   - protocol hash
   - analyzer diagnostics
   - Generators.Tests snapshot tests
   - UM-T10's hand-written stubs replaced by generated ones, with the PlayMode tests unchanged and still green
   - Requires UM-T4 and UM-T10.
10. **UM-T12 (feature)** The rendezvous server (.NET 10 minimal API, Dockerfile) with Rendezvous.Tests.
11. **UM-T13 (feature)** RendezvousJoinCodeService (client, plus the dedicated server's self-registration) and RelayJoinCodeService. Adds `JoinByCode`, with a PlayMode test against an in-process rendezvous server.
12. **UM-T14 (feature)** A latency and loss simulation profile, plus a lossy PlayMode suite.
13. **UM-T15 (feature)** Editor: the setup wizard, the prefab validator, and the Build Dedicated Server menu.
14. **UM-T16 (feature)** The Minimal sample in Samples~.
15. **UM-T17 (chore)** CI with `ci.yml`: dotnet tests and the GameCI PlayMode run, plus a nightly Docker Compose smoke test (server plus 2 headless bot clients). This needs a Unity license secret, which Ryan supplies.
16. **UM-T18 (docs)** Hosting guide: one section per hosting profile (GenericVps, RunPod), how to pick one, and the no-port-forwarding path (Relay for home hosting). Includes checking RunPod's current UDP support. Requires UM-T23.
17. **UM-T19 (docs)** DocFX site:
   - guides: Getting Started in 10 minutes, Topologies, Attributes reference, Join codes, Reconnect and seats, Testing, Hosting, Porting a game, Troubleshooting
   - API reference generated from XML docs
   - published to GitHub Pages by `docs.yml`
   - README rewritten to README-STYLE, linking the site
18. **UM-T20 (chore)** Tag v1.0.0. Release notes.
19. **UM-T23 (feature)** Server package + hosting profiles (see Hosting). Requires UM-T15. UM-T18 and UC-T13 require it.

**UnityCursory** (UC-T3 requires UM-T10, so early porting can start before the generator lands. UC-T7 requires UM-T20, library v1.0.0; UC-T12 requires MAU-T2, MindAttic.Unity profiles; UC-T15 requires UM-T19, the DocFX site.)
1. **UC-T3 (chore)** Unity 6000.4.3f1 project:
   - URP 2D
   - manifest referencing `file:../../MindAttic.Unity.Multiplayer/unity/com.mindattic.unity.multiplayer` and Capture
   - the `Tests/Core.Tests` dotnet pattern
   - BIBLE.md that ports the CUR-LAW rules
   - .gitattributes
2. **UC-T4 (feature)** The engine-free Core model, ported from `GameState.cs`: blocks, shapes, walls, goals, circuit, votes, whistles, plus the level data schema. Includes NUnit tests.
3. **UC-T5 (feature)** Level ScriptableObjects for L1–L14 ported from `SeedLevelN`, plus SolvabilityTests ported from Cursory.Tests.
4. **UC-T6 (feature)** Server physics sim: Script mode at 30 Hz, the friction and grab joint mapping, mass/inertia/damping. PlayMode test: one cursor moves mass 1.0 but not 2.0; two cursors move 2.0.
5. **UC-T7 (feature)** Networked cursor as the player object. `[ToServer]` Move/Grab/GrabShape/GrabWall/GrabWireEnd/Release, server-authoritative body sync, interpolation.
6. **UC-T8 (feature)** Tethers, the segmented rope and cursor collision, as per-player settings.
7. **UC-T9 (feature)** Goals and win detection (block and shape), plus the circuit bulb logic, ported with CircuitTests.
8. **UC-T10 (feature)** Votes (reset and level, 2/3 quorum, early reject, 15 s timeout) and whistles.
9. **UC-T11 (feature)** Client presentation: camera pan and zoom (0.1–4), click-vs-drag thresholds, the HUD, vote overlay, level banner, pause menu and minimap, with synthesized whistle tones.
10. **UC-T12 (feature)** Front end: the profile name and color (MindAttic.Unity), Host / Join by IP / Join by code / Quit, and the join-code display for the host.
11. **UC-T13 (feature)** The dedicated server build and Docker image for the server plus rendezvous, deployed per UM-T18.
12. **UC-T14 (chore)** An end-to-end test: server plus 2 headless bots solve L2 together.
13. **UC-T15 (docs)** README to README-STYLE with screenshots, a "how it uses the library" page linked from the DocFX "Porting a game" guide, and a v1.0.0 tag.

Every commit names its own ticket (the commit-msg hook enforces this). Each ticket is one commit, on `main`.

## Verification
- **Core:** `dotnet test` on Core.Tests, Generators.Tests and Rendezvous.Tests in each repo.
- **Unity:** PlayMode runs of NetcodeIntegrationTest suites in both topologies (host+client, server+2 clients), including the lossy profile, through the Unity CLI with `-runTests -batchmode` and in CI.
- **Smoke test:** the nightly Docker Compose run, where the Linux server build and 2 bot clients connect by join code and exchange RPCs.
- **Manual:**
  - Multiplayer Play Mode with 2 or 3 virtual players: host by code, join by IP, then kill a client and reconnect to confirm it gets the same cursor back.
  - On a real VPS: a dedicated server joined from two machines.
- **Docs:** the DocFX site builds without warnings, and the Pages URL loads.
