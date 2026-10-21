# Attributes reference

Every networked class is a `partial class` deriving from `NetEntity` (the base type lives in the adapter
assembly, `Unity.Multiplayer`; see [Getting started](getting-started.md)). Five attributes, all in the
[`Unity.Multiplayer.Core`](xref:Unity.Multiplayer.Core) API reference, describe its whole networked surface;
the package's source generator writes the RPC plumbing (the sending body and the NGO stub) for every attributed
member when the script compiles, and a Roslyn analyzer rejects shapes that can't work before the build even
finishes.

```csharp
public partial class CursorAvatar : NetEntity
{
    [ToServer(Delivery = Delivery.Unreliable, RateLimit = 30)]
    public partial void Move(Vector2 worldPos);                 // call site: Move(p)

    [ToClients]
    public partial void Bumped();

    [ToOwner]
    public partial void Denied(string reason);

    [ToClient]
    public partial void Whisper(ulong target, string text);

    [Synced(OnChanged = nameof(OnMassChanged))]
    private float _mass = 1f;
}
```

## [ToServer] — client calls the server

Call it anywhere on the owning client; the server runs `On<Name>(<same parameters>[, NetSender sender])`.

| Property | Default | What it does |
|---|---|---|
| `Delivery` | `Reliable` | `Reliable` arrives in order with the object's other reliable RPCs; `Unreliable` may drop or arrive out of order — for high-rate state like a cursor position, where the next call supersedes this one. |
| `RateLimit` | `0` (no limit) | Most calls per second the server accepts from one sender (a token bucket refilled at this rate). A call over the limit is dropped and reported through [`NetSession.RpcRejected`](topologies.md), never handled. |
| `Burst` | `0` (= `RateLimit`, one second's worth) | How many calls a sender may make back to back before `RateLimit` applies — the bucket's size. |
| `RequireOwnership` | `true` | `true`: only the entity's owner may call it; anyone else's call is dropped. `false`: any connected client may call it (a shared button, a vote). |

```csharp
[ToServer(RateLimit = 20)]
public partial void SetDirection(Vector2 direction);

private void OnSetDirection(Vector2 direction, NetSender sender)
{
    _direction = Vector2.ClampMagnitude(direction, 1f);
}
```

The handler may leave out the trailing `NetSender sender` parameter when it doesn't need the caller's identity.
When it's kept, `sender` converts implicitly to its `ulong` client id, so a handler can answer whoever called it
through a `[ToClient]` method: `Denied(sender, "Not your turn")`. `NetSender` also carries `SeatIndex` and
`DisplayName` (see [Reconnect and seats](reconnect-seats.md)) and `IsServer` (true for the host's own player
calling itself). Before the handler runs, the server checks ownership and the rate; a call that fails either is
never handled.

## [ToClients] — server calls every client

Only the server may call it. Every client, the host's own player included, runs `On<Name>(<same parameters>)`.

| Property | Default | What it does |
|---|---|---|
| `Delivery` | `Reliable` | Same meaning as `[ToServer]`'s. |

```csharp
[ToClients]
public partial void Bumped();

private void OnBumped() { _flashUntil = Time.time + 0.2f; }
```

## [ToOwner] — server calls the entity's owner

Only the server may call it; the owning client runs `On<Name>(<same parameters>)`. Same `Delivery` option as
`[ToClients]`. Use it for feedback that only matters to one player (a rejected action, a private prompt).

## [ToClient] — server calls one specific client

Only the server may call it. Its **first parameter must be a `ulong` target client id** — the generator rejects
any other shape (MAMP010); that client runs `On<Name>(<the remaining parameters>)`. Same `Delivery` option.
Pass a `NetSender` directly as the target; it converts implicitly to the `ulong` the attribute requires.

```csharp
[ToClient]
public partial void Whisper(ulong target, string text);

// called as: Whisper(sender, "only you see this")
```

## [Synced] — server-owned replicated state

Marks a **field** (never a method) as replicated state. `[Synced] float _mass;` becomes a `Mass` property
backed by a network variable: the server writes it, every client reads the same value, and a late joiner gets
the current value as soon as it spawns.

| Property | Default | What it does |
|---|---|---|
| `OnChanged` | `null` | `nameof(Handler)` of a `void Handler(T previous, T current)` method on the same class, called on every peer (server included) whenever the value changes. |

```csharp
[Synced(OnChanged = nameof(OnMassChanged))]
private float _mass = 1f;

private void OnMassChanged(float previous, float current) { /* react to the change */ }
```

Rules the generator and analyzer enforce:

- **Always use the generated property (`Mass`), never the field (`_mass`).** Reading the field directly is a
  warning (MAMP007); writing it directly is an error (MAMP006) — the compiler catches a client trying to assert
  authoritative state before it ever runs (BIBLE UM-LAW-1).
- A constant field initializer becomes the network variable's first value; a non-constant initializer is
  rejected (MAMP013).
- The field must not be `static`, `readonly` or `const`, and its generated property name must not collide with
  an existing member (MAMP012).

To find an object itself by a name the game chooses (a level's block id, `"door:A"`), not by spawn order, give its
prefab a `NetKey` and spawn it with `session.Objects.Spawn(prefab, key)`; every peer then looks it up with
`session.Objects.TryGet(key, out T)`. See [Networked objects by key](objects.md).

## How a call is handled, and what the generator writes

For `[ToServer]`/`[ToClients]`/`[ToOwner]`/`[ToClient]`, the method you declare is the **send side** — you call
it like any method and the generator-written body actually sends it (`MoveServerRpc`, `...ClientsRpc`,
`...OwnerRpc`, `...ClientRpc` underneath, each carrying the attribute's `Delivery`). A host calling a
`[ToServer]` method on itself runs the handler locally with the host as the sender. Every RPC and synced
signature in an assembly is hashed into one `[assembly: NetProtocolAttribute]` (protocol hash); a connecting
client whose assembly doesn't match is rejected at approval before any gameplay code runs — see
[Reconnect and seats](reconnect-seats.md#connection-approval).

## Generator diagnostics

The analyzer reports these at compile time (`MAMP` = MindAttic MultiPlayer), so a mistake here is a build error
or warning, not a runtime surprise:

| ID | Severity | Means |
|---|---|---|
| MAMP001 | Error | An RPC method (or its type) isn't `partial`, or already has a body. |
| MAMP002 | Error | An RPC method has no matching `On<Name>` handler. |
| MAMP003 | Error | An RPC parameter's type can't be sent. |
| MAMP004 | Error | A `[Synced]` field's type can't be a network variable. |
| MAMP005 | Error | A `NetEntity` subclass is generic (NGO can't give it stable RPC ids). |
| MAMP006 | Error | Code writes a `[Synced]` field directly instead of its generated property. |
| MAMP007 | Warning | Code reads a `[Synced]` field directly instead of its generated property. |
| MAMP008 | Error | An RPC or `[Synced]` attribute sits on a type that isn't a `NetEntity`. |
| MAMP009 | Error | An RPC method's shape isn't allowed (non-`void`, `static`, generic, `ref`/`out`/`params`, more than one direction attribute). |
| MAMP010 | Error | A `[ToClient]` method's first parameter isn't the `ulong` target client id. |
| MAMP011 | Error | `[Synced(OnChanged = ...)]` names no matching `void Handler(T previous, T current)`. |
| MAMP012 | Error | A `[Synced]` field is `static`/`readonly`/`const`, or its property name is already taken. |
| MAMP013 | Error | A `[Synced]` field's initializer isn't a compile-time constant. |

See [Troubleshooting](troubleshooting.md) for what to do when one of these fires, and
[Testing](testing.md#generatorstests) for how `Generators.Tests` pins one test per diagnostic.
