# Getting started: a new multiplayer game in minutes

By the end of this page you have a brand-new Unity project that is a working multiplayer game. It has a main menu with
Host, Join by IP and Join by code, a lobby with a join code and the roster, reconnect, an in-game menu, players drawn in
their own name and colour, objects found by key, a restart vote, a bot, EditMode and PlayMode tests, a dedicated server
running on your PC, and an end-to-end smoke test with two bots. You write no code to get there; then you replace the
template's game with yours.

It uses the [kit](kit.md): Unity.Multiplayer (this package), MindAttic Components (the menus) and Unity.Shared (the
player's profile). Every command below can be pasted as is.

## What you need

- **Unity 6000.4.3f1**, with **Linux Build Support (Mono)** if you want to run servers and bots in Docker.
- **Git**, and the **.NET 10 SDK** for the `mp-host` tool.
- **Docker** (Docker Desktop on Windows) for the local server and the smoke test. Not needed to play in the editor.

## 1. Get the kit (1 minute)

The three packages are used by `file:` path, so clone their repos side by side. Your game goes in the same folder:

```powershell
mkdir C:\Games; cd C:\Games
git clone https://github.com/mindattic/Unity.Multiplayer
git clone https://github.com/mindattic/MindAttic.Components
git clone https://github.com/mindattic/Unity.Shared
```

## 2. Make a Unity project beside them (2 minutes)

In Unity Hub, click **New project**, pick **Unity 6000.4.3f1** and any template (**Universal 2D** is a good start), name
it `SpaceCrates` and put it in `C:\Games`. Or from a terminal:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe" -batchmode -quit -createProject C:\Games\SpaceCrates
```

Open `C:\Games\SpaceCrates\Packages\manifest.json`. Add the multiplayer package at the top of `"dependencies"`, and
list Netcode for GameObjects under `"testables"` after the `"dependencies"` block (add the `"testables"` array if the
file has none):

```json
{
  "dependencies": {
    "com.mindattic.unity.multiplayer": "file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer",
    ...
  },
  "testables": [ "com.unity.netcode.gameobjects" ]
}
```

The package pulls in Netcode for GameObjects 2.x and Unity Transport 2.x. The `testables` entry is needed for the
package's test kit (`Unity.Multiplayer.Testing`), which builds on NGO's test helpers. Without it, that assembly does not
compile, and batch mode refuses to run. Open the project in the editor.

## 3. Run the New Game wizard (1 minute)

Click **MindAttic ▸ Multiplayer ▸ New Game…**.

1. **Packages.** The window checks `Packages/manifest.json` and lists exactly the lines it lacks: MindAttic Components,
   Unity.Shared (optional), the Unity Test Framework if the project template left it out, and
   `com.unity.netcode.gameobjects` under `"testables"` if you skipped it. Click **Add missing lines to
   Packages/manifest.json**. It writes the `file:` paths to the repos it finds
   beside Unity.Multiplayer, and Unity imports them. If a repo is somewhere else, the window shows the line with the
   documented path for you to fix by hand, and never writes a path that does not exist. The lines it adds look like this:

   ```json
   "com.mindattic.components.unity": "file:../../MindAttic.Components/unity/com.mindattic.components.unity",
   "com.mindattic.unity.shared": "file:../../Unity.Shared/unity/com.mindattic.unity.shared",
   "com.unity.test-framework": "1.6.0",
   ...
   "testables": [ "com.unity.netcode.gameobjects" ]
   ```

2. **Your game.** Type a name (`SpaceCrates`) and click **Create game**.

The wizard copies the game template into `Assets/SpaceCrates`, with `SpaceCrates` as its namespace and assembly names
and fresh asset GUIDs. It also:

- puts `Assets/SpaceCrates/Game.unity` first in Build Settings,
- turns on **Run In Background**,
- writes `mp-host.json` at the project root, with the server scene and the smoke test's bots,
- writes `.github/workflows/nightly.yml`, the nightly smoke test,
- and opens the scene.

The same thing runs in batch mode, for scripts and CI:

```powershell
Unity.exe -batchmode -projectPath C:\Games\SpaceCrates -executeMethod Unity.Multiplayer.Editor.NewGame.CreateFromCommandLine -addPackages
Unity.exe -batchmode -projectPath C:\Games\SpaceCrates -executeMethod Unity.Multiplayer.Editor.NewGame.CreateFromCommandLine -gameName SpaceCrates
```

The first adds the manifest lines. The second creates the game, or exits with code 2 and prints the missing lines.

## 4. Play (2 minutes)

Press **Play** and click **Host**. The lobby shows a join code (or your LAN address when no code service is set up) and
the players. Click **Start**. Move your square with the arrow keys or WASD, and walk into a crate to push it. **Esc**
opens the in-game menu: **Invite players**, **Settings** (your name and colour), **Vote to restart**, **End game**.

A second player needs a second copy of the game:

- **Multiplayer Play Mode** (quickest): install `com.unity.multiplayer.playmode`, open **Window ▸ Multiplayer ▸
  Multiplayer Play Mode**, tick **Player 2** and press Play. Host in one window. In the other, click **Join by IP** and
  type `127.0.0.1`, or **Join by code** and type the code.
- **A build:** **File ▸ Build And Run**. Host in the editor and join from the build, or the reverse.

Now try the edges. Rename yourself in Settings mid-game: every peer's label changes. Vote to restart: the other player
gets Yes / No, and the crates go back to their starts once two thirds say Yes. Close a client and rejoin within 60
seconds: you get your seat back ([Reconnect and seats](reconnect-seats.md)).

## 5. What you got: the tour

Everything in `Assets/SpaceCrates` is yours to change. Its `README.md` lists every file; these are the ones to read
first.

**The scene.** `Game.unity` has one `NetworkManager` GameObject holding Unity Transport and three of the kit's
components:

- **`NetBootstrap`** owns the session. A headless launch serves by itself, `-mode`, `-port` and the rest of the launch
  line retarget it, and it reads the server package's `server.json`.
- **`NetFrontEnd`** is every menu and dialog.
- **`NetVotes`** spawns the vote board and gives the front end its vote overlay.

The Player Prefab is `Resources/Game/Player.prefab`. A `Game` object holds `Game.cs`. Players, the dedicated server and
bots all run this one scene.

**The player** (`Runtime/Player.cs`):

```csharp
public partial class Player : NetEntity
{
    [ToServer(RateLimit = 20)]
    public partial void Move(Vector2 direction);   // the owner calls it; the server runs OnMove

    [Synced] private Vector2 _position;            // the server writes Position; every peer reads it

    private void OnMove(Vector2 direction) => _direction = Vector2.ClampMagnitude(direction, 1f);

    public string DisplayName => Roster(out NetPlayer p) ? p.DisplayName : "Player";   // from NetRoster
    public Color32 Color => Roster(out NetPlayer p) ? p.UnityColor() : new Color32(255, 255, 255, 255);
}
```

The server owns the game state, and clients send what they want to happen; that is the one rule the whole package is
built on ([UM-LAW-1](../BIBLE.md#UM-LAW-1)). The class is `partial` because the package's source generator writes the
other half. Names and colours are not copied onto the player: they come from the session's [roster](players.md), so a
rename shows everywhere at once. See [Attributes reference](attributes.md).

**Keyed objects** (`Runtime/Crate.cs`, `Runtime/Arena.cs`). The crate prefab has a `NetKey`. The server spawns
`crate:0`, `crate:1` and `crate:2` with `session.Objects.Spawn(prefab, key, position)`, and any peer finds one by its key,
not by spawn order:

```csharp
if (session.Objects.TryGet("crate:0", out Crate crate)) Debug.Log(crate.Position);
```

See [Networked objects by key](objects.md).

**Server rules and a vote** (`Runtime/Arena.cs`, `Runtime/Game.cs`). `Arena` is a plain class. `Arena.Serve(session,
cratePrefab)` spawns the crates whenever the session runs as a server, and its restart `VoteKind` despawns them and
spawns them again under the same keys. `Game.cs` registers that vote and adds the game's items to the front end:

```csharp
NetVotes.Instance.AddKind(_arena.CreateRestartVote());
frontEnd.AddScreen("How to play", s => s.AddText("Move with the arrow keys or WASD."), MenuPlacement.Both);
frontEnd.AddPauseMenuItems(s => s.AddButton("Vote to restart", () => NetVotes.Instance.Board.StartVote("restart")));
```

See [Votes](votes.md) and [Drop-in front end](front-end.md).

**The bot** (`Runtime/GameBot.cs`). A `[NetBotDriver]` `IBotDriver`. Launched with `-bot A`, the library joins the game
by code with no menus and calls `RunAsync`. The bot walks to its crate with the same `Move` calls a player makes,
pushes the crate, and calls `bot.Pass(...)` once the server has moved it. See [Bots](bots.md).

## 6. Run the tests (2 minutes)

Open **Window ▸ General ▸ Test Runner**. Under **EditMode**, `SpaceCrates.EditorTests` checks the scene's kit
components and runs the prefab validator. Under **PlayMode**, `SpaceCrates.Tests` runs on the test kit, each test with a
real host or dedicated server and real clients on a free loopback port:

- `PlayerTests` (`NetSessionTest`): a `Move` reaches the server and comes back as a synced position, everyone sees the
  roster's name and colour, and the server clamps a cheating client's input.
- `CrateTests`: every crate is found by key on a client, and a restart vote puts them back.
- `BotTests` (`NetBotTest`): a dedicated server and two `GameBot`s join by code through an in-process rendezvous service
  and each push their crate. This is the nightly smoke test, without Docker.

Or in batch mode:

```powershell
Unity.exe -batchmode -projectPath C:\Games\SpaceCrates -runTests -testPlatform PlayMode -assemblyNames SpaceCrates.Tests -testResults results.xml
```

See [Testing](testing.md) for the whole kit.

## 7. A server on your PC, then on the internet (5 minutes)

Install `mp-host`, the kit's deploy tool, once per machine:

```powershell
powershell -File C:\Games\Unity.Multiplayer\tools\install-mp-host.ps1
```

Then, from the project folder:

```powershell
cd C:\Games\SpaceCrates
mp-host game local            # builds the Linux server and runs it, with a rendezvous service, in Docker on this PC
mp-host game down             # stops it
```

`mp-host` finds the editor from `ProjectVersion.txt` and the server scene from `mp-host.json`. When the server is up it
prints the join code; players click **Join by code**. For a server on the internet, rent any Linux box with Docker and
SSH by key (a $4 VPS works), store its login and a GitHub token for the image once (kept in MindAttic.Vault, never in
the repo), and deploy. `spacecrates` is the server name the wizard put in `mp-host.json`:

```powershell
mp-host credentials set-ssh --name spacecrates --host 203.0.113.5 --user root --key $HOME\.ssh\id_ed25519
mp-host credentials set-ghcr --username <github-user> --token <PAT with write:packages>
mp-host game deploy --dry-run     # every step, nothing done
mp-host game deploy               # build, push to GHCR, start on the box, print the join code
```

[Hosting](hosting.md) covers the fifteen-plus providers, per-match allocators and updates.

## 8. Bots end to end, and every night (2 minutes)

```powershell
mp-host game smoke
```

This builds one Linux player and runs it under Docker Compose three ways at once: as the dedicated server, as the
rendezvous service, and as two bots (`-bot A`, `-bot B`, from `mp-host.json`). It exits 0 once both bots have logged
`[NetBot:A] OK:`.

Push the project to GitHub and the `nightly.yml` the wizard wrote runs the same smoke test every night. It is one job:
`uses: mindattic/Unity.Multiplayer/.github/workflows/game-smoke.yml@main`. Add a `UNITY_LICENSE` secret for GameCI. If
your MindAttic repos are private, also add `MINDATTIC_REPOS_TOKEN`. See [Bots](bots.md#the-nightly-workflow).

## 9. Make it yours

Rename, replace and delete freely. The order most games take:

1. Replace the squares (`Shapes.cs`) with your art. Replace `Player`'s movement with your controls: keep input as
   `[ToServer]` calls and state as `[Synced]` fields.
2. Replace `Arena` with your rules, started from `session.Started` on the server.
3. Give your objects keys from your own data (level ids, entity ids).
4. Add your votes (`VoteKind` with a `Check` and a `Passed`) and your menu items.
5. Teach `GameBot` your game, so the smoke test proves a real round.
6. Move engine-free rules into an assembly with `noEngineReferences` and test it with plain `dotnet test`, as
   [Unity.Cursory does](case-study-cursory.md).

## Without the wizard

Each piece also works on its own, in an existing project:

- **MindAttic ▸ Multiplayer ▸ Add Front End** networks the open scene (a NetworkManager with Unity Transport, a
  `NetBootstrap` and a prefab list) and adds `NetFrontEnd`. If MindAttic Components is missing, it names the manifest
  line. See [Drop-in front end](front-end.md).
- **MindAttic ▸ Multiplayer ▸ Set Up Scene** does the same without the menus. Drive the session from your own UI (below).
- **Add Component ▸ MindAttic ▸ Multiplayer ▸ Net Votes** adds votes. See [Votes](votes.md).
- **MindAttic ▸ Multiplayer ▸ Validate Prefabs** checks every network prefab before you ship.
- [Porting a game](porting.md) moves an existing networked project onto the package.

### Drive it from your own UI

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
`InvalidOperationException` while the session is running. `DefaultConfig` reads the current defaults back. The
environment and the command line (`-port`, `-transport`, `-mode`, ...) still win over both the Inspector and
`Configure`, so a launch line can always retarget a build.

## The samples

**Window ▸ Package Manager ▸ MindAttic Multiplayer ▸ Samples** has three:

- **Game Template**: what New Game copies. Prefer the menu, which also checks the manifest, renames the namespace,
  gives the assets fresh GUIDs and writes `mp-host.json`.
- **Front End**: a square per player with the drop-in menus, and the hooks in one script.
- **Minimal**: the same arena with a small hand-made IMGUI menu that calls `NetBootstrap` directly, and no extra
  packages. Its `README.md` walks through every line, including a `[ToClients]` call.

## Where to next

- [The kit](kit.md): the three packages and which piece does which job.
- [Case study: Unity.Cursory](case-study-cursory.md): a full game built this way.
- [Drop-in front end](front-end.md), [Players](players.md), [Votes](votes.md), [Networked objects by key](objects.md).
- [Testing](testing.md) and [Bots](bots.md).
- [Topologies](topologies.md), [Join codes](join-codes.md) and [Hosting](hosting.md) once your server outlives the
  editor.
