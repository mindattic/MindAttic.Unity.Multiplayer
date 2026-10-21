# Hosting on Edgegap (Edgegap)

The per-match allocation profile (UM-T29): instead of one server that stays up (GenericVps, RunPod
and the rest of the hosting table), Edgegap deploys one container **per match**, on demand, close to
the players in it, and you tear it down when the match ends. This is the right shape for a game that
runs short matches rather than a persistent world.

Unlike every other profile in this table, Edgegap is not driven by `mp-host deploy`/`status`/`logs`/
`update`/`destroy` -- there is no single long-running server for those commands to act on. Instead,
Core's `Unity.Multiplayer.Core.EdgegapMatchAllocator` implements the UM-T29 `IMatchAllocator`
contract: a matchmaker (your own backend, or a quick test harness) calls `AllocateAsync` when a match
is ready and `ReleaseAsync` once it ends.

## How it fits together

1. mp-host still builds the server package and pushes its image to GHCR, exactly as for every other
   profile (`ServerPackageBuilder` + `DockerCliImageTool`, UM-T23/UM-T25).
2. Once per build, call `EdgegapMatchAllocator.EnsureAppVersionAsync(imageReference, ports)`. This
   creates or updates the Edgegap app version that points at the pushed GHCR image and declares the
   ports the container exposes (the game port, named `"game"` by default).
3. Per match, your matchmaker calls `EdgegapMatchAllocator.AllocateAsync(new MatchRequest(matchId,
   maxPlayers, region))`. The allocator asks Edgegap to deploy that app version, polls until Edgegap
   reports the container running with its ports mapped, and returns a `MatchAllocation`: the
   endpoint (`JoinTarget`, a public IP and the external port Edgegap mapped to your game port) and a
   join code published through whatever `IJoinCodeService` you give the allocator (the self-hosted
   rendezvous service from UM-T12 works well here, since its codes are reachable by any client that
   knows the service's URL -- not just this process).
4. Hand the player the join code (or the endpoint directly); `NetSession.JoinAllocatedServerAsync`
   on the client side requests nothing itself -- it just joins what the allocation already resolved.
5. Once the match is over, call `EdgegapMatchAllocator.ReleaseAsync(allocation)`: it revokes the join
   code and stops the Edgegap deployment.

## Setting it up

```csharp
var api = new HttpEdgegapApiClient(apiToken); // from the Edgegap dashboard's API token page
var rendezvous = new RendezvousJoinCodeService(new RendezvousClient(rendezvousUrl), rendezvousSecret, serverAdvertisement: null);
var allocator = new EdgegapMatchAllocator(api, rendezvous, appName: "my-game", versionName: "v1.2.0");

await allocator.EnsureAppVersionAsync(
    "ghcr.io/myorg/mygame:v1.2.0",
    new[] { new EdgegapPortSpec("game", 7777, TransportKind.Udp) },
    environmentVariables: new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_URL"] = rendezvousUrl });

MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-42", maxPlayers: 8, region: "eu-west"));
// hand allocation.Code (or allocation.Endpoint) to players ...
// ... match plays out ...
await allocator.ReleaseAsync(allocation);
```

Store the API token in the vault, never the repo:

```
mp-host credentials set-edgegap --token <Edgegap API token> --app-name my-game
```

(`mp-host` itself does not call Edgegap's API -- `set-edgegap` only gives your matchmaking service,
or a one-off script, a place to read the token from instead of hard-coding it.)

## Cost and sizing notes

Edgegap bills for the time a deployment runs, by its resource tier (CPU/RAM) -- there is no "always
on" cost the way a VPS has, since a deployment only exists for the length of a match.
`EdgegapMatchAllocator.ReleaseAsync` stops billing for that match the moment it is called, so a
matchmaker that forgets to call it on every exit path (a crash, a dropped last player) is the one way
to leak cost here; wrap match lifecycles in a `finally` that always releases. Verify Edgegap's current
regions, resource tiers and app-version/deployment API shape against their live docs before depending
on this for a public launch -- this is also why the real-provider smoke test
(`tests/Core.Tests/EdgegapSmokeTests.cs`) is opt-in rather than part of every build: it costs real
money and depends on Edgegap's schema not having drifted since this was written.
