# MyGame: a networked game skeleton

Made by **MindAttic ▸ Multiplayer ▸ New Game…** from Unity.Multiplayer's game template. It is a whole game you can
play, test, deploy and smoke-test today, built from the three MindAttic kit packages: Unity.Multiplayer (networking,
votes, bots, test kit, hosting), MindAttic Components (the menus and overlays) and Unity.Shared (the player's profile
name and colour). Rename, replace and delete freely: it is your code now.

Open `Game.unity`, press **Play** and click **Host**. A second copy (Multiplayer Play Mode, or a build) clicks **Join by
IP** (`127.0.0.1`) or **Join by code**. Move with the arrow keys or WASD and walk into a crate to push it. **Esc** opens
the in-game menu: **Invite players**, **Settings** (name and colour), **Vote to restart**.

## What is here

| File | What it shows |
|---|---|
| `Game.unity` | The one scene for players, the dedicated server and bots. The NetworkManager GameObject holds `NetworkManager`, Unity Transport, `NetBootstrap` (the session), `NetFrontEnd` (every menu) and `NetVotes` (votes and the overlay); the Player Prefab is `Resources/Game/Player.prefab`. A `Game` object holds `Game.cs`. |
| `Runtime/Player.cs` | The player, a `NetEntity`: one `[ToServer] Move(direction)` (rate-limited, clamped on the server) and one `[Synced] Position`. Its square and label use the owner's name and colour from `NetSession.Roster` (`NetRoster`), so a rename in Settings shows at once. |
| `Runtime/Crate.cs` | A keyed networked object: the prefab has a `NetKey`; the server spawns `crate:0`, `crate:1`, `crate:2` with `NetSession.Objects.Spawn`, and any peer finds one with `session.Objects.TryGet("crate:0", out Crate crate)`. |
| `Runtime/Arena.cs` | The server's rules in a plain class: spawning the crates, pushing them, and the **restart** `VoteKind` that despawns and respawns them under the same keys. Plain so a test can serve it on its own session. |
| `Runtime/Game.cs` | The scene glue: serves the `Arena`, registers the restart vote on `NetVotes`, and adds a How-to-play screen and a Vote to restart button to the front end. |
| `Runtime/GameBot.cs` | The `[NetBotDriver]` `IBotDriver`: launched with `-bot A`, it walks to its crate and pushes it, then passes. |
| `Runtime/GameInput.cs`, `Runtime/Shapes.cs` | Keyboard input for either input system, and coloured squares made in code (no art). |
| `Resources/Game/*.prefab`, `GamePrefabs.asset` | The player and crate prefabs and the `NetPrefabList` the NetBootstrap registers. |
| `Tests/PlayMode/GameTests.cs` | PlayMode tests on the kit's `NetSessionTest` and `NetBotTest`: a move round trip with the roster name and colour, the server clamping input, crates by key, a restart vote, and two bots joining by code. |
| `Tests/EditMode/GameSetupTests.cs` | EditMode checks: the scene's kit components and prefabs, and the prefab validator. |

The wizard also wrote, at the project root, `mp-host.json` (the server scene, the smoke test's bots `A,B` and
scenario) and `.github/workflows/nightly.yml` (the library's reusable smoke workflow), put `Game.unity` first in Build
Settings and turned on Run In Background.

## Next steps

- **Tests:** Window ▸ General ▸ Test Runner, or `Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -assemblyNames MyGame.Tests`.
- **A server on this PC:** `mp-host game local` (Docker). **Hosted:** `mp-host game deploy --dry-run`, then `mp-host game deploy`.
- **Bots end to end:** `mp-host game smoke` builds one Linux player and runs it as the server and two bots under Docker Compose.
- **Docs:** [Getting started](https://mindattic.github.io/Unity.Multiplayer/articles/getting-started.html) walks
  through every file above; [The kit](https://mindattic.github.io/Unity.Multiplayer/articles/kit.html) maps the three
  packages; [Case study: Unity.Cursory](https://mindattic.github.io/Unity.Multiplayer/articles/case-study-cursory.html)
  is a full game built the same way.
