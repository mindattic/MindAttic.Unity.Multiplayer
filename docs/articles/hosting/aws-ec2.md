# Hosting on AWS EC2 (AwsEc2)

A big-cloud profile (UM-T27): `mp-host` creates a VPC security group it owns, then an EC2 instance
running the same Docker image + rendezvous side-car compose stack as the GenericVps profile, booted
by a cloud-init document passed as the instance's `UserData`. Unlike GenericVps, there is no
bring-your-own box: `mp-host` creates and destroys the instance and its security group itself.

## What `mp-host deploy` does here

1. Builds (or reads an already-built) **GenericVps-profile** server package and its Docker image
   (EC2 instances run the exact same compose stack as a plain VPS -- `AwsEc2` only changes how the
   box and its firewall are created, not the shape of the package Unity builds), then pushes the
   image to GHCR.
2. Calls EC2's `CreateSecurityGroup` to make an empty security group named `mp-host-<server name>`.
3. Renders the compose file + bootstrap script (the same ones GenericVps uploads over SSH) into a
   `#cloud-config` document and launches the instance (`RunInstances`) with it as `UserData`, your
   chosen AMI and instance type, and the new security group attached.
4. Calls `AuthorizeSecurityGroupIngress` to open SSH (22/tcp, the only way `mp-host` can reach the
   box afterwards), the rendezvous port (8080/tcp) and your declared game port.
5. Polls `DescribeInstances` over SSH for `GET http://127.0.0.1:<healthPort>/healthz` once the
   instance has a public IP, exactly like GenericVps.
6. Watches `docker compose logs game` over SSH for the join-code line and prints it.

## Before you deploy

You need an AWS account, an EC2 key pair already created in the region you'll deploy to, and the
matching private key locally. AMI ids are region-specific, so `mp-host` does not guess one --
look up a current Ubuntu 22.04 LTS AMI id for your region (the
[Canonical AMI locator](https://cloud-images.ubuntu.com/locator/ec2/)) before your first deploy.

```
mp-host credentials set-aws --access-key-id <AKIA...> --secret-access-key <secret> --region us-east-1
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
mp-host credentials set-cloud-options --profile AwsEc2 --name prod-ec2 \
  --opt amiId=ami-0abcdef1234567890 --opt keyName=my-ec2-keypair \
  --opt sshUser=ubuntu --opt sshKeyPath=C:\Users\me\.ssh\my-ec2-keypair.pem
```

The AWS credential is shared with the `AwsLightsail` profile; the launch options above are per
server name and are what `ProviderConnection.ProviderOptions` carries into `Ec2Provisioner` --
see its class doc for every recognised key (`vpcId`/`subnetId`/`instanceType`/`rendezvousImage` are
all optional).

Then deploy:

```
mp-host deploy --profile AwsEc2 --name prod-ec2 --package ./build/GenericVps \
  --image myorg/mygame --tag v1 --region us-east-1 --size t3.micro \
  --rendezvous-url https://rendezvous.example.com --rendezvous-secret <a long random string> \
  --public-address prod-ec2
```

Add `--dry-run` first to see every EC2 API call (`CreateSecurityGroup`, `RunInstances`,
`AuthorizeSecurityGroupIngress`, ...) mp-host would make, without spending anything.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket) | Players connect here. |
| 8080 | TCP | The rendezvous service. |
| 22 | TCP | SSH -- the only way mp-host reaches the box after boot; mp-host opens it to `0.0.0.0/0` by default. Tighten the security group yourself afterwards if you want SSH restricted to your own IP. |
| health port | TCP, bound to 127.0.0.1 | Not reachable from the internet by design. |

## Updating and tearing down

`mp-host update` re-renders the compose file and restarts it in place over SSH -- the instance
itself persists across an image swap, exactly like GenericVps's rolling restart; there is no
instance replacement the way RunPod's pod-swap works. `mp-host destroy ... --yes` stops the compose
stack, terminates the instance, then deletes the security group (EC2 sometimes needs a few seconds
after termination before the group can be deleted; mp-host warns rather than fails if that happens
-- delete it yourself once termination finishes).

## Cost and data-transfer notes

EC2 bills the instance by the hour/second **and** its internet egress separately -- the game
traffic flowing out to every connected player counts against the second meter. As a rule of thumb:
a small-scale real-time game server sending each player roughly 20 KB/s (plan for your own game's
actual send rate -- this varies a lot by genre and tick rate) works out to about **0.07 GB per
player-hour**. At EC2's typical US-region on-demand egress price of roughly $0.09/GB (verify your
region's current price against the [EC2 pricing page](https://aws.amazon.com/ec2/pricing/on-demand/)
-- this changes and the first 100 GB/month account-wide is often free), that's on the order of
**$0.006 per player-hour** in data transfer alone, on top of the instance's own hourly rate (a
`t3.micro` is a reasonable default for a small lobby). Multiply by your expected concurrent players
and hours of operation to budget a month, and remember `mp-host destroy` is the cheapest way to
make sure nothing keeps billing after a test.
