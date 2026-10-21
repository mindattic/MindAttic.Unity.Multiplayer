# Hosting on AWS GameLift (GameLift)

A managed-fleet profile (UM-T30), and the odd one out in this table in two ways at once: it is the
only profile that is both provisioned once (a fleet that stays up, like GenericVps/RunPod/every
UM-T26-T28 profile) **and** allocated per match (like Edgegap/Gameye, UM-T29). mp-host's
`GameLiftProvisioner` uploads your build and creates the fleet; `Unity.Multiplayer.Core.GameLiftMatchAllocator`
then creates one game session (and one player session per seat) per match on that fleet, same as
every other `IMatchAllocator` adapter. The fleet itself is never a Docker container -- GameLift's
classic EC2 fleets run the build's executable directly.

## Three pieces

1. **Runtime** -- `GameLiftServerHost` (`unity/.../Runtime/Hosting/GameLift`) drives the GameLift
   Server SDK's lifecycle: `InitSDK`, `ProcessReady` (with `OnStartGameSession` calling
   `ActivateGameSession`, `OnProcessTerminate` and `OnHealthCheck`), `AcceptPlayerSession`/
   `RemovePlayerSession` as players join and leave, and `ProcessEnding` on a clean shutdown. It is
   compiled in only when the GameLift Server SDK package is installed (it defines
   `MINDATTIC_GAMELIFT`); `NetBootstrap` attaches it automatically, right after `ServerHost`, so a
   game adds nothing to its own code. Without the package installed, `GameLiftServerHost.Attach` is a
   documented no-op and the exact same build still runs fine on every other host in this table.
2. **mp-host** -- `GameLiftProvisioner` zips the `GameLift`-profile server package (no Docker image:
   `IProvisioner.RequiresContainerImage` is false for this one), uploads it as a GameLift build, waits
   for it to go `READY`, then creates a fleet of EC2 instances running it plus a simple-routing alias.
   This is a one-time (or per-update) step, exactly like provisioning a VPS for GenericVps.
3. **Matchmaking** -- `GameLiftMatchAllocator` (`Unity.Multiplayer.Core`) implements the UM-T29
   `IMatchAllocator` contract against the fleet mp-host created: `AllocateAsync` starts a game session
   and waits for it to go `ACTIVE`, publishing a join code the usual way; `CreatePlayerSessionAsync`
   (beyond the shared `IMatchAllocator` contract -- GameLift is the one provider here that needs a
   reservation per player before they connect) reserves a seat for one player.

## Setting up the Unity side

Install the GameLift Server SDK package in your project (from AWS's GameLift SDK download, added as
a local or Git package -- check the exact package id/version against what you installed and adjust
`Unity.Multiplayer.asmdef`'s `versionDefines` entry if it differs from `com.amazonaws.gamelift@5.0.0`,
which is what this integration was written against). Once it's installed, `MINDATTIC_GAMELIFT` is
defined automatically and `GameLiftServerHost` activates itself -- nothing else to wire up.

A player's `ConnectionPayload.PlayerToken` (the same field every profile already uses to give a
reconnecting player its seat back) doubles as the GameLift player session id when running under
GameLift: your matchmaker gets that id from `CreatePlayerSessionAsync` below and must hand it to the
player to send as their player token when they connect. `GameLiftServerHost` reads it back off the
connected client's seat (`NetSession.TryGetSeat`) to call `AcceptPlayerSession`/`RemovePlayerSession`
-- no new wire field, no change to connection approval.

## Setting up mp-host

```
mp-host credentials set-aws --access-key-id <AKIA...> --secret-access-key <secret> --region us-east-1
mp-host credentials set-cloud-options --profile GameLift --name my-fleet \
  --opt ec2InstanceType=c5.large --opt fleetType=ON_DEMAND --opt concurrentExecutions=1
```

The AWS credential is shared with `AwsEc2`/`AwsLightsail`. `ec2InstanceType` (default `c5.large`) and
`fleetType` (default `ON_DEMAND`; `SPOT` is cheaper but can be reclaimed) are optional, same as every
other launch option in this table. `concurrentExecutions` (default `1`) is how many game-server
processes run per instance; this provisioner only supports `1` cleanly, since more than one process
per instance needs each to be launched with a distinct port and `GameLiftProvisioner` does not attempt
that -- a later extension if you need the extra density.

Then build and deploy, from a `GameLift`-profile Unity build (`MindAttic > Multiplayer > Build Server
Package > GameLift`, or `-serverProfile GameLift` in batch mode):

