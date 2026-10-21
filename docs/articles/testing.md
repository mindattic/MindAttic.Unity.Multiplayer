# Testing

The package is tested at two levels: plain `dotnet test` for everything engine-free, and Unity PlayMode/EditMode
suites for everything that needs the engine or real networking. Both run in CI
(`.github/workflows/ci.yml`); a nightly job additionally runs a real two-client smoke test end to end.

## dotnet test

```bash
dotnet test tests/Core.Tests          # Unity.Multiplayer.Core: config, join codes, seats, approval, rpc gate, rendezvous client...
dotnet test tests/Rendezvous.Tests    # server/Rendezvous, hosted in-process with WebApplicationFactory
dotnet test tests/Generators.Tests    # the source generator and its analyzer diagnostics
dotnet test tests/Hosting.Cli.Tests   # mp-host: every provisioner/allocator call mocked
```

No Unity install is needed for any of these. `Unity.Multiplayer.Core` is engine-free by design (`UM-LAW-2`,
`noEngineReferences`): `tests/Core.Tests` compiles the package's own `Runtime/Core/**/*.cs` straight in
(`<Compile Include="..\..\unity\...\Runtime\Core\**\*.cs">`), so the code under test is exactly the code Unity
imports, not a copy. `tests/Core.Build` compiles the same sources as netstandard2.1 / C# 9 — the way Unity
actually compiles them — so a language feature Core.Tests' net10 TFM would silently allow still fails the way
it would in Unity.

### Generators.Tests

Snapshot-tests the generator's output: the package's own `TestAvatar` and one test per member shape (every RPC
direction, every `[Synced]` type), each compiled clean as C# 9. One test per diagnostic in
[Attributes reference](attributes.md#generator-diagnostics) asserts it fires for the broken input it's meant to
catch. After an intentional change to the generator's output shape:

```bash
UPDATE_SNAPSHOTS=1 dotnet test tests/Generators.Tests
```

rewrites `Snapshots/` — review the diff before committing it, since it's the thing the test is meant to pin.

### Hosting.Cli.Tests

Every `mp-host` provider call (Hetzner, Vultr, DigitalOcean, AWS, GCE, Azure, Fly, RunPod, Edgegap, Gameye,
GameLift, ...) is mocked with NUnit, so this suite runs with no cloud credentials and touches no real API. Each
provider also has opt-in `[Explicit]` smoke tests that exercise the real API and always tear down what they
create — run those only with real credentials loaded (`mp-host credentials set-...`), never in CI. See
[Hosting](hosting.md#secrets) for where those credentials live.

## Unity suites

```powershell
powershell -File tests/run-playmode.ps1     # PlayMode: NetcodeIntegrationTest over real Unity Transport, loopback
powershell -File tests/run-editmode.ps1     # EditMode: editor tooling, one real Linux server build (-SkipBuild to leave it out)
powershell -File tests/run-sample.ps1       # imports the Minimal sample through Package Manager, runs its PlayMode tests
powershell -File tests/run-smoke.ps1        # builds the nightly smoke test's Linux player
```

These need Unity 6000.4.3f1 installed locally (`tests/PlayModeHost` is the bare project both suites run
against). PlayMode suites cover what only means something with real networking: hosting and joining over UDP
and WebSocket, reconnect taking back the same seat, a killed client's restart taking over a stale connection,
scene flow and late joiners, every RPC direction and rate limiting, and the whole flow again under
[simulated latency and loss](topologies.md#testing-a-bad-connection) (`LossyNetworkTests`). EditMode covers the
setup wizard, the prefab validator and the dedicated-server build — including a real Mono Linux build inside
the suite, skipped only in CI (GameCI's editor image has no Linux Dedicated Server build-support module) and
still run by `run-editmode.ps1` locally, per HOUSE-LAW-8 (done is verified, not asserted).

`run-sample.ps1` builds a throwaway host project (`tests/SampleHost`, git-ignored), imports the Minimal sample
through `UnityEditor.PackageManager.UI.Sample` the same way a game developer would from the Package Manager
window, and runs `tests/MinimalSample/Tests` against the imported copy — so the sample that ships is the sample
that's tested, not a copy kept in sync by hand. `-Author` regenerates its scene, prefab and prefab list.

## The nightly smoke test

`.github/workflows/nightly-smoke.yml` builds one Linux player (`tests/MinimalSample/Editor/SmokeHostBuild.cs`)
that plays every role — rendezvous service, dedicated server, or bot client — picked at run time by the
`MINDATTIC_ROLE` environment variable (`tests/MinimalSample/Runtime/SmokeDriver.cs`), and runs all three under
Docker Compose (`smoke/docker-compose.yml`). The server registers with rendezvous and publishes a join code to a
shared volume; two bot clients read it, join through the rendezvous service, and drive their squares into the
arena wall. `smoke/wait-for-smoke.sh` watches for both bots' `OK:` log lines. This is the one test that proves
the join-code flow and an RPC round trip end to end, against the actual built player, not a PlayMode stand-in.
Run it locally with `powershell -File tests/run-smoke.ps1`.

## CI

`.github/workflows/ci.yml` runs the four `dotnet test` suites above as a matrix job, plus GameCI's
`unity-test-runner` for EditMode and PlayMode (`-runTests -batchmode`, filtered to one test assembly per mode).
Both Unity jobs need a `UNITY_LICENSE` repository secret. `.github/workflows/docs.yml` (this site) and
`.github/workflows/nightly-smoke.yml` are separate workflows — see [Hosting](hosting.md) for what the smoke test
proves about a real deploy, and the repository root `README.md` for the exact commands.

## Adding your own tests

A game consuming the package tests its own `NetEntity` subclasses the same way `tests/MinimalSample` tests
`MinimalPlayer`: a `NetcodeIntegrationTest`-based PlayMode suite that starts a host and one or more clients over
loopback, calls the game's own `[ToServer]` methods, and asserts on the `[Synced]` state and `[ToClients]`
calls every peer receives. The Minimal sample's tests (`tests/MinimalSample/Tests`) are a working, small
example to copy from.
