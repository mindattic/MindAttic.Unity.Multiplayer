# Getting started in 10 minutes

By the end of this page your own scene is networked, has a main menu with Host, Join by IP and Join by code, a lobby
with a join code, connection dialogs, reconnect, and an in-game Esc menu, and a player prefab that the server moves.
You write one small script, and none of it is menu code.

## 1. Add the packages (1 minute)

Add these to the game's `Packages/manifest.json`. The paths assume the MindAttic repos sit beside your game's folder:

```json
"com.mindattic.unity.multiplayer": "file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer",
"com.mindattic.components.unity": "file:../../MindAttic.Components/unity/com.mindattic.components.unity",
"com.mindattic.unity.shared": "file:../../Unity.Shared/unity/com.mindattic.unity.shared"
```

The first is the multiplayer package. It pulls in Netcode for GameObjects 2.x and Unity Transport 2.x. The other two
are optional and give you the [drop-in front end](front-end.md): MindAttic Components draws the menus, and Unity.Shared
supplies the player's name and colour from their MindAttic profile. Leave both out if your game brings its own UI, and
the menus are not even compiled. Add `com.unity.services.multiplayer` too if you want Unity Relay join codes, which get
through home routers with no port forwarding (see [Join codes](join-codes.md)). Without it, hosting and joining by IP
still work.

## 2. Network the scene and add the menus (1 minute)

Open your scene and run **MindAttic ▸ Multiplayer ▸ Add Front End**. In one undo step it adds:

- a `NetworkManager` GameObject with Unity Transport,
- `NetBootstrap`, the one component that owns the session,
- a `NetPrefabList` with your NetworkObject prefabs,
- `NetFrontEnd`, the menus.

