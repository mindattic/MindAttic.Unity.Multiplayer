# Hosting on your own PC (Local)

The free/home option with no rented host at all (UM-T31): `mp-host local up`/`mp-host local down`
run the already-built server package as a Docker container on the machine `mp-host` itself runs on --
no GHCR push, no SSH, no cloud API, no credential to store. Other players on the same network join by
IP; `local up` also makes a best-effort attempt to forward the game port off the LAN via UPnP, with a
plain explanation when the router refuses.

## What `mp-host local up` does

1. Builds (or reads an already-built) **GenericVps-profile** server package -- the same Linux/Docker
   package every other profile uses; it runs under Docker Desktop (with WSL2 on Windows) the same way
   it would on a rented Linux VPS.
2. Finds this machine's own LAN IPv4 address(es) and prints them (`LAN join: connect by IP to
   192.168.1.42:7777`) -- `--public-address` overrides the guess if you have more than one active
   network adapter and want a specific one.
3. Renders a compose file that **builds from the package directory** (`build: <dir>`) instead of
   pulling a pushed `ghcr.io/...` image -- there is nothing to push to for a box that never leaves the
   LAN -- and runs `docker compose up -d --build`. The rendezvous service is included only when you
   pass `--rendezvous-url`/`--rendezvous-secret`; LAN-only play has no use for join codes.
4. Polls the health port directly over loopback HTTP (no SSH hop needed on the same machine).
5. Unless `--no-upnp` is given, discovers a UPnP Internet Gateway Device on the LAN (SSDP multicast,
   then a SOAP `AddPortMapping` call against its `WANIPConnection`/`WANPPPConnection` service) and asks
   it to forward the game port. See "UPnP: what succeeds and what doesn't" below.
6. Remembers the package directory, compose file name and whether a mapping was added, so `mp-host
   local down --name <server>` needs no other flags.

```
mp-host local up --name home --package ./build/GenericVps --public-address 192.168.1.42
```

or, to also be reachable beyond the LAN with a join code:

```
mp-host local up --name home --package ./build/GenericVps \
  --rendezvous-url http://192.168.1.42:8080 --rendezvous-secret <a long random string>
```

```
mp-host local down --name home
```

Both accept `--dry-run`, same as every other command, printing the compose/UPnP calls it would make
instead of making them.

## UPnP: what succeeds and what doesn't

A UPnP mapping only ever works when the router both supports UPnP **and** has it turned on -- `mp-host
local up` reports the outcome plainly either way, and a refusal is not an error:

- **Success:** the router accepted the mapping; mp-host also calls `GetExternalIPAddress` as a
  courtesy and prints it if the router answers.
- **No gateway answered the discovery broadcast:** the most common outcome on an **HOA-controlled or
  otherwise managed network** (RFC 0001's documented case for the owner's own home network) -- UPnP is
  simply off, by policy, at the router. Nothing is broken; there is nothing to retry.
- **The router answered but refused the mapping** (a SOAP fault, e.g. `Action Not Authorized`): UPnP
  is on but port forwarding is blocked by the router's own configuration/policy.

Either refusal means the same thing for players: **this listen server is reachable on the LAN by IP
only.** For a player off the LAN, use **Unity Relay** as the join-code provider for a listen server
(RFC 0001's default for exactly this case), or rent a dedicated host from elsewhere in this hosting
table -- a UPnP failure on a home network is the expected reason RFC 0001 gives Relay as the default
for listen servers in the first place, not a bug in this profile.

Pass `--no-upnp` to skip the three-second discovery attempt entirely once you already know your
network has none (most HOA/managed networks) -- `mp-host local down --name <server>` also skips
removing a mapping it never made.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket), bound to every interface | Players connect here, including over LAN by IP. |
| 8080 | TCP | The rendezvous service, only when `--rendezvous-url` was given. |
| health port | TCP, bound to 127.0.0.1 | Not reachable from the internet (or the LAN) by design. |

## Windows prerequisites

Docker Desktop with the WSL2 backend (the server package is always a Linux build, same as every other
profile) and, for a router with UPnP, nothing extra -- `mp-host local up` talks SSDP/SOAP directly, no
third-party UPnP client tool required.
