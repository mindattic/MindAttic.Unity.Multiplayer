# Troubleshooting

Errors and symptoms this package produces most, grouped by where they show up.

## Compile-time: generator and analyzer diagnostics (MAMP0xx)

These fire in the editor's Console and on the command line the moment the offending script compiles — nothing
runs before they're fixed. Full table and examples: [Attributes reference](attributes.md#generator-diagnostics).

| Symptom | Likely cause | Fix |
|---|---|---|
| MAMP001 "is not partial" | A `[ToServer]`/etc. method, or its containing class, is missing `partial`. | Add `partial` to both the class and the method. |
| MAMP002 "has no handler" | No `On<Name>` method exists for an attributed RPC. | Add `private void On<Name>(<same parameters>[, NetSender sender])`. |
| MAMP003 unsendable parameter | A parameter type NGO can't serialize (an interface, a type with no serializer). | Use a primitive, a Unity math type, a `struct` implementing `INetworkSerializable`, or send an id/index instead. |
| MAMP004 unreplicable `[Synced]` type | Same restriction as MAMP003, for a synced field's type. | Same fix. |
| MAMP005 generic `NetEntity` | A `NetEntity` subclass (or one of its base classes) is generic. | Make it non-generic; NGO can't give a generic type stable RPC ids. |
| MAMP006 direct write to a `[Synced]` field | Code assigns `_mass = ...` instead of `Mass = ...`. | Use the generated property everywhere; never the backing field. |
| MAMP007 direct read of a `[Synced]` field (warning) | Code reads `_mass` instead of `Mass`. | Same fix; this one's a warning so it won't block the build, but fix it anyway — the field isn't kept current once NGO owns the property. |
| MAMP008 attribute outside a `NetEntity` | `[ToServer]`/`[Synced]`/etc. on a class that doesn't derive from `NetEntity`. | Derive from `NetEntity`, or remove the attribute. |
| MAMP009 bad RPC shape | The method returns non-`void`, is `static`/generic, has `ref`/`out`/`params`, or carries more than one direction attribute. | Match the shape in [Attributes reference](attributes.md): `void`, instance, no `ref`/`out`/`params`, exactly one direction. |
| MAMP010 `[ToClient]` missing target | The method's first parameter isn't a `ulong`. | Add a `ulong target` (or pass a `NetSender`, which converts implicitly) as the first parameter. |
| MAMP011 bad `OnChanged` | The named handler doesn't exist, or isn't `void Handler(T previous, T current)`. | Match that exact signature, with `T` the field's type. |
| MAMP012 bad `[Synced]` field | The field is `static`/`readonly`/`const`, or its generated property name collides with an existing member. | Remove the modifier, or rename the field/the colliding member. |
| MAMP013 non-constant initializer | `[Synced] float _mass = SomeMethod();` — the initial value must be known at compile time. | Set a literal initial value, then assign the real starting value in `OnSpawned` on the server. |

## Compile-time: installing the package

