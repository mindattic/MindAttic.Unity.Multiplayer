# Bots and end-to-end smoke tests

A bot is your game, launched headless with `-bot <role>`, playing itself over the real network. The library does all
of the plumbing: it keeps the scene from serving or showing menus, joins by code through the rendezvous service, and
then hands the connected session to **your bot driver**, the only bot code a game writes. The same build runs as the
dedicated server, so one Linux player, a rendezvous container and N bot containers make an end-to-end smoke test of
the whole game. `mp-host game smoke` runs that locally, and one job in a nightly workflow runs it in CI.

Unity.Cursory is the first game on it: its two bots grab a heavy block from both ends and drag it around a wall onto
a pad, a level one player cannot solve.

## What a new game does

A game made with **MindAttic ▸ Multiplayer ▸ New Game…** already has all four steps below: its `GameBot` driver,
`BotTests` (a `NetBotTest`), an `mp-host.json` with `"bots": "A,B"`, and `.github/workflows/nightly.yml`. See
[Getting started](getting-started.md#8-bots-end-to-end-and-every-night-2-minutes). For an existing game:

1. **Write a driver.** One class, anywhere in the game's code:

   ```csharp
   using System.Threading.Tasks;
   using Unity.Multiplayer;
   using UnityEngine;

   [NetBotDriver]                                   // found by itself; or make it a component in the scene
   public sealed class RunnerBot : IBotDriver
   {
       public async Task RunAsync(NetBot bot)       // called once the bot has joined
       {
           Runner me = await bot.LocalPlayer<Runner>();               // this bot's player object
           Flag flag = await bot.Spawned<Flag>();                     // any replicated object
           me.RunTo(bot.Role == "A" ? flag.Left : flag.Right);        // [ToServer] calls, as a player's input would
           await bot.Until(() => flag.Captured, "the flag to be captured", 60f);
           bot.Pass("captured the flag in " + bot.Scenario);         // logs "[NetBot:A] OK: ..."
       }
   }
   ```

2. **Test it in process** with the test kit's `NetBotTest` (below): a dedicated server and N bots in one PlayMode
   test, no Docker.

3. **Run the real thing** from the project folder (needs Docker and the project's Unity version):

   ```text
   mp-host game smoke                      # two bots, A and B
   mp-host game smoke --bots 4 --scenario ctf-2
   ```

4. **Nightly in CI**: add `.github/workflows/nightly.yml` with one job:

   ```yaml
   name: Nightly smoke
   on: { schedule: [{ cron: '0 10 * * *' }], workflow_dispatch: {} }
   jobs:
     smoke:
       uses: mindattic/Unity.Multiplayer/.github/workflows/game-smoke.yml@main
       secrets: inherit                    # UNITY_LICENSE, as GameCI needs
   ```

   Defaults for both come from the game's `mp-host.json`, so the command line and the workflow stay bare:

   ```json
   { "bots": "A,B", "scenario": "2" }
   ```

## The driver

`IBotDriver.RunAsync(NetBot bot)` runs on the main thread after the bot has connected. `NetBot` gives it:

| Member | What it is |
|---|---|
| `Session` | The bot's connected `NetSession` (roster, events, everything a player's client has). |
| `Role` | What `-bot` said: `A`, `B`, `scout`, `3`. One driver tells its bots apart by role. |
| `Scenario` | What `-scenario` said, or null. The library only passes it on; the server gets the same word (`NetBots.Scenario`). |
| `JoinCode` | The code it joined by. |
| `Pass(message)` | The run passed: logs `[NetBot:<role>] OK: message`. The driver may keep playing (hold a block, keep a door open); the timeout no longer applies. |
| `Fail(message)` | The run failed: logs `[NetBot:<role>] FAIL: message`, cancels every wait, and a player quits with exit code 1. Throwing does the same, with the exception's message. |
| `NextFrame()`, `Seconds(s)` | Resume next frame, or after real seconds. |
| `Until(condition, what, timeout)` | Resume once the condition holds; a `TimeoutException` naming `what` fails the run. |
| `Spawned<T>(match)`, `FindSpawned<T>`, `FindAllSpawned<T>` | Replicated objects on this client that carry a `T`. |
| `LocalPlayer<T>()` | This bot's own player object. |
| `Cancellation` | Cancelled when the run fails or times out. |

Returning without `Pass` or `Fail` passes the run. Any wait after a failure throws `OperationCanceledException`, so a
driver never outlives its verdict.

**Registering it.** Either mark a plain class `[NetBotDriver]` (it needs a parameterless constructor; with IL2CPP and
high stripping also mark it `[Preserve]`), or put a component that implements `IBotDriver` in the scene. A launch with
none, or more than one, fails with a message saying so.

## The launch line

| Argument | On | What it does |
|---|---|---|
| `-bot <role>` | a bot | Makes the process a bot. `NetBootstrap` starts nothing by itself and `NetFrontEnd` builds no menus (Core's `NetLaunch`), even in a window. |
| `-joincodefile <path>` | a bot | Waits for the file and joins by the code in it. |
| | a server | Writes every join code it publishes to the file, atomically. |
| `-joincode <code>` | a bot | Joins by this code. |
| `-address <ip>` `-port <n>` | a bot | With no code or code file: joins directly. |
| `-scenario <name>` | both | Free text for the game: a level, a map, a mode. |
| `-bottimeout <s>` | a bot | Fails the run after this long (default 180). |
| `-playertoken <t>` | a bot | Its seat token; without one each bot launch gets a fresh token, so two bots from one build never take each other's seat. |

Everything else is read as usual: `-rendezvous`, `-transport`, `-tickrate`. A bot plays as `Bot <role>` in the roster.
A typical pair:

```text
Game.x86_64 -batchmode -nographics -mode server -rendezvous http://rendezvous:8080 -joincodefile /shared/code.txt -scenario 2
Game.x86_64 -batchmode -nographics -bot A -rendezvous http://rendezvous:8080 -joincodefile /shared/code.txt -scenario 2
```

A plain Linux player run with no display crashes in SDL's default video driver. The smoke image sets
`SDL_VIDEODRIVER=dummy`, and so does the server package's generated Dockerfile; set it yourself when you run a player
headless anywhere else.

## In-process tests: `NetBotTest`

`Unity.Multiplayer.Testing.NetBotTest` is the compose stack without Docker. It is a `NetSessionTest` with a dedicated
server and one client session per bot. `RunBots` starts the bots first, which wait for the code file as they do in
compose. It then starts the server with an in-process rendezvous service and the code file, and fails the test unless
every bot passes:

```csharp
public class CaptureTheFlagBots : NetBotTest
{
    public CaptureTheFlagBots() : base(bots: 2) { }

    protected override string Scenario => "ctf-2";

    protected override void OnSessionsCreated()
    {
        SetPlayerPrefab(Resources.Load<GameObject>("Runner"));
        Server.Started += () => FlagRules.Serve(Server, Scenario);   // your server-side game code, on the fixture's server
    }

    [UnityTest, Timeout(180000)]
    public IEnumerator BothRunnersCaptureTheFlag() => RunBots(role => new RunnerBot(), "A", "B");
}
```

`Bots` and `Bot("A")` give each `NetBotRunner` afterwards (`State`, `PassMessage`, `FailureReason`, `Bot`). To check
the server in between, use `StartServerAndBots` and then `WaitForBots`. `BotTimeoutSeconds` (default 120) bounds each
bot's run. The library's own `NetBotTests` and Unity.Cursory's `BotEndToEndTests` are examples to copy.

Outside the fixture, `NetBotRunner.Run(gameObject, session, driver, new NetBotOptions { ... })` runs one bot on any
idle session, and `NetBots.WriteJoinCodes(session, path)` makes any server write its codes to a file.

## `mp-host game smoke`

From the project folder (or `--project <dir>`), [mp-host](hosting.md#deploy-any-game-with-one-command-mp-host-game)
does it all and exits 0 or 1:

1. Builds one StandaloneLinux64 player with the library's own build method,
   `Unity.Multiplayer.Editor.BotPlayerBuild.BuildFromCommandLine`, into `Builds/Smoke/player`. The boot scene is
   mp-host.json's `scene`, else the first enabled Build Settings scene. It is a plain player, because GameCI has no
   Linux Dedicated Server support. The editor is found as `mp-host game` finds it.
2. Writes `Builds/Smoke/Dockerfile` (Ubuntu, non-root, `SDL_VIDEODRIVER=dummy`) and `Builds/Smoke/docker-compose.yml`.
   The compose file holds the rendezvous service, `server` (`-mode server -joincodefile /shared/join-code.txt`) and one
   `bot-<role>` service per bot, sharing a volume. Read them, or run them by hand with `docker compose up`.
3. Builds the rendezvous image from the Unity.Multiplayer checkout that `Packages/manifest.json` points at (or
   `--multiplayer <dir>`; else `ghcr.io/mindattic/rendezvous`), then builds the game image.
4. Starts the stack with a fresh registration secret and reads the logs every 3 s. It passes once every bot has logged
   `[NetBot:<role>] OK:`, and fails on any `FAIL:` line or when the time runs out. The logs are kept in
   `Builds/Smoke/compose.log` and the stack is torn down.

| Option | mp-host.json | Default |
|---|---|---|
| `--bots A,B` or `--bots 3` | `bots` | `A,B` |
| `--scenario <s>` | `scenario` | none |
| `--server-args "<args>"`, `--bot-args "<args>"` | `server-args`, `bot-args` | none |
| `--timeout <s>` | `smoke-timeout` | 300 |
| `--skip-build` (reuse `Builds/Smoke/player`), `--player <dir>` (a player built elsewhere) | | build |
| `--keep` (leave it running), `--dry-run` (write the stack, run nothing), `--development`, `--unity <exe>` | | |

## The nightly workflow

`.github/workflows/game-smoke.yml` in Unity.Multiplayer is a reusable workflow. Its first job builds the player with
GameCI (`BotPlayerBuild`), and its second runs `mp-host game smoke --player ...` from a checkout of Unity.Multiplayer.
Inputs, all optional:

| Input | Default |
|---|---|
| `bots`, `scenario`, `server-args`, `bot-args`, `timeout` | the game's `mp-host.json`, then the CLI's defaults |
| `project-path` | `.` |
| `siblings` | none; repos (`owner/name`) checked out beside the game for `file:../../<name>` package references |
| `unity-version` | `auto` (ProjectVersion.txt) |
| `multiplayer-ref` | `main` |

Secrets: `UNITY_LICENSE` (or `UNITY_EMAIL`/`UNITY_PASSWORD`/`UNITY_SERIAL`) for GameCI, and `MINDATTIC_REPOS_TOKEN`
when Unity.Multiplayer or a sibling is private. `compose.log` is uploaded as the `smoke-logs` artifact.

The package's own nightly (`nightly-smoke.yml`, the Minimal sample's `SmokeDriver`) predates this and still drives
its roles through environment variables. It stays that way for now. The reusable workflow builds a project that is
already in the repo, and the package's generic game, the New Game template, only becomes a project once the wizard
has run. Locally, `tests/run-new-game.ps1 -Smoke` runs `mp-host game smoke` on a freshly generated game. New games use
the above.
