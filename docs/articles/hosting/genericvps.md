# Hosting on a generic VPS (GenericVps)

The default profile (UM-T23): your server package runs as a Docker container beside the
rendezvous service on any UDP-capable Linux VPS you already have -- Hetzner, Vultr, DigitalOcean,
OVHcloud, a home box behind a forwarded port, anything with SSH and a public IPv4 address. There is
no cloud API involved: `mp-host` (UM-T25) treats the box as already provisioned and configures it
over SSH.

## What `mp-host deploy` does here

1. Builds (or reads an already-built) server package and its Docker image, then pushes the image to
   GHCR (`ghcr.io/<owner>/<repo>:<tag>`).
2. Renders a `docker-compose.yml` that runs your pushed image beside `mindattic/rendezvous`
   (the join-code service from UM-T12), uploads it over SSH.
3. Uploads a one-shot bootstrap script that installs Docker if it's missing, logs into GHCR with a
   token piped over stdin (never on the command line, never written to the repo), pulls both images
   and runs `docker compose up -d`.
4. Best-effort opens the game port in `ufw` if it's present (`ufw allow <port>/udp`); this is your
   box, so if you manage the firewall some other way mp-host only warns, it does not fail the
   deploy.
5. Polls `GET http://127.0.0.1:<healthPort>/healthz` **on the box itself** over SSH (the compose
   file binds the health port to `127.0.0.1`, so it isn't reachable from the internet).
6. Watches `docker compose logs game` for the line the server itself prints once it has
   self-registered with rendezvous: `[MindAttic.Multiplayer] Join code: ABC-DEF-GHJ`, and prints it.

## Before you deploy

Rent a small VPS (1 vCPU / 1-2 GB RAM is plenty for most game servers) with Docker-capable Linux
and a public IPv4 address, and make sure you can SSH into it with a key (password auth is not
supported).

Store its connection details and your GHCR token in the vault -- never in the repo, never on a
shared command line:

```
mp-host credentials set-ssh --name prod-box --host 203.0.113.5 --user deploy --key C:\Users\me\.ssh\id_ed25519
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
```

Then deploy:

```
mp-host deploy --profile GenericVps --name prod-box --package ./build/GenericVps \
  --image myorg/mygame --tag v1 \
  --rendezvous-url http://203.0.113.5:8080 --rendezvous-secret <a long random string> \
  --public-address 203.0.113.5
```

Add `--dry-run` first to see every SSH command, upload and docker call mp-host would make without
touching the box.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket) | Players connect here. |
| 8080 | TCP | The rendezvous service: players resolve join codes here. |
| health port | TCP, bound to 127.0.0.1 | `GET /healthz`; not reachable from the internet by design. Open it yourself only for an outside monitor. |

## Updating and tearing down

`mp-host update --profile GenericVps --name prod-box --server-id deploy@203.0.113.5:22 --image myorg/mygame --tag v2 ...`
re-renders the compose file, pulls the new image and runs `docker compose up -d` in place -- an
in-place rolling restart, not a new box. `mp-host destroy ... --yes` runs `docker compose down -v`;
the VPS itself is yours to keep or cancel.

## Cost and sizing notes

Any provider's smallest CPU-only VPS tier is enough for most turn-based or small-lobby real-time
games (a handful of players, modest tick rate). Pick a region close to your players; UDP and
WebSocket game traffic is otherwise usually the cheapest part of the bill compared to CPU/RAM. Check
your provider's bandwidth allowance if you expect many concurrent matches -- a long-running Discord
voice-adjacent game server can add up over a month in a way a burst workload won't.
