# Drop-in front end

A multiplayer game needs the same screens every time: a main menu with Host and Join, somewhere to type an address or
a join code, a lobby that shows the code and who is in, a "connecting..." dialog you can cancel, a clear message when
something goes wrong, a way back in after a dropped connection, and an in-game Esc menu. `NetFrontEnd` is all of that
as one component. Add it beside the scene's `NetBootstrap` and press Play.

It is built entirely from [MindAttic Components](https://github.com/mindattic/MindAttic.Components) (the MindAttic
design system for Unity), so it looks like every other MindAttic game and page, works with a mouse, a keyboard or a
gamepad, and follows the dark or light theme.

## Add it (two minutes)

1. Add MindAttic Components, and optionally Unity.Shared, to the game's `Packages/manifest.json` beside the
   multiplayer package. The paths assume the MindAttic repos sit beside your game's folder:

   ```json
   "com.mindattic.unity.multiplayer": "file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer",
   "com.mindattic.components.unity": "file:../../MindAttic.Components/unity/com.mindattic.components.unity",
   "com.mindattic.unity.shared": "file:../../Unity.Shared/unity/com.mindattic.unity.shared"
   ```

2. Open your scene and run **MindAttic ▸ Multiplayer ▸ Add Front End**. It networks the scene first if it is not yet
   (exactly what **Set Up Scene** does: a NetworkManager with Unity Transport, a `NetBootstrap` and a prefab list) and
   then adds `NetFrontEnd` to the same GameObject. Running it again changes nothing. If MindAttic Components is
   missing it changes nothing and tells you the exact manifest line to add.

3. Press **Play**.

That is the whole setup. Everything below is optional.

### Why two extra packages, and why "optional"

