# Hosting on Google Compute Engine (Gce)

A big-cloud profile (UM-T27): `mp-host` creates a firewall rule it owns plus a GCE instance running
the same Docker image + rendezvous side-car compose stack as GenericVps, booted by a plain shell
`startup-script` (GCE's guest agent runs this literally rather than parsing it as cloud-init, unlike
the AWS and Azure profiles).

## What `mp-host deploy` does here

1. Builds (or reads an already-built) **GenericVps-profile** server package and image, pushes it to
   GHCR.
2. Renders the compose file + bootstrap script into a plain startup script and calls
   `instances.insert` with it as the `startup-script` metadata key, your chosen zone, machine type
   and source image, tagging the instance `mp-host-<server name>` for the firewall rule to target.
3. Calls `firewalls.insert` to create a rule (also named `mp-host-<server name>`) opening SSH
   (22/tcp), the rendezvous port (8080/tcp) and your declared game port to that tag.
4. Polls `instances.get` for a public (NAT) IP, then over SSH for
   `GET http://127.0.0.1:<healthPort>/healthz` once it's up, exactly like GenericVps.
5. Watches `docker compose logs game` over SSH for the join-code line and prints it.

## Before you deploy

You need a GCP project and an SSH key pair locally. Unlike the AWS profiles, mp-host does not
implement Google's service-account JWT signing flow itself (it would mean shipping its own RSA
signing code for a token you could otherwise mint in one command) -- it takes a short-lived OAuth2
access token directly, which you (or your CI) refresh before each deploy:

```
gcloud auth application-default login
gcloud auth print-access-token
```

```
mp-host credentials set-gce --project my-gcp-project --access-token ya29.A0ARrdaM9...
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
mp-host credentials set-cloud-options --profile Gce --name prod-gce \
  --opt sshUser=ubuntu --opt sshKeyPath=C:\Users\me\.ssh\id_ed25519 \
  --opt sshPublicKeyPath=C:\Users\me\.ssh\id_ed25519.pub
```

Access tokens typically expire in about an hour -- re-run `credentials set-gce` with a fresh one if
a deploy fails with an authentication error. `sourceImage` (default Ubuntu 22.04 LTS), `network`
(default `global/networks/default`) and `machineType` (default `e2-small`) are optional overrides --
see `GceProvisioner`'s class doc for the full list.

Then deploy:

```
mp-host deploy --profile Gce --name prod-gce --package ./build/GenericVps \
  --image myorg/mygame --tag v1 --region us-central1-a --size e2-small \
  --rendezvous-url https://rendezvous.example.com \
  --public-address prod-gce
```

(`--region` carries the GCE *zone*, e.g. `us-central1-a`, not a region -- GCE instances and
firewall-rule scoping are zonal.) Add `--dry-run` first to see every Compute Engine call
(`instances.insert`, `firewalls.insert`, ...) mp-host would make, without creating anything.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket) | Players connect here. |
| 8080 | TCP | The rendezvous service. |
| 22 | TCP | SSH -- the only way mp-host reaches the box after boot. |
| health port | TCP, bound to 127.0.0.1 | Not reachable from the internet by design. |

## Updating and tearing down

`mp-host update` restarts the compose stack in place over SSH, same as the other UM-T27 VM
profiles. `mp-host destroy ... --yes` stops the compose stack, deletes the instance, then deletes
the firewall rule (mp-host warns rather than fails if the rule delete needs a moment -- delete it
yourself if it's still there afterwards and nothing else uses it).

## Cost and data-transfer notes

GCE bills internet egress separately from the instance's own per-second billing, and the price
depends on destination and the monthly tier you're in -- check the
[Compute Engine network pricing page](https://cloud.google.com/vpc/network-pricing) for your
region's current rate (a commonly-cited starting rate for egress to the internet is roughly
$0.12/GB as of this writing, dropping at higher monthly volumes). Using the same rule-of-thumb send
rate as the AWS guides (roughly 20 KB/s per player -- replace with your own game's actual figure),
a player-hour is about 0.07 GB, which works out to on the order of **$0.008 per player-hour** in
data transfer at that rate, on top of the instance's own hourly cost (`e2-small` is a reasonable
default for a small lobby). `mp-host destroy` after each test run is the cheapest way to avoid
paying for an idle instance between events.
