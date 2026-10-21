# Join codes

A join code is a short, typeable string that resolves to a server — `ABC-DEF-GHJ` — so players never type an
IP address. One interface, two implementations, and `JoinCodeProvider.Auto` picks the right one for the
topology.

## The format

A code is 8 data characters plus one check character, written as three groups of three (`ABC-DEF-GHJ`):

- **Alphabet:** `23456789ABCDEFGHJKLMNPQRSTUVWXYZ` — 32 symbols, 5 bits each, 40 bits of data. It never contains
  `0`, `O`, `1` or `I`, so nothing in a code is ambiguous to read aloud or copy down.
- **Check character:** a Damm check over GF(32). It catches every single-character substitution and every swap
  of two characters next to each other or one apart, anywhere in the code, the check character included.
- **Typo tolerance:** parsing ignores case, whitespace, and `-`/`_`/`.` separators (and Unicode dashes). If
  someone types a look-alike character that can't legally appear (`0`/`O`, `1`/`I`), the parser tries the
  glyphs it's usually mistaken for and accepts the one the check character confirms.

`JoinCode.TryParse` does all of this; a game never needs its own normalization.

## The two providers

| | Unity Relay | Rendezvous service |
|---|---|---|
| Gets through home-router NAT | Yes | No — needs a reachable address |
| Needs | `com.unity.services.multiplayer` installed, project linked to Unity Cloud | `NetConfig.RendezvousUrl` pointed at a running `server/Rendezvous` instance |
| Default for | Listen server (`Topology.Host`), when installed | Dedicated server (`Topology.Server`); a listen server without Relay |
| Implementation | `RelayJoinCodeService` | `RendezvousJoinCodeService` (Core, engine-free) over `RendezvousClient` |

Both implement the same engine-free [`IJoinCodeService`](xref:Unity.Multiplayer.Core.IJoinCodeService)
contract (`PublishAsync` → a code, `ResolveAsync` → a target or null, idempotent `RevokeAsync`), so a session
doesn't need to know which one is live.

`NetConfig.JoinCodes` (or the `NetBootstrap` Inspector field) chooses:

```csharp
public enum JoinCodeProvider
{
    Auto,        // the topology's default: Relay (else the rendezvous URL) for Host, Rendezvous for Server
    None,        // join by address only
    Relay,
    Rendezvous,
}
```

`JoinCodeDefaults.ForPublishing(topology, configured)` is the rule `Auto` expands to: Relay for a Host,
Rendezvous for a Server, and `None` for a Client (a client never publishes a code). A listen server refines it with
what is set up, through `JoinCodeDefaults.HostingOrder(configured, relayInstalled, hasRendezvousUrl)`: `Auto` tries
Relay first when `com.unity.services.multiplayer` is installed, then the rendezvous service when `RendezvousUrl` is
set. `HostWithCode` uses the first of those (`ForHosting`), and the drop-in front end's **Host** tries each in turn
before hosting for joining by IP. An explicit `Relay` or `Rendezvous` is kept as chosen. A dedicated server with a
rendezvous URL registers itself as soon as it's `Running`, and re-registers with a new code if the service ever
forgets it (`RegistrationLost`).

## Using it

```csharp
// Host: publish a code (Relay by default)
JoinCode code = await bootstrap.HostWithCode();
string text = session.PublishedCodeText; // "ABC-DEF-GHJ"

// Client: resolve and join
bool joined = await bootstrap.JoinByCode("abc-def-ghj"); // case and dashes don't matter
```

`JoinByCode` tries the rendezvous service first when `RendezvousUrl` is set, then Relay when the package is
installed and the code is Relay-shaped (Unity Relay's own 6–12 character codes are written in a subset of this
alphabet, so a Relay code plus a check character is a valid join code). The transport used to connect comes
from whichever registration resolves the code. `NetSession.JoinCodePublished` / `JoinCodeFailed` report success
or a message to show; a session withdraws its code when it ends.

## The rendezvous service

`server/Rendezvous` (ASP.NET Core, .NET 10 minimal API) maps a code to a dedicated server's IP:port. A
dedicated server calls `POST /v1/register` with its registration secret and gets back a code, a private token
and a TTL (default 60 seconds, renewed by a heartbeat every third of it); `GET /v1/resolve/{code}` is what a
client calls to get the address back. Run it yourself:

```bash
docker build -f server/Rendezvous/Dockerfile -t mindattic-rendezvous .
docker run -p 8080:8080 -e Rendezvous__RegistrationSecret=<secret> mindattic-rendezvous
```

See [Hosting](hosting.md#tls) for TLS and the full secrets list, and
[the no-port-forwarding path](hosting.md#the-no-port-forwarding-path) for when Relay is the only option that
works at all (an HOA-controlled network with no port forwarding).

## No join codes at all

Set `JoinCodeProvider.None` (or leave Relay/the rendezvous service unconfigured) and players join by
`JoinByAddress(ip, port)` only — useful for a LAN party or when a matchmaking service already knows the
address (see `JoinAllocatedServerAsync` in [Hosting](hosting.md#per-match-allocation-instead-of-one-always-on-box)
for servers that are allocated per match rather than published as one long-lived code).