The front end lives in its own assembly, `Unity.Multiplayer.UI`, inside the multiplayer package. That assembly compiles
only when `com.mindattic.components.unity` is installed (an asmdef `versionDefines` entry defines
`MINDATTIC_COMPONENTS`, and the assembly's define constraint requires it). A game that brings its own UI pays nothing
for it: no extra package, no extra code, no TextMesh Pro. A Unity `package.json` cannot list a `file:` dependency, so
the package cannot pull the two in by itself. That is why you add the manifest lines yourself.

`com.mindattic.unity.shared` is optional too. With it, the player's name and colour come from their MindAttic profile
(`Profiles.Current.Settings.Common`), the same ones every MindAttic game uses. Without it they are kept in PlayerPrefs.

TextMesh Pro needs its Essential Resources in the project. MindAttic Components imports them by itself the first time
the editor loads, with no pop-up.

## What the player sees

| Screen | What is on it |
|---|---|
| Main menu | **Host**, **Join by IP**, **Join by code** (hidden when the NetBootstrap's Join Codes is None), **Settings**, the game's own items, **Quit**. The subtitle says who you are playing as. |
| Join by IP | One field, `192.168.1.20` or `192.168.1.20:7777` (or `[::1]:7777`). The port defaults to the NetBootstrap's, and the last address is remembered. |
| Join by code | One field. Any case, with or without dashes; a single misread character (`0`/`O`, `1`/`I`) is corrected (see [Join codes](join-codes.md)). |
| Connecting | A busy dialog ("Joining 192.168.1.20:7777...", "Looking up ABC-DEF-GHJ...") with **Cancel**. |
| Lobby (host) | The join code, large, with a **Copy** button. If no code could be published, it shows "Players join by IP at *your LAN address*:*port*" instead. Below it is the live roster: each player's name and colour swatch, a **Host** tag on you, "reconnecting..." on a dropped player's held seat, and a remove button with a confirm. Then **Leave** and **Start**. |
| In game | The menus are gone. **Esc**, or gamepad Start or B, opens the in-game menu: **Resume**, **Invite players** (the host's code and roster again), **Settings**, the game's own items, and **Leave game** (**End game** for the host), with a confirm. |
| Settings | Name and colour (eight colours that read well on dark and light), plus the game's own settings. It saves when you go back. |

**Host** publishes a join code when one can be published. With the NetBootstrap's Join Codes left at Auto it tries
Unity Relay when `com.unity.services.multiplayer` is installed, then the rendezvous service when the NetBootstrap has a
Rendezvous URL (the same rule as `NetSession.HostWithCode`, see [Join codes](join-codes.md)); Relay or Rendezvous
chosen explicitly is the only one tried. When none can, for example when Relay is installed but the project is not
linked to Unity Cloud and there is no URL, the game is hosted anyway. A toast says why there is no code, and players
join by IP.

### When a game ends

Every way a session can end has its own title and sentence. The text lives in one place, Core's `DisconnectText`, so a
game's own UI can show the same words with `DisconnectText.For(session.LastDisconnect)`. When the server said something
more specific, such as a kick reason or the two versions, that appears as a second, muted line.

| Reason | Title | What it says |
|---|---|---|
| Version mismatch | Different version | Your game is a different version from the host's. Update both to the same version to play together. |
| Protocol mismatch | Different build | Your game and the host's were built from different code. Install the same build on both. |
| Full | Game full | Every seat in this game is taken. Try again later. |
| Banned | Can't join | You are banned from this game. |
| Wrong password | Wrong password | This game needs a password, and the one given did not match. |
| Bad payload | Couldn't join | The host could not read your join request. Update both games to the same version. |
| Kicked | Removed from the game | The host removed you from the game. (The host's reason follows.) |
| Server shut down | Game ended | The host closed the game. |
| Could not connect | Couldn't connect | No game answered. Check the address or code, and that the host is still running. |
| Timed out | Connection lost | The game stopped responding. Check your internet connection and try again. |
| Transport failure | Network error | The network connection failed. Check your connection and try again. |

Leaving on purpose (Leave, Cancel) shows nothing.

### Reconnecting

When a client's connection is lost (`TimedOut` or `TransportFailure`), the front end does not drop the player back to
the main menu. It shows "Connection lost. Getting your seat back (try 1 of 5)..." with a progress bar and **Cancel**,
and rejoins the same way the player first joined (the same address, or the same code looked up again). It sends the
same player token, so within the server's `ReconnectGraceSeconds` the player gets their seat and player object back
(see [Reconnect and seats](reconnect-seats.md)). The tries wait 1, 2, 4, 8, 8 seconds by default. A refusal (full,
banned) or a kick stops the retries. If every try fails, the player sees "Connection lost" and the main menu.

A server that shut down, a kick and a refusal are never retried: they were deliberate, and retrying would only fail
again. The rule is Core's `ReconnectPolicy`, so it is covered by plain `dotnet test`.

## The player's name and colour

The front end reads them from an `IPlayerIdentitySource` just before it hosts or joins. It puts them on the session's
`Identity`, which goes to the server in the connection approval payload. The server's `Seat` then carries them
(`seat.DisplayName`, `seat.Color`), and that is what the lobby roster shows. A game reads them the same way. The Front
End sample's player copies them into two `[Synced]` fields, so every peer draws each square in its owner's colour.

```csharp
public interface IPlayerIdentitySource
{
    string DisplayName { get; }
    Color Color { get; }
    string StoredIn { get; }                 // "your MindAttic profile (Default)", "this computer"
    void Save(string displayName, Color color);
}
```

The default is `ProfileIdentitySource` when Unity.Shared is installed, else `PlayerPrefsIdentitySource`. A game with
its own accounts sets its own:

```csharp
frontEnd.Identity = new MyAccountIdentity();
```

Names are cleaned the way the server accepts them (`PlayerIdentitySources.CleanName`: trimmed, no control characters,
at most 32 characters, "Player" when empty). The NetBootstrap's own Display Name and Player Color fields are overridden
by the front end.

## Hooks for the game

```csharp
NetFrontEnd frontEnd = NetFrontEnd.Instance;   // or a serialized reference

// Events.
frontEnd.SessionStarted += session => { };      // a host is running / a client is in
frontEnd.GameStarted    += () => LoadLevel();   // the menus handed over: host pressed Start, client got in, dedicated server running
frontEnd.Left           += why => ShowTitle();  // back at the main menu, with why (LocalLeave for the player's own leave)
frontEnd.Reconnected    += () => { };           // a lost connection came back
frontEnd.StateChanged   += state => { };        // Menu, Connecting, Lobby, InGame, Reconnecting

// The game's own screens and items.
frontEnd.AddScreen("Options", s => s.AddRange("Volume", 0, 100, 80, SetVolume, true));   // a button in both menus
frontEnd.AddScreen("Credits", s => s.AddText("Made by..."), MenuPlacement.MainMenu);
frontEnd.AddMainMenuItems(s => s.AddButton("Tutorial", StartTutorial));                 // before Quit
frontEnd.AddPauseMenuItems(s => s.AddButton("Restart level", RestartLevel));            // before Leave
frontEnd.AddSettingsItems(s => s.AddToggle("Show ping", showPing, v => showPing = v));  // under name and colour

// Keep the menus out of the way (a cutscene, your own title screen first).
frontEnd.MenusVisible = false;
```

Each `Add...Items` callback adds items to a screen the front end owns. The in-game menu is built once, so an
`AddPauseMenuItems` callback runs exactly once (when the menus are built, or at once if they already are) and its items
stay for the front end's life, across games. Refresh what they show from the screen's `Shown` event, which fires once
each time the menu opens:

```csharp
frontEnd.AddPauseMenuItems(s =>
{
    MaToggle shake = s.AddToggle("Screen shake", false, on => player.Shake = on);
    s.Shown += () => shake.SetValueWithoutNotify(player.Shake);   // hooked once, never piles up
});
```

An assembly that calls these hooks references `Unity.Multiplayer.UI`, `MindAttic.Components.Unity` and
`MindAttic.Components.Unity.Core` (the default `Assembly-CSharp` references them all already). `s` is a MindAttic
Components `MenuScreen`, so everything it offers (`AddButton`, `AddToggle`, `AddRange`, `AddSelect`,
`AddTextbox`, `AddText`, `AddSection`, ...) is available. `frontEnd.MainMenu` and `frontEnd.PauseMenu` are the two
`MenuStack`s, if you need more.

Every button is also a method, so a game can drive the same flow from its own UI or a quick-play button. You still get
the dialogs and the reconnect: `Host()` / `HostAsync()`, `JoinByAddress(text)` / `JoinByAddressAsync`,
`JoinByCode(code)` / `JoinByCodeAsync`, `StartGame()`, `Leave()`, `ConfirmLeave()`, `OpenSettings()`.

### Loading a level when the host presses Start

Either handle `GameStarted` yourself, or set **Game Scene** on a settings asset: the server then loads it for everyone
through [scene flow](topologies.md) when the host presses Start. The scene must be in the build's scene list, and the
NetworkManager must have scene management on.

## The settings asset

Optional. **Assets ▸ Create ▸ MindAttic ▸ Multiplayer ▸ Front End Settings**, then drop it on the NetFrontEnd's
**Settings** field. Without one, the defaults below apply.

| Field | Default | What |
|---|---|---|
| Title | product name | The main menu's title |
| Join By Address / Join By Code / Settings Screen / Quit Button | on | Which main-menu buttons appear (Quit never on WebGL) |
| Dim Behind Menu | on | Dim the scene behind the main menu |
| Show Lobby | on | Off goes straight into the game after hosting |
| Game Scene | empty | A scene the server loads for everyone on Start |
| Kick Message | "The host removed you from the game." | What a removed player is told |
| Pause Menu / Pause Title | on / "Menu" | The in-game Esc menu |
| Auto Reconnect / Reconnect Attempts / First Delay / Max Delay | on / 5 / 1 s / 8 s | The reconnect policy (or set `frontEnd.Reconnect` in code) |
| Theme | current | A MindAttic `ThemeTokens` asset for the menus |
| Sorting Order | 500 | The menus' canvas order, above the game's own UI |

## Servers, bots and CI: no UI, ever

A headless process never creates a canvas, a menu or a dialog. That covers batch mode, `-nographics`, a Dedicated
Server build and a CI run, and also any launch that picked its role with `-mode` (a server or a bot client). The rule
is Core's `NetLaunch.ShowsMenus`. The component still follows the session, so `SessionStarted`, `GameStarted` and `Left`
fire, and messages go to the log. The same scene therefore runs as the player's game, a dedicated server and a bot.

## The Front End sample

**Window ▸ Package Manager ▸ MindAttic Multiplayer ▸ Samples ▸ Front End** imports `FrontEnd.unity`. It is the
Minimal sample's arena with the front end instead of a hand-made menu. Each square is in its owner's chosen colour,
with their name, and `FrontEndGame.cs` shows the hooks above. Its README lists every file.

## Testing your game's menus

`Unity.Multiplayer.UI.Testing` drives a game's own front end from its PlayMode tests, menus included in a batch-mode
run (`FrontEndTesting.ForceMenus`). See [Testing your front end](testing.md#testing-your-front-end).

## Where it is tested

- `dotnet test tests/Core.Tests`: `DisconnectText` for every reason and rejection, `ReconnectPolicy`, `HostAddress`
  parsing, and `NetLaunch.ShowsMenus` (FrontEndRulesTests).
- `tests/run-playmode.ps1`, `Unity.Multiplayer.UI.Tests`. Two front ends, a host and a client over real Unity
  Transport on loopback, are driven through their menus:
  - hosting shows the lobby; Join by IP gets the client in; the roster shows the client's name and colour from the
    approval payload; Start enters the game; Leave from the in-game menu returns to the main menu;
  - join by code uses the code the lobby shows, through an in-process rendezvous server, also with Join Codes left at
    Auto and only a rendezvous URL configured;
  - the in-game menu's game items run once and their `Shown` handlers fire once per open, across several games;
  - a version mismatch and a kick each show their dialog;
  - a lost connection (a UDP relay that cuts the link) reconnects to the same seat; a link that never comes back gives
    up with a message;
  - Settings saves the name and colour, and the new name reaches the server;
  - a headless scene with a real NetBootstrap builds no UI;
  - the profile-backed identity reads and writes the MindAttic profile.
- `tests/run-editmode.ps1`: Add Front End networks the scene and adds the component, a second run changes nothing,
  undo removes it, and with the packages missing it changes nothing and names the exact manifest lines.
- `tests/run-sample.ps1 -Sample FrontEnd`: the sample as Package Manager imports it, wired by the wizard, serving
  headless with no UI, and each square carrying its player's name and colour on every peer.
