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
| Needs | `com.unity.services.multiplayer` installed, project linked to a Unity Cloud project with Relay on ([setup](#unity-relay-for-a-home-host-no-port-forwarding)) | `NetConfig.RendezvousUrl` pointed at a running `server/Rendezvous` instance |
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

## Unity Relay for a home host (no port forwarding)

Relay is what lets a player host from home, behind a router they can't forward ports on, and still hand friends a
code. It needs three things, and the package checks each one and says which is missing:

1. **The package.** `"com.unity.services.multiplayer": "2.4.1"` in `Packages/manifest.json` (it defines
   `MINDATTIC_UGS`; 2.4.1 is the version this library is verified with, `RelaySetup.PackageVersion`).
2. **A link to a Unity Cloud project.** The project needs a cloud project id. Unity bakes it into every build, and Unity
   Services reads it as `Application.cloudProjectId`.
3. **Relay turned on** for that Cloud project at [cloud.unity.com](https://cloud.unity.com/) (the project, then
   **Products ▸ Relay**).

Players need no Unity account: each game signs in to Unity Authentication **anonymously**.

### Setting it up

**In the editor:** **MindAttic ▸ Multiplayer ▸ New Game…** has a step 3, *Join codes without port forwarding*. It shows
where the project stands and has three buttons: **Add com.unity.services.multiplayer**, **Link project (Services
settings)** (opens **Edit ▸ Project Settings ▸ Services**, where you pick the organization and the Cloud project and
click Link) and **Unity Cloud dashboard**. **MindAttic ▸ Multiplayer ▸ Check Unity Relay Setup** logs the same status at
any time.

**From a script, with the editor closed:**

```powershell
# Link the project and add the package (find both ids at cloud.unity.com: the project's Settings, and the organization's)
powershell -File tools/link-unity-cloud.ps1 -Project D:\Projects\MindAttic\MyGame `
    -ProjectId 0f8fad5b-d9cb-469f-a165-70867728950e -OrganizationId 1234567890123 -ProjectName MyGame

# Only report where it stands (exit 0 when the package line and the link are both in place)
powershell -File tools/link-unity-cloud.ps1 -Project D:\Projects\MindAttic\MyGame -Check
```

The script writes `cloudProjectId`, `organizationId` and `projectName` into `ProjectSettings/ProjectSettings.asset`,
which is what the Services page's Link button writes, and adds the package line. It refuses while the editor has the
project open, because the editor would overwrite the file. Turning Relay on stays a dashboard switch.

**In CI or batch mode:** `Unity.exe -batchmode -projectPath <game> -executeMethod
Unity.Multiplayer.Editor.RelaySetupCheck.CheckBatch` logs the status and exits 0 only when the package is compiled in
and the project is linked. Add `-addRelayPackage` to add the package line first.

### At run time

- **Sign-in at startup.** The NetBootstrap's **Unity Relay** section has three fields:
  - **Relay Environment**: the Unity Services environment, `production` by default.
  - **Relay Region**: empty lets Relay pick the region closest to the host.
  - **Sign In At Startup**: on by default.

  With sign-in on, the scene initializes Unity Services in that environment and signs in anonymously as it starts, so
  **Host** and **Join by code** don't wait for it. A headless dedicated server doesn't sign in, and neither does a
  scene whose Join Codes is None or Rendezvous. The sign-in is shared by the whole process. If it fails, the first
  Relay call tries again. `RelayJoinCodeService.SignInAsync()` does the same from code.
- **Host.** With Join Codes at Auto and the package installed, `HostWithCode` (and the front end's **Host** button)
  creates a Relay allocation for `MaxPlayers - 1` players, starts the host on it (DTLS, or `wss` for WebSocket) and
  publishes Relay's code with the check character added.
- **Join by code.** A Relay-shaped code is joined through Relay's allocation, with no address involved.
- **When something is missing.** Nothing is sent to Unity Services when the project isn't linked: `HostWithCode`
  and `JoinByCode` fail at once with `RelaySetup.NotLinkedMessage`, which says how to link it. Other Unity
  Services failures become a `RelaySetupException` whose message names the cause: Relay is off for the project, the
  environment doesn't exist, or the game is offline. The drop-in front end shows these in the menu. **Host** toasts
  "No join code: ..." and hosts for joining by IP; **Join by code** shows a *Couldn't join* dialog
  (see [Front end](front-end.md)). `RelayJoinCodeService.Readiness` (`NotInstalled` / `NotLinked` / `Ready`) answers the
  same question in code.

### Verifying it

The PlayMode suite covers everything up to Unity Services without a Cloud project: the not-linked path for
`HostWithCode`/`JoinByCode` (`JoinByCodeTests`) and for the menus (`NetFrontEndTests`). A live Relay game needs a
linked project with Relay on. Build the game and run one copy on the home network and one on another, such as a
phone's hotspot. Click **Host** on the first, then type the lobby's code into **Join by code** on the second. The host's
Console logs "Signed in to Unity Services (environment production) for Relay join codes." when sign-in works.

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
