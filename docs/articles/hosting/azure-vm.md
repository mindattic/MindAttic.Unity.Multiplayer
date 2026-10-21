# Hosting on an Azure VM (AzureVm)

A big-cloud profile (UM-T27): `mp-host` creates the four ARM resources a Linux VM needs -- a network
security group (NSG), a static public IP, a network interface (NIC) wiring them to an existing VNet
subnet, and the VM itself -- running the same Docker image + rendezvous side-car compose stack as
GenericVps, booted by a cloud-init document passed as `customData`.

## What `mp-host deploy` does here

1. Builds (or reads an already-built) **GenericVps-profile** server package and image, pushes it to
   GHCR.
2. Creates an empty NSG, a static public IP (Azure assigns this address immediately -- before the VM
   that will use it even exists, unlike AWS/GCE where the IP only appears once the instance is
   running) and a NIC linking the IP, the NSG and your existing VNet subnet.
3. Renders the compose file + bootstrap script into a `#cloud-config` document and creates the VM
   (`virtualMachines` PUT) attached to that NIC, with your chosen size and an SSH public key for
   login (password authentication is disabled).
4. Authorizes NSG rules for SSH (22/tcp), the rendezvous port (8080/tcp) and your declared game
   port.
5. Polls the public IP (already known from step 2) over SSH for
   `GET http://127.0.0.1:<healthPort>/healthz`, exactly like GenericVps.
6. Watches `docker compose logs game` over SSH for the join-code line and prints it.

## Before you deploy

You need an Azure subscription, a resource group, and **an existing VNet + subnet** -- mp-host
creates the NSG/IP/NIC/VM but deliberately does not create a VNet, since most teams already have one
per environment and a profile silently creating network topology alongside a server deploy is more
surprising than helpful. You also need a service principal (an Entra ID app registration with
Contributor on the resource group) for mp-host's client-credentials login, and an SSH key pair
locally:

```
az ad sp create-for-rbac --name mp-host --role Contributor --scopes /subscriptions/<sub>/resourceGroups/<rg>
```

```
mp-host credentials set-azure --tenant <tenant-id> --client-id <app-id> --client-secret <password> --subscription <sub-id>
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
mp-host credentials set-cloud-options --profile AzureVm --name prod-azure \
  --opt resourceGroup=mp-host-rg \
  --opt subnetId=/subscriptions/<sub>/resourceGroups/mp-host-rg/providers/Microsoft.Network/virtualNetworks/vnet/subnets/default \
  --opt sshUser=azureuser --opt sshKeyPath=C:\Users\me\.ssh\id_ed25519 \
  --opt sshPublicKeyPath=C:\Users\me\.ssh\id_ed25519.pub
```

`vmSize` (default `Standard_B1s`) and `location` (default `eastus`, or pass `--region` on deploy)
are optional overrides -- see `AzureVmProvisioner`'s class doc for the full list.

Then deploy:

```
mp-host deploy --profile AzureVm --name prod-azure --package ./build/GenericVps \
  --image myorg/mygame --tag v1 --region eastus --size Standard_B1s \
  --rendezvous-url https://rendezvous.example.com --rendezvous-secret <a long random string> \
  --public-address prod-azure
```

Add `--dry-run` first to see every ARM call (NSG, public IP, NIC, VM PUTs) mp-host would make,
without creating anything.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket) | Players connect here. |
| 8080 | TCP | The rendezvous service. |
| 22 | TCP | SSH -- the only way mp-host reaches the box after boot. |
| health port | TCP, bound to 127.0.0.1 | Not reachable from the internet by design. |

## Updating and tearing down

`mp-host update` restarts the compose stack in place over SSH, same as the other UM-T27 VM
profiles. `mp-host destroy ... --yes` stops the compose stack, then deletes the VM, its NIC, and
finally its public IP and NSG, in that order -- ARM resources reference each other (the NIC
references the IP and NSG; the VM references the NIC), so deleting out of order fails. mp-host warns
rather than fails if the IP/NSG delete needs a moment for the NIC deletion to finish propagating --
delete them yourself afterwards if they're still there and nothing else uses them.

## Cost and data-transfer notes

Azure bills outbound data transfer separately from VM compute time, with the rate depending on
region and monthly volume -- check the
[Azure bandwidth pricing page](https://azure.microsoft.com/en-us/pricing/details/bandwidth/) for
your region's current tier (a commonly-cited starting rate is roughly $0.087/GB for the first 10 TB/
month as of this writing). Using the same rule-of-thumb send rate as the other UM-T27 guides
(roughly 20 KB/s per player -- replace with your own game's actual figure), a player-hour is about
0.07 GB, which works out to on the order of **$0.006 per player-hour** in data transfer at that
rate, on top of the VM's own hourly cost (`Standard_B1s` is a reasonable default for a small lobby).
`mp-host destroy` after each test run is the cheapest way to avoid paying for an idle VM, its public
IP (Azure bills a reserved static IP even while the VM is stopped) and NIC between events.
