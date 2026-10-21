# The kit: three packages, one way to make a multiplayer game

Unity.Multiplayer is one of three MindAttic packages that are built, tested and released to work as one kit. Together
they are a one-stop shop for a multiplayer Unity 6 game: networking, menus, player identity, votes, networked objects,
bots, a test kit, server builds and deploys. You add three lines to `Packages/manifest.json`, run one wizard, and get a
game that already does all of it ([Getting started](getting-started.md)).

| Package | Repo | What it gives a game | Needed for |
|---|---|---|---|
| `com.mindattic.unity.multiplayer` | [mindattic/Unity.Multiplayer](https://github.com/mindattic/Unity.Multiplayer) (this site) | Networking on Netcode for GameObjects behind its own API: attribute RPCs and `[Synced]` fields, any topology, join codes, reconnect and seats, the roster, votes, keyed objects, bots, the test kit, the server package and `mp-host` | Everything. It is the only required one. |
| `com.mindattic.components.unity` | [mindattic/MindAttic.Components](https://github.com/mindattic/MindAttic.Components) (`unity/`) | The MindAttic design system for Unity: themed uGUI and TextMesh Pro menus, dialogs, toasts, toolbars, HUD widgets (minimap, status pill, banner), keyboard and gamepad navigation | The drop-in front end (`NetFrontEnd`), the vote overlay, and your own HUD |
| `com.mindattic.unity.shared` | [mindattic/Unity.Shared](https://github.com/mindattic/Unity.Shared) | Player profiles shared by every MindAttic game: one `settings.json` per profile with the player's name, colour, volumes and language | The player's name and colour following them from game to game. Without it they are kept in PlayerPrefs |

```text
your game
  ├─ Unity.Multiplayer ── networking, roster, votes, keyed objects, bots, test kit, mp-host
  │     └─ Unity.Multiplayer.UI  (compiled only when MindAttic Components is installed)
  │           ├─ NetFrontEnd, VoteOverlay ─── built from ──► MindAttic Components
  │           └─ ProfileIdentitySource ────── reads ──────► Unity.Shared (when installed)
  ├─ MindAttic Components ── your HUD, your own screens (MaToolbar, MaMinimap, MaToast, ...)
  └─ Unity.Shared ── your settings and saves (Profiles.Current)
```

## Why three packages and not one

- **Pay only for what you use.** A server-only tool, a game with its own UI or a test harness takes Unity.Multiplayer
  alone. Its UI assembly (`Unity.Multiplayer.UI`) compiles only when MindAttic Components is installed (an asmdef
  version define), so it never drags TextMesh Pro or uGUI into a project that does not want them.
- **Each is useful on its own.** MindAttic Components themes single-player games and web-styled tools; Unity.Shared keeps
  profiles for every MindAttic game, networked or not.
- **They still read as one.** The front end's lobby is a MindAttic Components roster list, its Settings screen saves to
  the Unity.Shared profile, and the vote overlay shares the theme of your own HUD. One theme change restyles the menus,
  the overlay and the HUD together.

A Unity `package.json` cannot depend on a `file:` package, so the multiplayer package cannot pull the other two in by
itself. **MindAttic ▸ Multiplayer ▸ New Game…** and **Add Front End** check `Packages/manifest.json`, name the exact
lines that are missing, and New Game can add them for you.

## Which piece for which job

| You want | Use | Package | Read |
|---|---|---|---|
| A whole game skeleton, today | **MindAttic ▸ Multiplayer ▸ New Game…** | all three | [Getting started](getting-started.md) |
| A network session in a scene | `NetBootstrap` (**Set Up Scene**), `NetBootstrap.Configure` | Multiplayer | [Topologies](topologies.md) |
| Main menu, lobby, join by code, reconnect, Esc menu | `NetFrontEnd` (**Add Front End**) | Multiplayer + Components (+ Shared) | [Drop-in front end](front-end.md) |
| Send input to the server, share state | `NetEntity` with `[ToServer]`, `[ToClients]`, `[ToOwner]`, `[ToClient]`, `[Synced]` | Multiplayer | [Attributes reference](attributes.md) |
| Names, colours, ping, "you" | `NetSession.Roster` (`NetRoster`, `NetPlayer`), `PlayerColors` | Multiplayer | [Players](players.md) |
| The player's name and colour across games | `ProfileIdentitySource`, `Profiles.Current` | Shared | [Unity.Shared](https://github.com/mindattic/Unity.Shared) |
| Restart, change-level and kick votes | `NetVotes`, `VoteKind`, `VoteOverlay` | Multiplayer (+ Components for the overlay) | [Votes](votes.md) |
| Find a spawned object by your own id | `NetKey`, `NetSession.Objects.Spawn` / `TryGet` | Multiplayer | [Networked objects by key](objects.md) |
| Join codes through home routers or to a rented box | Unity Relay or the rendezvous service | Multiplayer | [Join codes](join-codes.md) |
| A HUD in the same look as the menus | `MaCanvas`, `MaToolbar`, `MaMinimap`, `MaStatusPill`, `MaBanner`, `MaToast` | Components | [MindAttic Components for Unity](https://github.com/mindattic/MindAttic.Components/blob/main/docs/unity/COMPONENTS.md) |
| Tests over a real connection | `NetSessionTest`, `NetBotTest`, `FreePort`, `FrontEndTesting` | Multiplayer (`Unity.Multiplayer.Testing`) | [Testing](testing.md) |
| A bot that plays the game | `IBotDriver`, `[NetBotDriver]`, `-bot A` | Multiplayer | [Bots](bots.md) |
| A server on this PC, a VPS or a per-match host | `mp-host game local`, `mp-host game deploy`, `mp-host.json` | Multiplayer (`mp-host`) | [Hosting](hosting.md) |
| Server and bots end to end, nightly | `mp-host game smoke`, `game-smoke.yml` | Multiplayer | [Bots](bots.md#mp-host-game-smoke) |

## Where they live on disk

The kit's packages are referenced by `file:` path, with the repos side by side:

```text
<repos>/
  Unity.Multiplayer/        unity/com.mindattic.unity.multiplayer
  MindAttic.Components/     unity/com.mindattic.components.unity
  Unity.Shared/             unity/com.mindattic.unity.shared
  YourGame/                 Packages/manifest.json -> "file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer", ...
```

A game elsewhere uses absolute `file:` paths (or the New Game wizard writes them for you). The reusable CI workflow
checks the sibling repos out the same way (`siblings:` in [game-smoke.yml](bots.md#the-nightly-workflow)).

## Versions

The three packages are released together and tested together: this package's PlayMode suite runs with MindAttic
Components and Unity.Shared installed, and [Unity.Cursory](case-study-cursory.md) runs all three on every change.
Unity.Multiplayer follows whole-number versions (`1`, `2`, ...; see the
[CHANGELOG](https://github.com/mindattic/Unity.Multiplayer/blob/main/CHANGELOG.md)).
