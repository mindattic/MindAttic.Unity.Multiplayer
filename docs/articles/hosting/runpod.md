# Hosting on RunPod (RunPod)

The WebSocket-transport profile (UM-T23): your server package runs as a single RunPod pod,
reachable over a RunPod-mapped TCP port. Unlike GenericVps, RunPod runs just the game server --
there's no rendezvous side-car in the pod, so you point it at a rendezvous service deployed
somewhere else (a GenericVps box, your own small VPS, or a shared one your studio already runs).

## What `mp-host deploy` does here

1. Builds (or reads an already-built) `RunPod`-profile server package and its Docker image, then
   pushes it to GHCR.
2. Calls RunPod's GraphQL API (`podFindAndDeployOnDemand`) to create a pod from that image, with
   the declared game port in its TCP port list and your rendezvous URL/secret as pod environment
   variables.
3. RunPod maps the declared port to a public one at pod creation -- there is no separate firewall
   step; `mp-host` logs that and moves on.
4. Polls the pod until RunPod reports it `RUNNING`.
5. Watches the pod's container logs for `[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ` (the same
   self-registration line GenericVps watches for) and prints it.

RunPod assigns the public IP and mapped port; the game server itself reads
`RUNPOD_PUBLIC_IP`/`RUNPOD_TCP_PORT_<port>` (UM-T13's `ServerAdvertisement`) to advertise the right
address to rendezvous, so you never have to wire that mapping through by hand.

## Before you deploy

You need a rendezvous service already reachable from the internet (deploy one via the GenericVps
profile, or point at a shared one) and a RunPod API key, stored in the vault:

```
mp-host credentials set-runpod --key <RunPod API key>
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
```

Then deploy:

```
mp-host deploy --profile RunPod --name ws-server --package ./build/RunPod \
  --image myorg/mygame --tag v1 \
  --rendezvous-url https://rendezvous.example.com \
  --public-address ws-server
```

`--public-address` is cosmetic here (RunPod's own `RUNPOD_PUBLIC_IP` is what the server actually
advertises); it's kept required across every profile so the same `--env` overrides work uniformly.

Add `--dry-run` first to see the exact GraphQL mutation mp-host would send, without creating
anything or spending RunPod credit.

## Updating and tearing down

RunPod pods are immutable once created -- there is no in-place container-image swap the way
GenericVps's `docker compose up -d` gives you. `mp-host update` therefore **terminates the old pod
and creates a new one**: players connected to the old pod are dropped during the swap, unlike
GenericVps's in-place restart. Plan updates for a quiet period, or run two pods behind your own
matchmaking if that's not acceptable. `mp-host destroy ... --yes` terminates the pod outright.

## Cost and sizing notes

RunPod bills by the second a pod runs, including CPU-only pods -- there's no need for a GPU
instance for a game server. Pick the smallest CPU/RAM tier your game needs and stop (don't just
leave running) pods between events if you're not hosting continuously; `mp-host destroy` is the
cheapest way to make sure nothing keeps billing after a test. Verify RunPod's current CPU-pod
pricing and TCP port-mapping limits against their live docs before committing to it for a public
launch -- this is also why the real-provider smoke test
(`tests/Hosting.Cli.Tests/Smoke/RunPodSmokeTests.cs`) is opt-in rather than part of every build: it
costs real money and depends on RunPod's schema not having drifted since this was written.
