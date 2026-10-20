---
codex: 1
project: MindAttic.Unity.Multiplayer
code: UM
layer: bible
status: living
updated: 2026-10-20
---

# MindAttic.Unity.Multiplayer — Project Bible
> Single source of truth for what MindAttic.Unity.Multiplayer IS, is NOT, and the rules that keep it coherent.
> README says how to use it; [RFC 0001](rfc/0001-architecture.md) holds the design and build order; this says how to think about the system.

Status legend: ✅ done (verified by a test/build) · 🟡 partial · ⬜ planned · `living`.

## 1. The one sentence {#UM-§1}
MindAttic.Unity.Multiplayer is a Unity 6 package that gives any MindAttic game server-authoritative multiplayer, written as attribute-decorated partial methods and fields, in either a listen-server or a dedicated-server topology, joined by IP:port or a join code.

## 2. The product promise {#UM-§2}
- A game adds one local package and one `NetBootstrap` component, marks methods `[ToServer]` / `[ToClients]` / `[ToOwner]` / `[ToClient]` and fields `[Synced]`, and is networked.
- The same game code runs as host, dedicated server or client; the topology is configuration, not code.
- A player who drops and reconnects gets the same seat back.

## 3. What it is NOT {#UM-§3}
- NOT a netcode engine. Netcode for GameObjects 2.x and Unity Transport 2.x do the transport and replication; this package is the API and policy on top.
- NOT client-authoritative. There is no client-side prediction of shared state; see [UM-LAW-1](#UM-LAW-1).
- NOT reflection-based. RPC plumbing is generated (or, as a fallback, hand-written in the generator's exact shape), never discovered at runtime.

## 4. Architecture canon {#UM-§4}
The full layout lives in [RFC 0001 § Architecture](rfc/0001-architecture.md#architecture) and is not repeated here ([UM-LAW-4](#UM-LAW-4)). The assembly boundary that the laws below depend on:

```
MindAttic.Unity.Multiplayer.Core     Runtime/Core   noEngineReferences; no UnityEngine, no Unity.Netcode
        ▲
MindAttic.Unity.Multiplayer          Runtime        the NGO adapter: the only runtime assembly that imports Unity.Netcode
        ▲
MindAttic.Unity.Multiplayer.Editor   Editor         editor tooling
MindAttic.Unity.Multiplayer.Tests    Tests/Runtime  PlayMode NetcodeIntegrationTest suites
game code                            (consumer)     talks to NetSession / NetEntity / attributes only
```

## 5. The Laws {#UM-§5}

MindAttic.Unity.Multiplayer **inherits the org-wide house rules** in
[`../../MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md). Of note here: [HOUSE-LAW-1] (whole-number versioning: the package is `0.0.0` until the v1.0.0 tag in UM-T20), [HOUSE-LAW-8] (done is verified, not asserted), [HOUSE-LAW-13] (LF-only, enforced by the root `.gitattributes`).

Project-specific laws:

### UM-LAW-1 — The server owns state {#UM-LAW-1}
Every piece of shared game state is written only on the server. Clients send intent (`[ToServer]` calls); the server validates it, applies it and replicates the result (`[Synced]` fields, `[ToClients]` / `[ToOwner]` / `[ToClient]` calls). A client never asserts an authoritative value, and the generator and analyzers reject client writes to `[Synced]` state.

### UM-LAW-2 — Nothing above the NGO adapter imports Unity.Netcode {#UM-LAW-2}
`Unity.Netcode` (and `Unity.Networking.Transport`) are referenced only by the adapter assembly `MindAttic.Unity.Multiplayer` and its Editor and Tests companions. `MindAttic.Unity.Multiplayer.Core` is engine-free (`noEngineReferences`), and game code sees only MindAttic types (`NetSession`, `NetEntity`, `NetSender`, the attributes). Generated code is the one sanctioned exception: it is emitted into the game's assembly by the generator and is not hand-written there. Swapping the backend means rewriting the adapter, nothing else.

### UM-LAW-3 — Generator output is C# 9 {#UM-LAW-3}
The source generator emits only C# 9 constructs (extended partial methods, no file-scoped namespaces, no record structs, no `required`, no raw string literals), so its output compiles on every Unity 6 compiler. It targets netstandard2.0 against Microsoft.CodeAnalysis.CSharp 4.3.x.

### UM-LAW-4 — One home per fact {#UM-LAW-4}
Every fact lives in exactly one place and everything else links to it. Dependency versions live in `package.json`; the design and ticket order live in RFC 0001; laws live here; story status lives in [USER_STORIES.md](USER_STORIES.md). A second copy is a bug.

## 6. Verified state {#UM-§6}
- ✅ Package skeleton (UM-T3): `package.json`, the four asmdefs, LICENSE, `.gitattributes`.
- ✅ Generator approach proven (UM-T4, [RFC 0002](rfc/0002-generator-spike.md)): a netstandard2.0 / Roslyn 4.3.1 `IIncrementalGenerator` emits `[Rpc(SendTo.Server)]` stubs and `NetworkVariable`s from C# 9 partial methods and fields; NGO ILPP processes them; host+client and an IL2CPP Linux Dedicated Server run them. Decision: GO, no fallback.
- ✅ Core session contracts (UM-T5): `NetConfig` (topology, address, port, transport, tick rate 30, max players, game version, `Validate`), `SessionStateMachine` (Host/Server `Idle → Starting → Running → Stopping → Idle`; Client `Idle → Connecting → Connected → Disconnected` with a `DisconnectReason`), and the `ITickClock` contract with `ManualTickClock`. Verified by `dotnet test tests/Core.Tests` (NetConfigTests, SessionStateMachineTests, ManualTickClockTests); `tests/Core.Build` compiles the same sources as netstandard2.1 / C# 9.
- ✅ Join codes (UM-T6): `JoinCode` / `JoinCodeFormat` (10 data characters = 50 bits from a 32-symbol alphabet without 0, O, 1 or I, plus a Damm check character over GF(32) that catches every single substitution and every neighbour swap; parse ignores case and separators and corrects one look-alike the check confirms), `JoinTarget` (endpoint or relay allocation) and the `IJoinCodeService` contract (`PublishAsync` → code, `ResolveAsync` → target or null, idempotent `RevokeAsync`) with `InMemoryJoinCodeService`. Verified by `dotnet test tests/Core.Tests` (JoinCodeTests, JoinTargetTests, InMemoryJoinCodeServiceTests).

## 7. Active frontier {#UM-§7}
The ticket chain in [RFC 0001 § Ticket chain](rfc/0001-architecture.md#ticket-chain). The UM-T4 gate is passed (GO) and UM-T5 and UM-T6 are done; next is UM-T7.

## 8. Glossary {#UM-§8}
- **NGO** — Netcode for GameObjects, Unity's GameObject netcode package.
- **Adapter** — the `MindAttic.Unity.Multiplayer` assembly; the only runtime code that touches NGO.
- **Listen server / host** — one player's game is also the server.
- **Dedicated server** — a headless server build everyone joins.
- **Join code** — a short, checksummed code resolved to a relay allocation or an IP:port.
- **Seat** — a player's slot on the server, kept across reconnects.