| Symptom | Likely cause | Fix |
|---|---|---|
| `NetSessionTest.cs`: CS0234 "The type or namespace name 'TestHelpers' does not exist in the namespace 'Unity.Netcode'", CS0246 `NetcodeIntegrationTest`; batch mode says "Scripts have compiler errors" | The package's test kit (`Unity.Multiplayer.Testing`) builds on NGO's test helpers, which only compile when NGO is a testable. | Add `"testables": [ "com.unity.netcode.gameobjects" ]` to `Packages/manifest.json` ([Getting started](getting-started.md#2-make-a-unity-project-beside-them-2-minutes)). |
| **New Game…** or **Add Front End** says MindAttic Components is missing | The front end and the vote overlay are built from it, and a `package.json` cannot depend on a `file:` package. | Click **Add missing lines to Packages/manifest.json** in **New Game…**, or add the line it shows. See [The kit](kit.md). |

## Editor: Validate Prefabs

**MindAttic ▸ Multiplayer ▸ Validate Prefabs** checks every network prefab under `Assets` (anything with a
`NetworkObject`/`NetworkBehaviour`, or named by a `NetPrefabList`):

| Issue | Means | Fix |
|---|---|---|
| `MissingNetworkObject` (error) | No `NetworkObject` on the prefab's root (it names the child that has one, if any). | Move the `NetworkObject` to the prefab's root. |
| `NotRegistered` (error) | No `NetPrefabList` (and no NGO `NetworkPrefabsList`) registers the prefab. | Add it to the list assigned on your `NetBootstrap` (**Set Up Scene** creates one automatically for prefabs nothing else lists). |
| `GenericNetEntity` (error) | A `NetEntity` on the prefab is itself generic, or derives from a generic class. | Same fix as MAMP005 — the runtime can't catch this on its own, since an attached component is never an open generic type, so the validator catches it ahead of time. |
| `EmptyListEntry` (warning) | A `NetPrefabList` has a null slot. | Remove the empty entry, or fill it. |

**Build Dedicated Server (Linux)** and **Build Server Package** both run this validator first and refuse to
build on any error.

## Runtime: connection refused

A client's `NetSession.LastDisconnect` (and the server's `ConnectionRejected` event) names which check failed:

| Reason | Means | Fix |
|---|---|---|
| `BadPayload` | The approval payload didn't decode (a very old client, or a non-MindAttic connection attempt). | Make sure both sides run the same package version. |
| `VersionMismatch` | `NetConfig.GameVersion` differs between client and server. | Match the version, or intentionally reject older clients (this is often correct — surface it to the player as "update your game"). |
| `ProtocolMismatch` | The connecting assembly's generated RPC/synced shapes hash differently than the server's — one side was built from different networked code. | Rebuild both sides from the same source; this is the generator and protocol hash doing their job (see [Attributes reference](attributes.md#how-a-call-is-handled-and-what-the-generator-writes)), not a bug. |
| `Banned` | The connecting player token is on the server's `IBanList`. | Expected; only a problem if you didn't mean to ban them. |
| `Full` | `NetConfig.MaxPlayers` reached (a held seat still counts). | Raise `MaxPlayers`, or wait for a held seat's grace window to expire — see [Reconnect and seats](reconnect-seats.md). |

## Runtime: join codes

| Symptom | Likely cause | Fix |
|---|---|---|
| "Unity Relay join codes need the com.unity.services.multiplayer package" | `com.unity.services.multiplayer` isn't installed, so `MINDATTIC_UGS` isn't defined and `RelayJoinCodeService.IsAvailable` is false. | Install the package from Package Manager, or set `JoinCodeProvider.Rendezvous`/`None` if you don't need Relay. |
| `HostWithCode` / `JoinByCode` fails with a Cloud-project error | The project isn't linked to Unity Cloud, or Relay isn't enabled for it. | **Edit ▸ Project Settings ▸ Services**, link the project and enable Relay. |
| A dedicated server never publishes a code | `NetConfig.RendezvousUrl` is empty, or `JoinCodeDefaults.ShouldSelfRegister` is false for some other reason (wrong topology, `JoinCodes` set to `None`). | Set `RendezvousUrl` (or `-rendezvous`/`MINDATTIC_RENDEZVOUS_URL`) and confirm `Topology.Server`. |
| Registration fails with 401/403 from the rendezvous service | Missing or wrong `MINDATTIC_RENDEZVOUS_SECRET` / `-rendezvoussecret`. | Match the secret the service was started with (`Rendezvous__RegistrationSecret`). See [Join codes](join-codes.md#the-rendezvous-service). |
| A code resolves to the wrong address, or none, behind NAT/a proxy | The server didn't advertise its real public address. | Set `NetConfig.PublicAddress`/`PublicPort` (or `-publicaddress`/`-publicport`), or see RunPod's automatic `RUNPOD_TCP_PORT_*` mapping in [Hosting](hosting/runpod.md). |

## Runtime: network conditions have no effect

`NetConfig.Conditions`/`-netsim` only simulates anything in the editor and in development builds — a release
build ignores it with a warning, by design, so a shipped build is never accidentally slowed down. See
[Topologies § Testing a bad connection](topologies.md#testing-a-bad-connection).

## Hosting and deployment

See [Hosting](hosting.md) for the provider table, and each provider's own page under `hosting/` for its
specific prerequisites and failure modes (credentials not set, firewall rule not opened, RunPod's UDP
limitation, and so on) — those are covered there rather than duplicated here, per BIBLE UM-LAW-4 (one home per
fact).

## Still stuck?

Check `docs/BIBLE.md` for what's actually shipped and verified so far (its "Verified state" section names the
test suite that proves each feature, and "Active frontier" says what isn't built yet) and `docs/USER_STORIES.md`
for the ticket that covers it.
