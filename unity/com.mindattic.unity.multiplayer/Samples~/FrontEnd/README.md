# Front End sample

A complete multiplayer game shell with no menu code: the drop-in front end (`NetFrontEnd`, from the optional
`Unity.Multiplayer.UI` assembly) gives this scene a main menu, a host lobby with the join code and the players, a
connecting dialog, plain-words dialogs for every way a game can end, an automatic reconnect, and an in-game Esc menu.
The game itself is the Minimal sample's square-per-player arena, with each square in its player's own colour and name.

The full guide is [the front end article](../../../../docs/articles/front-end.md) in the repository's docs.

## Before you import it

The front end is built from MindAttic Components, and takes the player's name and colour from their MindAttic profile.
Add both to the game's `Packages/manifest.json` (the paths assume the MindAttic repos sit beside your game's folder):

```json
"com.mindattic.components.unity": "file:../../MindAttic.Components/unity/com.mindattic.components.unity",
"com.mindattic.unity.shared": "file:../../Unity.Shared/unity/com.mindattic.unity.shared"
```

`com.mindattic.unity.shared` is optional: without it the name and colour are kept in PlayerPrefs. Without
`com.mindattic.components.unity` this sample (and the front end) does not compile at all, by design, so a game that
does not want it pays nothing for it.

## Run it

Open `FrontEnd.unity` and press Play. Click **Host**: the lobby shows the address to join (or a join code, when the
NetBootstrap has Relay or a rendezvous URL) and the players. Click **Start**, then move with the arrow keys or WASD.
Press **Esc** for the in-game menu. A second copy of the game (Multiplayer Play Mode, or a build) joins with
**Join by IP** or **Join by code**. Change your name and colour under **Settings**.

## What is in it

| File | What |
|---|---|
| `FrontEnd.unity` | A camera, and the NetworkManager made by **MindAttic > Multiplayer > Add Front End**: Unity Transport, `NetBootstrap`, `NetFrontEnd`, the player prefab. A `Game` object with `FrontEndGame`. |
| `FrontEndPlayer.cs` | The player: one `[ToServer]` for the arrow keys, and `[Synced]` position, colour and name. The server copies the colour and name from the player's seat, which got them from the front end's connection request. |
| `FrontEndGame.cs` | The game's hooks into the front end: a "How to play" screen in both menus (`AddScreen`), a setting on the Settings screen (`AddSettingsItems`), and `GameStarted` / `Left` / `Reconnected`. |
| `FrontEndInput.cs` | Arrow keys or WASD from the Input System or the Input Manager. |
| `FrontEndPlayer.prefab`, `FrontEndPrefabs.asset` | The player prefab and the prefab list the NetBootstrap registers. |

## Do it in your own game

1. Add the two manifest lines above.
2. Open your scene and run **MindAttic > Multiplayer > Add Front End**. It networks the scene if it is not yet (the
   same as **Set Up Scene**) and adds `NetFrontEnd` beside the `NetBootstrap`.
3. Press Play.

Everything else is optional: a `NetFrontEndSettings` asset (Assets > Create > MindAttic > Multiplayer > Front End
Settings) changes the title, the lobby, the scene Start loads and the reconnect timing; the hooks above add the game's
own screens and react to play starting and ending.
