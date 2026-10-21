# Votes

Most multiplayer games end up needing a vote: restart the round, skip the map, change the level, remove a griefer. The
votes kit is that whole feature: the rules in plain C#, a server-authoritative `VoteBoard` every peer sees, a built-in
kick, and a ready-made overlay with the question, the tally and Yes / No. A game only says which votes it offers and
what happens when one passes.

## Add it (one minute)

1. Select the GameObject with your `NetBootstrap` and **Add Component ▸ MindAttic ▸ Multiplayer ▸ Net Votes**.
2. Register your votes, on any peer, at any time (only the server's registrations matter):

   ```csharp
   NetVotes votes = NetVotes.Instance;
   votes.AddKind(new VoteKind("restart", "Restart the round?") { Passed = v => RestartRound() });
   ```

3. Start one from any peer, for example from a button:

   ```csharp
   NetVotes.Instance.Board.StartVote("restart");
   ```

That is all. With the [drop-in front end](front-end.md) in the scene, the overlay appears by itself in game: the
question, "Yes 1 · No 0 · needs 2 of 3 · 12 s", a bar towards the quorum, Yes and No, and a toast with the result.
Without the front end, see [Your own UI](#your-own-ui).

`NetVotes` registers the package's own `VoteBoard` prefab on every peer before the session starts, spawns the board on
the server once it runs, and serves it with the Inspector's rules. A dedicated server, a host and a client all run the
same scene.

## The rules

| Rule | Default | Inspector / code |
|---|---|---|
| Quorum | two thirds of the voters, rounded up: 1 of 1, 2 of 2, 2 of 3, 3 of 4, 4 of 5, 4 of 6 | **Quorum Fraction** / `VoteRules.QuorumFraction` |
| Time limit | 15 s, on the session's tick clock | **Timeout Seconds** / `VoteRules.TimeoutSeconds` |
| Cooldown | none; a player whose vote failed waits this long before starting another (others are not held up) | **Cooldown Seconds** / `VoteRules.CooldownSeconds` |
| Who may start | any player in the game | **Host Only** / `VoteRules.MayStart = id => ...` |
| The starter's vote | starting counts as Yes | `VoteRules.StarterVotesYes` |

And always:

- **One vote at a time.** Starting another while one runs is refused.
- **The roster is fixed when the vote starts.** A player who joins later does not vote on it.
- **It passes as soon as Yes reaches the quorum**, and is **rejected as soon as it no longer can**, even if every
  undecided voter said Yes. Nobody waits for the timer when the result is already certain.
- **A voter who leaves is dropped** and the quorum is recomputed from who is left, which can pass or reject the vote at
  once. A vote whose last voter leaves is abandoned.
- **Players may change their mind.** A second ballot replaces the first (the overlay disables its buttons once you
  have voted, as most games do).

## Vote kinds

A `VoteKind` is one vote a game offers. Only `id` and the question are required:

```csharp
board.AddKind(new VoteKind("level", r => "Switch to Level " + r.Number + "?")
{
    Description = r => levels.Name((int)r.Number),                                   // optional second line
    Check       = r => levels.Exists((int)r.Number) ? null : "There is no level " + r.Number + ".",
    MayVote     = (r, clientId) => true,                                              // who votes (default: everyone)
    Passed      = v => levels.Load((int)v.Number),                                    // runs on the server
    QuorumFraction = 0.5,                                                             // optional, this kind only
    TimeoutSeconds = 20,                                                              // optional, this kind only
});

board.StartVote("level", 3);                // a number payload
board.StartVote("map", text: "Docks");      // or a text payload
```

| Member | What it does |
|---|---|
| `Id` | What clients pass to `StartVote`. |
| `Title(request)` | The question every peer sees, worded on the server when the vote starts. |
| `Description(request)` | An optional second line. |
| `Check(request)` | Null when the request is fine, else the sentence the starter is told (a level that does not exist, a player who already left). |
| `MayVote(request, clientId)` | Who votes; the quorum counts only them. The kick vote leaves its target out. |
| `Passed(vote)` | What happens on the server when it passes, before any peer is told it passed. |
| `QuorumFraction`, `TimeoutSeconds` | This kind's own rules instead of the board's. |

A request carries the kind's id, who asked (`StartedBy`, filled in by the server), and an optional `long Number` and
`string Text`. Everything the server works out (title, description, quorum) reaches every peer, so clients need no
registration of their own.

## Kick

The board has a built-in kick vote. `NetVotes` adds it unless **Kick Votes** is off; in code, `board.AddKickKind(reason)`.

```csharp
board.StartKickVote(clientId);   // "Kick Bob?"
```

Bob does not vote on his own kick, so with three players the two others must both say Yes. The host's own player and
the starter cannot be its target, and a player who has already left is refused. When it passes, the server removes the
player with `NetSession.Kick`; they see the front end's "Removed from the game" dialog with the reason.

## What every peer sees

```csharp
VoteBoard board = VoteBoard.For(session);   // or NetVotes.Instance.Board

board.Status        // VoteStatus or null: Kind, Title, Description, Number, Text, StartedBy, Yes, No, Voters, Quorum, SecondsLeft
board.IsVoteOpen
board.LocalChoice   // Undecided, Yes, No, or NotAVoter (joined after it started, or left out by the kind)
board.ChoiceOf(clientId)

board.VoteChanged += status => { };         // at most once a frame
board.VoteEnded   += result => { };         // VoteResult: Ending (Passed, Rejected, TimedOut, Abandoned, Cancelled), counts
board.VoteRefused += (why, reason) => { };  // only on the peer whose StartVote was refused
board.CastVote(true);
```

On the server, `board.Box` is the Core `VoteBox` itself, `board.CancelVote()` calls a vote off, and `board.Rules` holds
the rules.

## Your own UI

The overlay is `Unity.Multiplayer.UI.VoteOverlay` (MindAttic Components required). It is wired for you when the scene has
a `NetFrontEnd` with menus, and only shows in game. Without a front end, create it yourself:

```csharp
VoteBoard.Spawned += board => { if (board.Session.IsClient) VoteOverlay.Create(board); };
```

`VoteOverlay.AutoWire = false` turns the automatic one off; `VoteOverlay.Created` lets you move it (`TopOffset`), hide it
(`ShowWhen`) or turn off its toasts. A game with its own UI binds to `board.Status` and uses Core's `VoteText` for the
same words: `VoteText.Tally(status)`, `VoteText.Standing(choice)`, `VoteText.Ended(result)`, `VoteText.Refused(refusal)`.

UI code that must not reference Netcode for GameObjects (like the overlay itself) binds to `IVoteBoard`, which
`VoteBoard` implements, and finds boards through `VoteBoards.Spawned`, `VoteBoards.Despawned` and `VoteBoards.For(session)`.

## Without NetVotes

`NetVotes` is three calls you can make yourself:

```csharp
VoteBoard.RegisterPrefab(session);                    // every peer, before the session starts
VoteBoard board = VoteBoard.Spawn(session, rules);    // the server, once it runs
board.AddKind(...).AddKickKind();
```

`VoteBox` (Core, engine-free) is the rules alone, for a bot, a single-player mode or a test:
`new VoteBox(clock, rules)`, `Register(kind)`, `Start(new VoteRequest("restart", playerId), roster)`, `Cast`,
`RemoveVoter`, `Cancel`, and the `Changed` / `Ended` events.

## Where it is tested

- `dotnet test tests/Core.Tests` (`VoteBoxTests`): the quorum table and custom fractions, passing, early rejection,
  timeouts and the countdown, departures, one vote at a time, who may start, the cooldown, payloads, checks, voter
  filters, per-kind rules, cancel, and the overlay's words.
- `tests/run-playmode.ps1`: `VoteBoardTests` (a host and two clients over real Unity Transport: a number payload passing
  and its effect on the server, a text payload with a description, early rejection, a timeout on a stepped clock, a
  player leaving mid-vote, refusals heard only by the starter, a kick vote removing its target, host-only starts),
  `NetVotesTests` (the component beside a real `NetBootstrap`) and, in `Unity.Multiplayer.UI.Tests`,
  `VoteOverlayTests` (the overlay showing a vote and passing it from its Yes button, toasts, wiring itself with a front
  end in game).
