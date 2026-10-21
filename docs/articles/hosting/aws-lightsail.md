# Hosting on AWS Lightsail (AwsLightsail)

The simpler of the two AWS profiles (UM-T27): one Lightsail instance running the same Docker image
+ rendezvous side-car compose stack as GenericVps, booted by a cloud-init document passed as
`userData`. Lightsail has no separate security-group resource the way EC2 does -- each instance
carries its own built-in firewall, opened with `PutInstancePublicPorts` -- so there is one fewer
resource for `mp-host` to create and tear down than `AwsEc2`.

## What `mp-host deploy` does here

1. Builds (or reads an already-built) **GenericVps-profile** server package and image (same package
   shape as every other Linux-VM profile), pushes it to GHCR.
2. Renders the compose file + bootstrap script into a `#cloud-config` document and calls
   `CreateInstances` with it as `userData`, your chosen blueprint (OS image), bundle (size) and key
   pair.
3. Calls `PutInstancePublicPorts`, which **replaces** the instance's whole port list, to open SSH
   (22/tcp), the rendezvous port (8080/tcp) and your declared game port.
4. Polls `GetInstance` for a public IP, then over SSH for `GET http://127.0.0.1:<healthPort>/healthz`
   once it's up, exactly like GenericVps.
5. Watches `docker compose logs game` over SSH for the join-code line and prints it.

## Before you deploy

You need an AWS account and a Lightsail key pair already created in the region/zone you'll deploy
to (Lightsail's own console can create one, or reuse an EC2 key pair imported into Lightsail), plus
the matching private key locally.

```
mp-host credentials set-aws --access-key-id <AKIA...> --secret-access-key <secret> --region us-east-1
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
mp-host credentials set-cloud-options --profile AwsLightsail --name prod-lightsail \
  --opt keyPairName=my-lightsail-keypair --opt availabilityZone=us-east-1a \
  --opt sshUser=ubuntu --opt sshKeyPath=C:\Users\me\.ssh\my-lightsail-keypair.pem
```

The AWS credential is shared with the `AwsEc2` profile. `blueprintId` (default `ubuntu_22_04`) and
`bundleId` (default `nano_3_0`, Lightsail's cheapest tier) are optional overrides -- see
`LightsailProvisioner`'s class doc for the full list.

Then deploy:

```
mp-host deploy --profile AwsLightsail --name prod-lightsail --package ./build/GenericVps \
  --image myorg/mygame --tag v1 --region us-east-1 \
  --rendezvous-url https://rendezvous.example.com \
  --public-address prod-lightsail
```

Add `--dry-run` first to see every Lightsail action (`CreateInstances`, `PutInstancePublicPorts`,
...) mp-host would make, without creating anything.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket) | Players connect here. |
| 8080 | TCP | The rendezvous service. |
| 22 | TCP | SSH -- the only way mp-host reaches the box after boot. |
| health port | TCP, bound to 127.0.0.1 | Not reachable from the internet by design. |

## Updating and tearing down

`mp-host update` restarts the compose stack in place over SSH, same as GenericVps and `AwsEc2` --
the instance persists across an image swap. `mp-host destroy ... --yes` stops the compose stack
then deletes the instance; its built-in firewall goes with it, so there is no second resource to
clean up the way EC2's security group needs.

## Cost and data-transfer notes

Lightsail's pricing is simpler than EC2's: each bundle (size tier) bundles a fixed monthly data
transfer allowance (for example, the cheapest Linux bundle typically includes on the order of 1 TB/
month as of this writing), with overage billed per GB once you exceed it -- check the
[Lightsail pricing page](https://aws.amazon.com/lightsail/pricing/) for your bundle's current
allowance and overage rate. Using the same rule-of-thumb send rate as the `AwsEc2` guide (roughly
20 KB/s per player, which you should replace with your own game's actual figure), a player-hour
costs about **0.07 GB** of transfer. At a typical ~1 TB/month bundle allowance, that's enough for
roughly **14,000 player-hours a month before you pay anything extra** for transfer -- comfortably
more than most small-to-medium game servers need, which is Lightsail's main appeal over EC2's
separately-metered egress. `mp-host destroy` after each test run keeps you from paying for an idle
instance between events.
