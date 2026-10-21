# Getting started in 10 minutes

This walks through the package's own Minimal sample: a square per player, moved by the server, in an 8x8
arena. It is the fastest way to see a host, a client, a join code and the attribute API working together. The
sample's own line-by-line README (`unity/com.mindattic.unity.multiplayer/Samples~/Minimal/README.md`) goes
deeper into each script; this page is the guided tour.

## 1. Add the package (1 minute)

Add it to the game's `Packages/manifest.json` as a local package:

```json
"com.mindattic.unity.multiplayer": "file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer"
```

It pulls in Netcode for GameObjects 2.x and Unity Transport 2.x. Install `com.unity.services.multiplayer` too if
you want Unity Relay join codes (see [Join codes](join-codes.md)) — without it the package still works for
hosting and joining by IP.

## 2. Import the Minimal sample (1 minute)

In **Window > Package Manager**, select **MindAttic Multiplayer**, open the **Samples** tab and import
**Minimal**. Unity copies it to `Assets/Samples/MindAttic Multiplayer/<version>/Minimal/`; the copy is yours to
change.

## 3. Host a game (1 minute)

Open `Minimal.unity` and press **Play**, then click **Host**. Your square appears; move it with the arrow keys
or WASD. Run it into a wall and it flashes.

Your game is now a **listen server**: it is the server and a player at once. See [Topologies](topologies.md)
for how the same code also runs as a dedicated server.

## 4. Join as a second player (3 minutes)

You need a second copy of the game:

- **Multiplayer Play Mode** (quickest): install `com.unity.multiplayer.playmode`, open
  **Window > Multiplayer > Multiplayer Play Mode**, tick **Player 2**, press Play. Host in one window; in the
  other, leave the address `127.0.0.1` and port `7777` and click **Join by IP**.
- **A build:** add `Minimal.unity` to the scene list and **Build And Run**. Host in the editor and join from the
  build, or the reverse.

Both squares move on both screens. Close the joining window; its square stays for 60 seconds. Rejoin within
that time and you get the same square back — see [Reconnect and seats](reconnect-seats.md).

## 5. Try a join code (1 minute)

Click **Host with code**: it shows a code like `ABC-DEF-GHJ`. On the other copy, type the code (any case, with
or without dashes) and click **Join by code**. A listen server's default is Unity Relay, which needs
`com.unity.services.multiplayer` installed and the project linked to Unity Cloud
(**Edit > Project Settings > Services**); without that the menu reports what's missing. See
[Join codes](join-codes.md) for the rendezvous alternative dedicated servers use.

## 6. Read the code (3 minutes)

The whole networked surface of `MinimalPlayer.cs` is three declarations:

```csharp
public partial class MinimalPlayer : NetEntity
{
    [ToServer(RateLimit = 20)]
    public partial void SetDirection(Vector2 direction);

    [ToClients]
    public partial void Bumped();

    [Synced] private Vector2 _position;

    private void OnSetDirection(Vector2 direction, NetSender sender)
    {
        _direction = Vector2.ClampMagnitude(direction, 1f);
    }

    private void OnBumped() { _flashUntil = Time.time + 0.2f; }
}
```

- `[ToServer(RateLimit = 20)]` — the owner calls `SetDirection(direction)`; it is sent to the server, which runs
  `OnSetDirection`, at most 20 times a second. The server never trusts the input as-is: it clamps the vector to
  length 1 before using it. **The server owns the game state; clients send what they want to happen** — the one
  rule the whole package is built on (BIBLE UM-LAW-1).
- `[ToClients]` — the server calls `Bumped()`; every client (the host's own player included) runs `OnBumped()`.
- `[Synced] private Vector2 _position;` — the generator turns this into a `Position` property backed by a
  network variable. Only the server writes it; every peer reads the same value, including late joiners.

`NetEntity` is the package's base class for anything networked; the class is `partial` because the package's
source generator writes the other half (the RPC plumbing) when the script compiles — see
[Attributes reference](attributes.md) for the full attribute set and the rules the generator enforces.

`MinimalMenu` drives the session through `NetBootstrap`, the one MindAttic component a networked scene needs:

| Button | Call |
|---|---|
| Host | `bootstrap.StartHost()` |
| Join by IP | `bootstrap.JoinByAddress(address, port)` |
| Host with code | `await bootstrap.HostWithCode()` |
| Join by code | `await bootstrap.JoinByCode(code)` |
| Leave | `bootstrap.Leave()` |

`bootstrap.Session` is the live `NetSession`: `IsActive`, `IsHost`, `State` and `PublishedCodeText` read its
state, and `Ended` / `JoinCodeFailed` report why a session ended or a code failed.

### Settings in code

The Inspector holds `NetBootstrap`'s default settings. Code (a menu, a launcher, a test) sets them with
`Configure` instead, before the session starts:

```csharp
bootstrap.Configure(new NetConfig { Topology = Topology.Server, Port = 47961, GameVersion = "1.2" });
// or change a few and keep the rest:
bootstrap.Configure(c => { c.Port = 47961; c.MaxPlayers = 4; });
```

`Configure` copies the config, validates it (`ArgumentException` lists every problem) and is refused with
`InvalidOperationException` while the session is running; `DefaultConfig` reads the current defaults back. To
have Auto Start or a headless launch use them, call it on an inactive GameObject and then activate it. The
environment and the command line (`-port`, `-transport`, `-mode`, …) still win over both the Inspector and
`Configure`, so a launch line can always retarget a build.

## Make it your own game

1. In your own scene, run **MindAttic > Multiplayer > Set Up Scene**. It adds a NetworkManager with Unity
   Transport and a `NetBootstrap`, and assigns a `NetPrefabList` (yours, or a new one filled with your
   NetworkObject prefabs).
2. Give your player prefab a `NetworkObject` and a `partial class` deriving from `NetEntity`; set it as the
   NetworkManager's Player Prefab.
3. Mark methods `[ToServer]` / `[ToClients]` / `[ToOwner]` / `[ToClient]`, mark state `[Synced]`, and write the
   `On<Name>` handlers.
4. Run **MindAttic > Multiplayer > Validate Prefabs**, then call the `NetBootstrap` from your own UI.

Your scripts' assembly must reference `Unity.Multiplayer`, `Unity.Multiplayer.Core` and `Unity.Netcode.Runtime`.
The generated half of a `NetEntity` uses Netcode types; your own code never needs to.

## Where to next

- [Topologies](topologies.md) for listen server vs. dedicated server and how to build the Linux player.
- [Attributes reference](attributes.md) for every option on `[ToServer]` / `[ToClients]` / `[ToOwner]` /
  `[ToClient]` / `[Synced]`.
- [Join codes](join-codes.md) for Relay vs. rendezvous.
- [Hosting](hosting.md) once you need a server that outlives the editor.