```
mp-host deploy --profile GameLift --name my-fleet --package ./build/GameLift \
  --rendezvous-url n/a --rendezvous-secret n/a --public-address n/a
```

(`--rendezvous-url`/`--rendezvous-secret`/`--public-address` are required by mp-host's shared argument
parser for every profile, but GameLift ignores them: a matchmaker resolves each match's join code
directly from `GameLiftMatchAllocator`, never through the rendezvous service. Pass any placeholder.)
`--image`/`--tag` are accepted but unused too, since there is no Docker image for this profile.

Add `--dry-run` first to see every GameLift API call (`CreateBuild`, the S3 upload, `CreateFleet`,
`CreateAlias`, ...) mp-host would make, without spending anything. Build your package on a Linux or
macOS Unity Editor (or CI runner) if you can -- zipping on Windows loses the Unix executable bit on
`start.sh`/the dedicated-server binary that the Linux fleet needs, though `GameLiftProvisioner`
compensates by marking those two files executable in the zip's own metadata regardless of what bit
the filesystem had, so a Windows-built package still works.

`mp-host status`/`logs` work a little differently here: there is no single box to SSH into or
container to tail, so `logs` prints the fleet's own activation/capacity/error events instead, and the
deploy pipeline's join-code wait always times out with a warning -- there is no "the" join code for a
fleet, only one per game session once a matchmaker allocates one (see below). `mp-host update` is a
blue/green swap: it builds and uploads a new build, creates a new fleet from it, waits for `ACTIVE`,
then repoints your existing alias at the new fleet; the old fleet (and its build) are left running --
`mp-host destroy --server-id <old serverId> --yes` once you've confirmed the new one serves matches
correctly. `mp-host destroy` deletes the alias, fleet and build together.

## Allocating a match

```csharp
var api = new HttpGameLiftApiClient(accessKeyId, secretAccessKey, region); // the AWS account mp-host used
var joinCodes = new RendezvousJoinCodeService(new RendezvousClient(rendezvousUrl), rendezvousSecret, serverAdvertisement: null);
var allocator = new GameLiftMatchAllocator(api, joinCodes, fleetId: "fleet-0123456789abcdef0");

MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-42", maxPlayers: 8));
// hand allocation.Code (or allocation.Endpoint) to players who already have a seat reserved below ...

foreach (PlayerIdentity player in players)
{
    string playerSessionId = await allocator.CreatePlayerSessionAsync(allocation, player.PlayerToken);
    // tell this player to connect with PlayerToken = playerSessionId (overriding their usual reconnect token)
}

// ... match plays out; the server calls ProcessEnding itself (GameLiftServerHost) once it's done ...
await allocator.ReleaseAsync(allocation); // revokes the join code; GameLift has no API to force the session to end
```

`fleetId` is what `mp-host deploy` printed as part of its `serverId` (`GameLiftServerId.Split`); pass
an `aliasId` instead when you want the allocator to keep working across an `mp-host update`'s fleet
swap without changing. Note `ReleaseAsync` only revokes the join code -- GameLift has no API call to
force a game session to end from the outside, only the server process itself (via `ProcessEnding`,
`GameLiftServerHost`) actually does that, so tell the game to wrap up on its own terms (your own RPC
or admin command) rather than expecting `ReleaseAsync` to kill it.

## Cost notes

GameLift bills three things: the EC2 instance-hours behind your fleet (the same on-demand/spot rates
as plain EC2, plus a small per-vCPU-hour GameLift surcharge on top -- verify the current rate for your
instance family against the [GameLift pricing page](https://aws.amazon.com/gamelift/pricing/)), data
transfer out to players (the same roughly **0.07 GB per player-hour** rule of thumb as the `aws-ec2.md`
guide, at EC2's standard egress rate), and nothing extra for game/player sessions themselves -- those
are free; you only pay for the fleet capacity they run on. Unlike Edgegap/Gameye, a GameLift fleet
keeps costing money between matches the moment it is `ACTIVE`, because the instances are provisioned
up front, not spun up per match: size the fleet (instance type and count) for your expected concurrent
match count, and `mp-host destroy` a fleet you are not actively matchmaking onto, the same as you
would any other always-on profile in this table. Verify GameLift's current regions, instance families
and build/fleet API shape against their live docs before depending on this for a public launch -- this
is also why the real-provider smoke tests (`tests/Core.Tests/GameLiftSmokeTests.cs`,
`tests/Hosting.Cli.Tests/Smoke/GameLiftSmokeTests.cs`) are opt-in rather than part of every build.
