# Porting a game

A guide for moving an existing Unity game — single-player, or already networked some other way — onto
`Unity.Multiplayer`. The order below is the one that keeps the game running (and testable) at every step, not
one everything-at-once rewrite.

## 1. Separate state from presentation, if it isn't already

The package's one rule is **the server owns shared state; clients send what they want to happen**
(BIBLE UM-LAW-1). Before touching any networking code, it's worth knowing which of the game's fields are:

- **Shared state** everyone must agree on (position, health, score, whose turn it is) — this becomes
  `[Synced]` fields, written only on the server.
- **Player intent** (move this way, grab that thing, press this button) — this becomes `[ToServer]` calls.
- **Purely local presentation** (camera shake, a particle effect, UI the player's own client drives) — this
  stays ordinary, un-networked code.

A single-player game that already separates "the simulation" from "the view" (a `GameState` class a
`MonoBehaviour` reads from, say) usually only needs its simulation fields marked `[Synced]` and its input calls
turned into `[ToServer]` methods — the simulation code itself barely changes. A game whose state and
presentation are the same `MonoBehaviour` fields benefits from pulling the state out first, as its own
exercise, before adding networking on top of it.

## 2. Make the player object a `NetEntity`

```csharp
// before
public class PlayerController : MonoBehaviour { ... }

// after
public partial class PlayerController : NetEntity { ... }
```

`NetEntity` requires a `NetworkObject` on the same GameObject (**MindAttic ▸ Multiplayer ▸ Validate Prefabs**
checks this) and the class itself, and every class it derives from, must not be generic
([MAMP005](attributes.md#generator-diagnostics)). Override `OnSpawned` / `OnDespawned` instead of any
`OnNetworkSpawn`/`OnNetworkDespawn` you had — `NetEntity` seals those so its own `[Synced]` wiring always runs.

## 3. Turn input into `[ToServer]` calls

Wherever the old code read input and changed shared state directly on every client (common in a prototype, or
a game that was never networked at all), split it in two: the client sends intent, the server applies it.

```csharp
// before (single-player / client-authoritative)
void Update()
{
    if (Input.GetKeyDown(KeyCode.Space)) Jump();
}
void Jump() { _velocity.y = jumpForce; }

// after
void Update()
{
    if (Input.GetKeyDown(KeyCode.Space)) Jump(); // owner calls the partial method
}

[ToServer]
public partial void Jump();

private void OnJump(NetSender sender)
{
    _velocity.y = jumpForce; // only the server actually applies it
}
```

See [Attributes reference](attributes.md) for `RateLimit`, `Burst` and `RequireOwnership` — input that fires
every frame (movement) usually wants a rate limit; a once-per-match action (a vote, a ready check) usually
wants `RequireOwnership = false` so any connected player can call it.

## 4. Turn shared fields into `[Synced]`

```csharp
// before
public Vector3 Position;

// after
[Synced] private Vector3 _position;
// use the generated `Position` property everywhere; never the field
```

The generator rejects a direct write to the field from anywhere ([MAMP006](attributes.md#generator-diagnostics))
and warns on a direct read ([MAMP007](attributes.md#generator-diagnostics)), so a search-and-replace that
misses a spot is a build error or warning, not a silent desync. Add `OnChanged = nameof(Handler)` wherever the
old code ran logic on the value changing (an animator trigger, a UI refresh).

## 5. Wire up the scene

Run **MindAttic ▸ Multiplayer ▸ Set Up Scene** once per scene that needs networking: it adds a NetworkManager
with Unity Transport and a `NetBootstrap`, and assigns a `NetPrefabList` of the scene's NetworkObject prefabs.
Then either replace the game's multiplayer menus with the [drop-in front end](front-end.md) (**MindAttic ▸
Multiplayer ▸ Add Front End**: host, join by IP or code, lobby, connection dialogs, reconnect and an Esc menu, with
the game's own options added through its hooks), or point the game's existing menu/lobby UI at the `NetBootstrap`
instance instead of whatever custom connect/host code it had — see
[Getting started](getting-started.md#drive-it-from-your-own-ui) for the exact calls (`StartHost`, `JoinByAddress`,
`HostWithCode`, `JoinByCode`, `Leave`) and the events to show connection state and errors (`Ended`,
`JoinCodeFailed`, and `DisconnectText` for the words).

If the game changes scenes while a session is live, turn on **Enable Scene Management** on the NetworkManager
and drive it through `NetSession.Scenes` (`Load`/`Unload`), not Unity's own `SceneManager` — only the server
may change scenes, and `NetSceneFlow` is what keeps late joiners synchronized to whatever scene is already
loaded.

## 6. Carry over anything that needs the same player back after a reconnect

If the old game already had a concept of "whose character is this" (a save slot, a player index), map it onto
the package's player token (`PlayerIdentity`, kept in `PlayerPrefs` by `NetBootstrap`'s **Remember Player**
option) so a dropped player reconnecting gets the same seat and the same player object back rather than a fresh
one. See [Reconnect and seats](reconnect-seats.md).

## 7. Replace any existing netcode transport last

If the game was already built on raw Netcode for GameObjects (custom RPCs, hand-rolled `NetworkVariable`s),
port one entity at a time: leave the untouched ones on their existing NGO code while a given `NetEntity`
subclass moves to attributes, since both can coexist on the same `NetworkManager` during the migration. Once
every networked type is a `NetEntity`, the game never references `Unity.Netcode` directly again outside the
adapter it's already depending on (BIBLE UM-LAW-2) — any custom rate limiting, approval or reconnect logic the
old code had can be deleted in favor of the package's own (see
[Reconnect and seats](reconnect-seats.md#connection-approval)).

## 8. Test as you go, then package for hosting

Write a small PlayMode suite against the game's own entities on the package's test kit (`NetSessionTest`), the
same way `tests/MinimalSample/Tests` does against `MinimalPlayer` — see [Testing](testing.md#the-test-kit). Once
the game plays correctly in a listen server, **MindAttic ▸ Multiplayer ▸ Build Dedicated Server (Linux)** and
**Build Server Package** get it onto a real host; see [Topologies](topologies.md) and [Hosting](hosting.md).

## A worked example

MindAttic's own co-op puzzle game, Cursory, is being ported from an ASP.NET Blazor Server + SignalR prototype
onto this package (tracked as the UnityCursory repo's UC-T3 through UC-T15 tickets; see
[RFC 0001 § UnityCursory](https://github.com/mindattic/Unity.Multiplayer/blob/main/docs/rfc/0001-architecture.md#unitycursory-public)
for the full mapping from the old simulation to this package's `[Synced]`/`[ToServer]` surface). That repo's own
README links back to this guide once the port lands.
