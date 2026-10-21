# Hosting on Fly.io (Fly)

A UM-T28 profile: `mp-host` creates the app and its machine straight through Fly's Machines API
(`https://api.machines.dev/v1`) -- no `flyctl deploy`, no `fly.toml` required at deploy time, though
`FlyTomlGenerator` can still render one if you want a file to hand `flyctl`/Fly's dashboard later.
Fly's own edge only delivers UDP packets to a machine that binds the special hostname
`fly-global-services` (never `0.0.0.0`), and its free shared (anycast) IPv4 only proxies HTTP(S)/TCP
-- a UDP game port needs a dedicated IPv4, which this profile allocates for you.

## What `mp-host deploy` does here

1. Builds (or reads an already-built) server package and its Docker image, then pushes the image to
   GHCR.
2. Creates the Fly app (`POST /apps`, idempotent -- an app that already exists is left alone) under
   your org.
3. Allocates a dedicated IPv4 for the app (`POST /apps/{app}/ips {"type":"v4"}`).
4. Creates the game machine (`POST /apps/{app}/machines`) from the pushed image, with your declared
   game port in its `services` (protocol taken straight from the manifest -- `udp` for the default
   dedicated-server profile) and `MINDATTIC_BIND_HOST=fly-global-services` set automatically so the
   server binds the address Fly's edge actually delivers UDP to.
5. Creates the rendezvous machine, either as a second machine in the *same* app under the
   `rendezvous` process group (default), or as a wholly separate `<name>-rendezvous` app -- see
   "Rendezvous: process group or separate app" below.
6. Polls the game machine until Fly reports it `started`. mp-host runs outside Fly's private network
   (6PN), so this is "did the machine come up", not a curl against your health endpoint; a declared
   health port still gets a `tcp_checks` entry in the generated `fly.toml` for Fly's own health
   tracking.
7. Watches its logs (via `flyctl logs`, the one call in this profile that isn't the Machines API --
   see below) for the self-registration line (`[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ`) and
   prints it.

## Why `flyctl` shows up at all

Every other call in this profile -- create, status, update, destroy -- goes straight to the Machines
API over plain REST, mocked directly in NUnit like every other provider. Fly has no simple REST
endpoint for application logs (it ships them over its own NATS-based log stream); `flyctl logs` is
the one place this profile shells out to the `flyctl` CLI instead. Install and authenticate it
(`flyctl auth token`) on whatever machine runs `mp-host logs`/`mp-host deploy` if you want to see the
join code print automatically; `--dry-run` prints the command it would have run instead.

## Before you deploy

Create a Fly API token (`flyctl tokens create deploy` or the dashboard) and note your org's slug
(`personal` for a personal account). Store both, plus your GHCR token, in the vault -- never in the
repo, never on a shared command line:

```
mp-host credentials set-fly --token <Fly API token> --org personal
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
```

Then deploy:

```
mp-host deploy --profile Fly --name my-game-server --package ./build/GenericVps \
  --image myorg/mygame --tag v1 \
  --rendezvous-url http://my-game-server.fly.dev:8080 \
  --public-address <printed dedicated IPv4> --region iad --size shared-cpu-1x
```

`--public-address` has to be filled in after the first deploy prints the allocated dedicated IPv4 (or
scripted around it, the same two-step dance as any provider that assigns its address at create time).
`--region`/`--size` default to `iad` (Ashburn, Virginia)/`shared-cpu-1x` when omitted; known sizes are
`shared-cpu-1x`/`2x`/`4x`/`8x` and `performance-1x`/`2x`/`4x`. Fly app names must be globally unique
and DNS-safe (lowercase letters, digits, hyphens) -- `--name` becomes the app name verbatim. Add
`--dry-run` first to see every Machines API call mp-host would make, without creating anything or
spending money.

## Rendezvous: process group or separate app

Pick with the `rendezvousMode` launch option (`mp-host credentials set-cloud-options --profile Fly
--name my-game-server --opt rendezvousMode=app`, alongside any `--opt rendezvousImage=...`
override -- the default is `ghcr.io/mindattic/rendezvous:latest`):

- **`process-group`** (default): a second machine in the *same* Fly app, tagged with the
  `rendezvous` process group (visible as its own group in `fly status`/the dashboard). The Machines
  API lets each machine in an app carry its own image, so this machine runs the rendezvous image
  while the game machines run yours -- something a `fly.toml`-driven `flyctl deploy` can't express,
  since that path is limited to one image per app. One app, one bill, one region to manage.
- **`app`**: a wholly separate Fly app, `<name>-rendezvous`, with its own (shared, since rendezvous
  is TCP-only and doesn't need a dedicated address) IPv4 and its own lifecycle. Use this if you want
  to scale or redeploy rendezvous independently of the game app, or share one rendezvous app across
  several game-server apps. `mp-host destroy` tears both apps down together.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket) | Players connect here; declared in the game machine's `services` at creation -- Fly has no separate firewall step. |
| 8080 | TCP | The rendezvous service: players resolve join codes here; declared in the rendezvous machine's `services`. |
| health port | not exposed | No `[[services.ports]]` entry; only a `fly.toml` `tcp_checks` entry for Fly's own tracking. |

## Updating and tearing down

`mp-host update --profile Fly --name my-game-server --server-id my-game-server --image myorg/mygame
--tag v2 ...` finds the app's current game machine and replaces its config in place through the
Machines API -- Fly stops it and starts it again on the new image, keeping the same machine id (an
in-place rolling restart like GenericVps's `docker compose up -d`, not a RunPod-style
destroy-and-recreate), but still a brief connection drop since this profile runs one game machine per
app. Scale to two machines behind Fly's own load balancer first if a zero-downtime update matters
more than cost. `mp-host destroy --profile Fly --name my-game-server --server-id my-game-server --yes`
destroys every machine in the app, the app itself, and -- in `app` rendezvous mode -- the separate
rendezvous app too.

## Cost and sizing notes

`shared-cpu-1x` (1 shared vCPU, 256 MB RAM) is Fly's smallest size and fine for a small-lobby
real-time game server once you raise the memory with `--opt`-equivalent sizing if your game needs
more; step up to `shared-cpu-2x`/`4x` or a `performance-*` size for more players per instance. A
dedicated IPv4 carries its own small monthly charge (verify Fly's current price before committing) --
`mp-host destroy` releases it along with the app, so nothing keeps billing after a test. Verify Fly's
current Machines API field shapes, region list and pricing against their live docs before relying on
this for a public launch -- this is also why the real-provider smoke test
(`tests/Hosting.Cli.Tests/Smoke/FlySmokeTests.cs`) is opt-in rather than part of every build.
