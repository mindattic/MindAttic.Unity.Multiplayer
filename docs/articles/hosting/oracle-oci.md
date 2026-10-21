# Hosting on Oracle Cloud Infrastructure (Oci)

A UM-T31 profile: Oracle's **Always Free** tier gives every tenancy either an Ampere A1 (ARM64)
compute allowance (up to 4 OCPUs/24 GB total, split across up to 4 instances) or two AMD
`VM.Standard.E2.1.Micro` instances, forever, at no cost. `mp-host` launches an instance running the
same Docker image + rendezvous side-car compose stack as GenericVps, booted via cloud-init passed as
base64 `user_data` metadata, and widens the subnet's existing security list to open the ports it
needs instead of creating a new per-instance firewall resource.

## Ampere A1 (ARM64) vs the AMD micro shape

The ticket for this profile (UM-T31) asked for a Linux ARM64 dedicated-server build to be verified
against Ampere A1, falling back to the AMD shape if Unity's ARM64 server target isn't usable.
**It currently isn't:** Unity's Linux Dedicated Server build goes through
`UnityEditor.BuildTarget.StandaloneLinux64` (`DedicatedServerBuild.IsLinuxServerSupported()` checks
exactly that constant) -- as of this writing, Unity 6's public `BuildTarget` enum has no Linux ARM64
Standalone/Server entry at all, only `StandaloneLinux64` (x86_64). There is nothing in the Unity
Editor to point at an ARM64 image even if Oracle's Ampere A1 shape is free and generous. Re-check this
against a newer Unity release before assuming it still holds: search `UnityEditor.BuildTarget` for a
Linux ARM64/Arm64 member, and if one exists, a game can cross-compile (or Unity can build) a matching
IL2CPP server and this profile's `shape`/`ocpus`/`memoryInGBs` options already accept
`VM.Standard.A1.Flex` with no other code change.

Until then, `OciProvisioner.DefaultShape` is **`VM.Standard.E2.1.Micro`** (1/8 OCPU, 1 GB RAM, AMD,
x86_64) -- the same architecture as every other mp-host profile's Linux build, and free forever under
the Always Free tier (up to two of them per tenancy). It is small; keep the player count per instance
modest, same as any other 1-vCPU host in this hosting table.

## What `mp-host deploy` does here

1. Builds (or reads an already-built) **GenericVps-profile** server package and image, pushes it to
   GHCR.
2. Renders the compose file + bootstrap script into a cloud-config document
   (`CloudInitRenderer.RenderCloudConfig`, the same shape AWS/Azure use) and calls `POST
   /20160918/instances` with it as base64 `metadata.user_data`, your chosen availability domain,
   shape and boot image.
3. Reads the subnet's existing security list (`GET /20160918/securityLists/{id}`) and writes back a
   superset including SSH (22/tcp), the rendezvous port (8080/tcp) and your declared game port --
   **never** removing a rule it did not add, since a security list is shared by the whole subnet, not
   owned by one instance the way a GCE firewall rule or Azure NSG is.
4. Resolves the instance's public IP (OCI, unlike GCE/Azure, does not return it on the instance
   resource itself -- this profile lists the instance's VNIC attachments, then reads the primary
   VNIC), then over SSH for `GET http://127.0.0.1:<healthPort>/healthz` once it's up, exactly like
   GenericVps.
5. Watches `docker compose logs game` over SSH for the join-code line and prints it.

## Before you deploy

You need an OCI tenancy, an **API signing key** (OCI's own RSA-based request signing -- not a bearer
token), and an already-existing VCN with a subnet (mp-host does not create a VCN):

1. In the OCI Console: **Profile ▸ My Profile ▸ API Keys ▸ Add API Key**, generate a key pair, and
   note the **fingerprint**, your **user OCID** and your **tenancy OCID** (both shown on the same
   page). Save the private key PEM file somewhere mp-host can read it.
2. Note your **compartment OCID** (the root compartment's OCID works for a quick start), an
   **availability domain** name (e.g. `Uocm:US-ASHBURN-AD-1`), your subnet's OCID, its **security
   list** OCID (Networking ▸ Virtual Cloud Networks ▸ your VCN ▸ Subnet Details ▸ Security Lists),
   and a platform image OCID for Canonical Ubuntu in your region/shape (Compute ▸ Images, or
   `oci compute image list` if you have the OCI CLI).
3. Generate (or reuse) a local SSH key pair for mp-host's own SSH access after boot.

```
mp-host credentials set-oci --tenancy ocid1.tenancy.oc1..aaaa... --user ocid1.user.oc1..aaaa... \
  --fingerprint aa:bb:cc:dd:ee:ff:... --key C:\Users\me\.oci\oci_api_key.pem \
  --region us-ashburn-1 --compartment ocid1.compartment.oc1..aaaa...
mp-host credentials set-ghcr --username <github-username> --token <PAT with write:packages>
mp-host credentials set-cloud-options --profile Oci --name home-oci \
  --opt availabilityDomain=Uocm:US-ASHBURN-AD-1 --opt subnetId=ocid1.subnet.oc1..aaaa... \
  --opt securityListId=ocid1.securitylist.oc1..aaaa... --opt imageId=ocid1.image.oc1..aaaa... \
  --opt sshUser=ubuntu --opt sshKeyPath=C:\Users\me\.ssh\id_ed25519 \
  --opt sshPublicKeyPath=C:\Users\me\.ssh\id_ed25519.pub
```

The private key PEM must be unencrypted (no passphrase) -- `RSA.ImportFromPem` only reads an
unencrypted PKCS#1/PKCS#8 PEM. `shape` (default `VM.Standard.E2.1.Micro`), and `ocpus`/`memoryInGBs`
(Flex shapes only, e.g. `VM.Standard.A1.Flex`, default 1/6) are optional overrides -- see
`OciProvisioner`'s class doc for the full list.

Then deploy:

```
mp-host deploy --profile Oci --name home-oci --package ./build/GenericVps \
  --image myorg/mygame --tag v1 --region us-ashburn-1 \
  --rendezvous-url https://rendezvous.example.com --rendezvous-secret <a long random string> \
  --public-address home-oci
```

(`--region` is accepted for consistency with the other profiles but is not read by this one -- OCI's
region is fixed by the credential's own `--region us-ashburn-1`, since the signed request's host
already encodes it.) Add `--dry-run` first to see every Core Services API call mp-host would make,
without creating anything.

## Open ports

| Port | Protocol | Purpose |
|---|---|---|
| your game port | UDP (or TCP for WebSocket) | Players connect here. |
| 8080 | TCP | The rendezvous service. |
| 22 | TCP | SSH -- the only way mp-host reaches the box after boot. |
| health port | TCP, bound to 127.0.0.1 | Not reachable from the internet by design. |

## Updating and tearing down

`mp-host update` restarts the compose stack in place over SSH, same as the other cloud VM profiles.
`mp-host destroy ... --yes` stops the compose stack and terminates the instance, but **leaves the
security list's rules in place** -- widening them was explicitly additive (see above), and another
instance on the same subnet may depend on them; remove a rule yourself once nothing uses it.

## Cost notes

Both the AMD micro shape and an Ampere A1 allocation within the free limits are free forever under
Oracle's Always Free tier, including a documented amount of outbound data transfer per month (verify
the current free-tier transfer allowance and overage pricing on Oracle's own pricing page before
relying on this for a public launch with heavy traffic) -- `mp-host destroy` after each test run still
costs nothing extra, but is good hygiene so a forgotten instance doesn't count against your two-free-
AMD-instance or Ampere A1 OCPU/memory limit when you want to spin up another one.