Without the two optional packages, use **MindAttic ▸ Multiplayer ▸ Set Up Scene** instead. It does the same without
the menus, and you drive the session from your own UI ([below](#drive-it-from-your-own-ui)).

## 3. Make a player (4 minutes)

Make a prefab with a `NetworkObject` and this script, set it as the NetworkManager's **Player Prefab**, and run
**MindAttic ▸ Multiplayer ▸ Validate Prefabs**:

```csharp
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using UnityEngine;

public partial class Player : NetEntity
{
    [ToServer(RateLimit = 20)]
    public partial void SetDirection(Vector2 direction);

    [Synced] private Vector2 _position;

    private Vector2 _direction; // server only
    private Vector2 _sent;      // owner only

    private void OnSetDirection(Vector2 direction)
    {
        _direction = Vector2.ClampMagnitude(direction, 1f); // the server never trusts input as-is
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner)
        {
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input != _sent) { _sent = input; SetDirection(input); }
        }

        if (IsServer) Position += _direction * 4f * Time.deltaTime; // replicated to everyone
        transform.position = Position;
    }
}
```

- `[ToServer(RateLimit = 20)]`: the owner calls `SetDirection(direction)`. The call is sent to the server, which runs
  `OnSetDirection`, at most 20 times a second. **The server owns the game state; clients send what they want to
  happen.** That is the one rule the whole package is built on (BIBLE UM-LAW-1).
- `[Synced] private Vector2 _position;`: the generator turns this into a `Position` property backed by a network
  variable. Only the server writes it. Every peer reads the same value, late joiners included.
- The class is `partial` because the package's source generator writes the other half when the script compiles. See
  [Attributes reference](attributes.md) for `[ToClients]`, `[ToOwner]`, `[ToClient]` and the rules the generator
  enforces.

Your scripts' assembly must reference `Unity.Multiplayer`, `Unity.Multiplayer.Core` and `Unity.Netcode.Runtime`. The
generated half of a `NetEntity` uses Netcode types; your own code never does.

## 4. Play (3 minutes)

Press **Play** and click **Host**. The lobby shows a join code (or your LAN address when no code service is set up)
and the players. Click **Start** and move with the arrow keys. **Esc** opens the in-game menu.

For a second player you need a second copy of the game:

- **Multiplayer Play Mode** (quickest): install `com.unity.multiplayer.playmode`, open
  **Window > Multiplayer > Multiplayer Play Mode**, tick **Player 2** and press Play. Host in one window. In the other,
  click **Join by IP** and type `127.0.0.1`, or click **Join by code** and type the code.
- **A build:** add the scene to the scene list and **Build And Run**. Host in the editor and join from the build, or
  the reverse.

Try the edges too. Close the joining window and rejoin within 60 seconds: you get the same player object back (see
[Reconnect and seats](reconnect-seats.md)). Join with a build of a different version: you get a "Different version"
dialog. Remove a player from the lobby: they are told why. Under **Settings**, change your name and colour; the
host's roster shows them.

## 5. Hook in your game (1 minute)

```csharp
var frontEnd = NetFrontEnd.Instance;
frontEnd.GameStarted += () => { /* load the level, show the HUD */ };
frontEnd.Left += why => { /* back at the main menu; why says why */ };
frontEnd.AddScreen("Options", s => s.AddRange("Volume", 0, 100, 80, SetVolume, true));
```

[Drop-in front end](front-end.md) covers every hook, the settings asset, the player's identity, the message for every
way a game can end, and how a dedicated server or bot using the same scene never shows a menu.

## Drive it from your own UI

The front end's buttons and your own UI make the same calls on `NetBootstrap`, the component on the NetworkManager:

| Action | Call |
|---|---|
| Host | `bootstrap.StartHost()` |
| Join by IP | `bootstrap.JoinByAddress(address, port)` |
| Host with a join code | `await bootstrap.HostWithCode()` |
| Join by code | `await bootstrap.JoinByCode(code)` |
| Leave | `bootstrap.Leave()` |

`bootstrap.Session` is the live `NetSession`. `IsActive`, `IsHost`, `State` and `PublishedCodeText` read its state.
`Started` and `Ended(Disconnection)` say when it starts and why it ended, and `JoinCodeFailed` says why a code could
not be made or used. `DisconnectText.For(disconnection)` turns an ending into the same plain words the front end
shows.

### Settings in code

The Inspector holds `NetBootstrap`'s default settings. Code (a menu, a launcher, a test) sets them with `Configure`
instead, before the session starts:

```csharp
bootstrap.Configure(new NetConfig { Topology = Topology.Server, Port = 47961, GameVersion = "1.2" });
// or change a few and keep the rest:
bootstrap.Configure(c => { c.Port = 47961; c.MaxPlayers = 4; });
```

`Configure` copies the config and validates it; `ArgumentException` lists every problem. It is refused with
`InvalidOperationException` while the session is running. `DefaultConfig` reads the current defaults back. To have
Auto Start or a headless launch use them, call it on an inactive GameObject and then activate it. The environment and
the command line (`-port`, `-transport`, `-mode`, ...) still win over both the Inspector and `Configure`, so a launch
line can always retarget a build.

## The samples

**Window > Package Manager > MindAttic Multiplayer > Samples** has two, each a whole working game:

- **Front End**: the steps above, done. A square per player in their own colour and name, with the drop-in menus. It
  needs the two optional packages.
- **Minimal**: the same arena with a small hand-made IMGUI menu that calls `NetBootstrap` directly, and no extra
  packages. Its `README.md` walks through every line, including a `[ToClients]` call.

## Where to next

- [Drop-in front end](front-end.md) for everything the menus do and every hook.
- [Topologies](topologies.md) for listen server vs. dedicated server, and how to build the Linux player.
- [Attributes reference](attributes.md) for every option on `[ToServer]` / `[ToClients]` / `[ToOwner]` /
  `[ToClient]` / `[Synced]`.
- [Join codes](join-codes.md) for Relay vs. rendezvous.
- [Hosting](hosting.md) once you need a server that outlives the editor.
