# Minimal sample: multiplayer in 10 minutes

Each player is a coloured square in an arena. The server moves the squares, and every screen shows the same thing.
A square flashes white on every screen when it hits a wall. A small menu hosts a game, joins one by IP, or hosts and
joins with a join code.

The networking is three declarations in `MinimalPlayer.cs`: one `[ToServer]` method, one `[ToClients]` method and one
`[Synced]` field. Everything else is ordinary Unity code.

| Minute | Step |
|---|---|
| 0-1 | [Import the sample](#1-import-it) |
| 1-2 | [Host a game](#2-host-a-game) |
| 2-5 | [Join it as a second player](#3-join-it-as-a-second-player) |
| 5-6 | [Try a join code](#4-try-a-join-code) |
| 6-10 | [Read the code](#5-read-the-code) |

## 1. Import it
1. Add the package to your project (see the package README) on Unity 6.
2. Open **Window > Package Manager**, select **MindAttic Multiplayer**, open the **Samples** tab and click **Import**
   next to **Minimal**.

Unity copies the sample to `Assets/Samples/MindAttic Multiplayer/<version>/Minimal/`. The copy is yours to change.

## 2. Host a game
Open `Minimal.unity` from that folder and press **Play**. Click **Host**. Your square appears; move it with the arrow
keys or WASD. Run it into a wall and it flashes.

Your game is now a **listen server**: it is the server and a player at once.

## 3. Join it as a second player
You need a second copy of the game. Use either of these:

- **Multiplayer Play Mode** (quickest): install `com.unity.multiplayer.playmode`, open
  **Window > Multiplayer > Multiplayer Play Mode**, and tick **Player 2**. Press Play. Click **Host** in one window.
  In the other, leave the address `127.0.0.1` and the port `7777`, and click **Join by IP**.
- **A build:** add `Minimal.unity` to the scene list in **File > Build Profiles**, then **Build And Run**. Host in the
  editor and join from the build, or the other way round.

Both squares move on both screens. Close the joining window and its square stays for 60 seconds. Rejoin within that
time and you get the same square back (the seat and its player object wait for you).

To join from another computer on your network, type the host's LAN address instead of `127.0.0.1`. UDP port 7777 must
be open on the host's firewall.

## 4. Try a join code
**Host with code** hosts and shows a code like `ABC-DEF-GHJ`. On the other copy, type the code (any case, with or
without dashes) and click **Join by code**. Where a code comes from:

- **Unity Relay** is the default for a listen server. It gets through home routers with no port forwarding. Install
  `com.unity.services.multiplayer` and link the project to a Unity Cloud project
  (**Edit > Project Settings > Services**). Without that, the menu shows what is missing.
- **The rendezvous service** maps a code to a server's IP:port. Use it for dedicated servers, or for hosts that players
  can reach directly. Put its address in the NetworkManager's **Net Bootstrap > Rendezvous Url**.

## 5. Read the code
The sample is five assets and three scripts:

| Asset | What it is |
|---|---|
| `Minimal.unity` | The scene: a camera, the **NetworkManager** object and the **Menu** object |
| `MinimalPlayer.prefab` | The player: a `NetworkObject` and the `MinimalPlayer` script |
| `MinimalPrefabs.asset` | A `NetPrefabList` holding the player prefab, so every peer can spawn it |
| `MinimalPlayer.cs` | The networked player |
| `MinimalMenu.cs` | The menu |
| `MinimalInput.cs` | Reads the keyboard; nothing here is about networking |
| `MindAttic.Unity.Multiplayer.Samples.Minimal.asmdef` | The sample's assembly |

### The scene
**MindAttic > Multiplayer > Set Up Scene** made the **NetworkManager** object. It holds three components:

- **NetworkManager** (Netcode for GameObjects). Its **Player Prefab** is `MinimalPlayer.prefab`, so each player who
  joins gets a square they own. **Enable Scene Management** is off because the sample has one scene. Turn it on when
  your game changes scenes through `NetSession.Scenes`, and add those scenes to the build.
- **Unity Transport**: how bytes travel. You never touch it; Net Bootstrap configures it.
- **Net Bootstrap**: the one MindAttic component a networked scene needs. It owns the `NetSession` and holds the
  settings every button uses: port 7777, UDP, join codes, the player's name and colour. Its **Prefab Lists** has
  `MinimalPrefabs.asset`. With **Auto Start** off, nothing starts until a button is pressed. A headless build (for
  example `-batchmode -nographics`) still starts as a dedicated server by itself.

The **Menu** object holds `MinimalMenu`, with its **Bootstrap** field pointing at that Net Bootstrap.

### MinimalPlayer.cs
```csharp
public partial class MinimalPlayer : NetEntity
```
`NetEntity` is the package's base class for anything networked. The class is `partial` because the package's source
generator writes its other half when the script compiles. You never see or edit that half.

```csharp
[ToServer(RateLimit = 20)]
public partial void SetDirection(Vector2 direction);
```
A **client to server** call. The owner calls `SetDirection(direction)` like any method; it is sent to the server,
which runs `OnSetDirection(direction)`. Two rules come with it:
- Only the object's owner may call it (`RequireOwnership` is on by default). Anyone else's call is refused and logged.
- At most 20 calls a second per player are let through.

You declare the method without a body; the generator writes the body that sends it. A handler can take a
`NetSender sender` last, to learn who called. Change `[ToServer]` to `[ToServer(Delivery = Delivery.Unreliable)]`
for fast, droppable updates.

```csharp
[ToClients]
public partial void Bumped();
```
A **server to every client** call. The server calls `Bumped()`; every client, the host's own player included, runs
`OnBumped()`. `[ToOwner]` and `[ToClient]` send to one client instead.

```csharp
[Synced] private Vector2 _position;
```
**Synced state.** The generator turns the field into a `Position` property backed by a network variable. Only the
server may set it; every peer reads the same value, and late joiners get it too. Always use `Position`, never
`_position`; the compiler flags the field if you do. Add `OnChanged = nameof(Handler)` to run code when it changes.

```csharp
protected override void OnSpawned()
```
Runs on every peer once the object exists on the network. Here the server picks a starting spot from the owner's seat.
Override `OnSpawned` and `OnDespawned`, not Netcode's `OnNetworkSpawn`.

```csharp
private void OnSetDirection(Vector2 direction)
{
    _direction = Vector2.ClampMagnitude(direction, 1f);
}
```
The `[ToServer]` handler, named `On` plus the method name. It runs on the server only. The server never trusts what a
client sends: a vector longer than 1 would be a faster player, so it is clamped. This is the rule the whole package is
built on. **The server owns the game state; clients send what they want to happen.**

```csharp
private void OnBumped() { _flashUntil = Time.time + 0.2f; }
```
The `[ToClients]` handler. It runs on every client.

`Update` runs on every peer and does three jobs:
1. **The owner** reads the keyboard and calls `SetDirection` only when the direction changes, not every frame.
2. **The server** moves the square by `_direction`, clamps it inside the arena, and writes `Position`, which
   replicates. On the frame it first hits a wall it calls `Bumped()`.
3. **Every peer** moves the transform to `Position`.

`OnGUI` draws the square with IMGUI, so the sample works in any render pipeline without sprites or materials. A game
would use a renderer and set `transform.position` from `Position`, as `Update` already does.

### MinimalMenu.cs
`MinimalMenu` calls the Net Bootstrap; each button is one line:

| Button | Call | What happens |
|---|---|---|
| Host | `bootstrap.StartHost()` | This game becomes the server and a player |
| Join by IP | `bootstrap.JoinByAddress(address, port)` | Connects to that host |
| Host with code | `await bootstrap.HostWithCode()` | Hosts, then publishes a code (`session.PublishedCodeText`) |
| Join by code | `await bootstrap.JoinByCode(code)` | Resolves the code, then connects |
| Leave | `bootstrap.Leave()` | Ends the session |

`bootstrap.Session` is the live `NetSession`. The menu reads `IsActive`, `IsHost`, `State` and `PublishedCodeText`
from it, and listens to two events:
- `Ended` reports why a session ended, such as a refused connection, a kick or a lost server.
- `JoinCodeFailed` reports why a code could not be made or used.

The join-code calls are `async` because they talk to Relay or the rendezvous service first. `Await` disables the
buttons while one runs and shows any error.

## Make it your own game
1. In your own scene, run **MindAttic > Multiplayer > Set Up Scene**. It adds the NetworkManager, Unity Transport and
   Net Bootstrap, and a prefab list for your network prefabs.
2. Give your player prefab a `NetworkObject` and a `partial` class deriving from `NetEntity`. Set it as the
   NetworkManager's **Player Prefab**.
3. Mark methods `[ToServer]` / `[ToClients]` / `[ToOwner]` / `[ToClient]`, mark state `[Synced]`, and write the
   `On<Name>` handlers.
4. Run **MindAttic > Multiplayer > Validate Prefabs**, then call the Net Bootstrap from your own UI.

Your scripts' assembly must reference `MindAttic.Unity.Multiplayer`, `MindAttic.Unity.Multiplayer.Core` and
`Unity.Netcode.Runtime`, as this sample's asmdef does. The generated half uses Netcode types; your code does not.
