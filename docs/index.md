---
_disableToc: true
---

# Unity.Multiplayer

> **v1.0.0** — first stable release. See the [changelog](https://github.com/mindattic/Unity.Multiplayer/blob/main/CHANGELOG.md)
> and [all releases](https://github.com/mindattic/Unity.Multiplayer/releases).

Reusable Unity 6 multiplayer for MindAttic games: attribute-decorated RPCs, a listen server or a dedicated
server, and joining by IP:port or a join code.

```csharp
public partial class MinimalPlayer : NetEntity
{
    [ToServer(RateLimit = 20)]
    public partial void SetDirection(Vector2 direction);

    [ToClients]
    public partial void Bumped();

    [Synced] private Vector2 _position;

    private void OnSetDirection(Vector2 direction) => _direction = Vector2.ClampMagnitude(direction, 1f);
    private void OnBumped() => _flashUntil = Time.time + 0.2f;
}
```

That is the whole networked surface of the Minimal sample: one client-to-server
call, one server-to-everyone call and one server-owned field. The package's source generator writes the rest.

## Start here

- **[Getting started in 10 minutes](articles/getting-started.md)** — network your own scene, add the menus with one
  click, write a player, host and join.
- **[Drop-in front end](articles/front-end.md)** — `NetFrontEnd`: main menu, lobby with join code, connection
  dialogs, reconnect and an in-game Esc menu from one component, with hooks for the game's own options.
- **[Topologies](articles/topologies.md)** — listen server vs. dedicated server, and how the same game code runs
  as either.
- **[Attributes reference](articles/attributes.md)** — `[ToServer]`, `[ToClients]`, `[ToOwner]`, `[ToClient]`,
  `[Synced]`, and the rules the generator and analyzers enforce.
- **[Join codes](articles/join-codes.md)** — Unity Relay vs. the self-hosted rendezvous service, and which one a
  session picks by default.
- **[Reconnect and seats](articles/reconnect-seats.md)** — how a dropped player gets the same seat and player
  object back.
- **[Testing](articles/testing.md)** — the test suites, what each one covers, and how to add your own.
- **[Hosting](articles/hosting.md)** — the full provider table: pick a path, read one page per provider.
- **[Porting a game](articles/porting.md)** — moving an existing game onto `NetEntity` / `NetSession`.
- **[Troubleshooting](articles/troubleshooting.md)** — the errors and symptoms this package produces most, and
  what causes them.

## API reference

The **[API reference](xref:Unity.Multiplayer.Core)** is generated from this repository's XML doc comments
for `Unity.Multiplayer.Core` — the engine-free assembly that holds the attributes, `NetConfig`, `JoinCode`,
seats, connection approval and the rendezvous client: the part of the surface a game calls directly. `NetSession`,
`NetEntity`, `NetBootstrap` and the editor commands live in the NGO adapter and Editor assemblies, which reference
`UnityEngine` / `Unity.Netcode` / `UnityEditor` and so aren't reflected here; they're documented with full
signatures and examples in the guides above, in particular [Getting started](articles/getting-started.md) and
[Topologies](articles/topologies.md).

## Project links

- [GitHub repository](https://github.com/mindattic/Unity.Multiplayer)
- [docs/BIBLE.md](https://github.com/mindattic/Unity.Multiplayer/blob/main/docs/BIBLE.md) — the project's rules
- [RFC 0001](https://github.com/mindattic/Unity.Multiplayer/blob/main/docs/rfc/0001-architecture.md) — the design
  and build order
