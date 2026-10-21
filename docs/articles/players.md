# Players: names, colours and the roster

Every multiplayer game needs to know who is playing: a name over each character, a colour for each team mate, a
scoreboard, a ping column, a "you" marker. `NetRoster` is that list, on every peer, kept in step by the server. A game
never copies names and colours onto its own networked objects to get them to the clients.

```csharp
NetRoster roster = session.Roster;          // or NetRoster.Current, for NetBootstrap.Instance's session

foreach (NetPlayer player in roster.All)    // by seat
{
    Debug.Log(player.DisplayName + " " + PlayerColors.ToHex(player.Color) + " " + player.PingMs + " ms"
        + (player.IsHost ? " (host)" : "") + (player.IsLocal ? " (you)" : ""));
}

NetPlayer me = roster.Local;                // null on a dedicated server
roster.TryGet(OwnerClientId, out NetPlayer owner);  // by connection
roster.TryGetBySeat(seatIndex, out NetPlayer inSeat); // by seat: stable across a reconnect

roster.Joined  += player => AddNameTag(player);
roster.Left    += player => RemoveNameTag(player);
roster.Changed += change =>
{
    if (change.OnlyPingChanged) return;     // the round trip is re-measured every second
    UpdateNameTag(change.Player);           // a rename, a new colour, dropped or back
};
```

## What a player record holds

| `NetPlayer` | What it is |
|---|---|
| `Seat` | The seat index. It identifies the player for as long as the seat lives, across reconnects. |
| `ClientId` | The connection the player is on now. It changes when they reconnect. |
| `DisplayName` | The name everyone sees. |
| `Color` | The colour everyone draws them in: their own, or their seat's palette colour if they never picked one. `player.UnityColor()` gives a `Color32`. |
| `IsHost` | True for a listen server's own player. |
| `IsConnected` | False while they have dropped and their seat is held for them (see [Reconnect and seats](reconnect-seats.md)). |
| `PingMs` | Round trip to the server in milliseconds (0 for the host), or `NetPlayer.UnknownPing` (-1) until measured. |
| `IsLocal` | True on the peer whose own player this is. |

The record is immutable; the roster hands out a new one whenever something changes, and `Changed` carries the record
before and after (`NameChanged`, `ColorChanged`, `ConnectionChanged`, `OnlyPingChanged`).

## Where the name and colour come from

A player sends their name and colour in the [connection approval payload](reconnect-seats.md#connection-approval)
(`NetSession.Identity`). The [drop-in front end](front-end.md) fills that from an `IPlayerIdentitySource`, by default
the player's MindAttic profile (`Profiles.Current.Settings.Common.DisplayName` and `.Color` from Unity.Shared). The
server puts them on the player's seat and builds the roster from the seats:

- the name is cleaned with `PlayerNames.Clean` (invisible characters dropped, single spaces, at most 32 characters),
  so an odd name never stops anyone from joining;
- a player who never picked a colour (the default white, `PlayerColor.White`) is drawn in their seat's colour from
  `PlayerColors.Palette`, eight colours that read well on dark and light, so neighbours never share one.

## How it reaches every peer

The server sends the whole roster to each connection once it is in (a late joiner and a returning player get
everything), then only what changes. It travels in named messages beside the game's own traffic, so there is no prefab
or `NetworkObject` to register and it works in any scene. The server measures each client's round trip once a second
and updates `PingMs` when it moves by 5 ms or more. When the session ends, the peer's roster empties and `Left` fires
for each player.

A player who drops keeps their record, `IsConnected` false, for as long as their seat is held. When they come back
with the same player token they get the same seat and the same record (with their new `ClientId`): other peers see a
`Changed`, never a `Left` and `Joined`.

## Changing a name or colour mid-game

```csharp
PlayerChangeResult result = await session.Roster.RequestChange("Bobby", PlayerColors.Palette[2]);
if (!result.Accepted)
{
    ShowToast(result.Reason);               // "A name cannot contain invisible or unprintable characters."
}
```

Pass `null` for the part that stays. The server is the judge ([UM-LAW-1](../BIBLE.md#UM-LAW-1)); it checks every
request with `PlayerChangeRequest.Validate`:

- **Name** (`PlayerNames.Validate`): 1 to 32 characters; letters and digits in any script, combining marks, punctuation
  and symbols (emoji too); single plain spaces between words. Refused: an empty name, a leading or trailing space, two
  spaces in a row, other kinds of blank space and line breaks, control and invisible formatting characters (zero-width
  spaces and joiners, direction overrides), private-use and unassigned code points, broken surrogate pairs.
- **Colour** (`PlayerColors.Validate`): fully opaque. `PlayerColors.TryParse("#3498DB")` reads `#RRGGBB` or
  `#RRGGBBAA`.
- **Rate**: at most 5 changes at once, then one a second, per player.

An accepted change is kept with the seat and in the player's own `NetSession.Identity`, so a reconnect brings it back,
and every peer gets a `Changed`. The answer arrives after the roster update, so `result.Player` and `roster.Local`
already show the new name. A host's own change is applied at once.

The front end does this for you: its Settings screen, opened mid-game, asks the server, shows a refusal as a toast,
and saves an accepted change back to the player's `IPlayerIdentitySource` (their MindAttic profile by default). It saves
any accepted change of the local player's name or colour the same way, including one a game makes with
`RequestChange`.

## The rules are plain C#

`PlayerNames`, `PlayerColors`, `PlayerRoster`, `RosterWire`, `NetPlayer` and `PlayerChangeRequest` live in the
engine-free Core assembly, so a game's own tools and tests can use them without Unity.

## Where it is tested

- `dotnet test tests/Core.Tests`: `PlayerNamesTests` (accepted and refused names, cleaning), `PlayerColorsTests`
  (palette, seat colours, hex, opacity, change requests), `PlayerRosterTests` (joined, left and changed from a
  replacement, snapshots, held seats, `NetPlayer.FromSeat`, a seat keeping a rename) and `RosterWireTests` (round
  trips, malformed bytes refused without throwing).
- `tests/run-playmode.ps1`, `RosterTests`: a host and two clients over real Unity Transport. Names and colours on every
  peer, including a late joiner; round trips measured; a rename reaching everyone; bad names and a see-through colour
  refused by the server; a reconnect keeping seat, name and colour; a kick removing the player everywhere.
  `NetFrontEndTests`: the lobby follows the roster, and a rename from the in-game Settings reaches the host's lobby and
  is saved to the player's identity once accepted.
