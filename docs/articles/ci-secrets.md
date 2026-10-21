# CI secrets

The Unity jobs in CI and the nightly smoke test run Unity through GameCI, which needs a Unity license on the GitHub
runner. Those secrets live in GitHub, not in any file in the repo, and each repo that runs Unity workflows needs its
own copy. `mindattic` is a personal GitHub account, so there are no organization-wide secrets to share.

## The one command

From a PC where Unity Hub is signed in and has an activated license:

```powershell
powershell -File tools\set-ci-secrets.ps1
```

It asks for your Unity account email and password once, then sets every secret below on `mindattic/Unity.Multiplayer`
and `mindattic/Unity.Cursory`, and lists what each repo now has. For a new game, name its repo:

```powershell
powershell -File tools\set-ci-secrets.ps1 -Repos mindattic/MyNewGame
```

`-Repos` takes several (`-Repos mindattic/GameA,mindattic/GameB`), and `-LicenseFile` points at a license file other
than the default `C:\ProgramData\Unity\Unity_lic.ulf`. Values go to `gh` on standard input, never on the command line.
It needs the [GitHub CLI](https://cli.github.com/) logged in (`gh auth login`) with the `repo` scope.

Run it from inside Claude Code as `! powershell -File D:/Projects/MindAttic/Unity.Multiplayer/tools/set-ci-secrets.ps1`.
The `!` shell is bash, so keep the forward slashes.

## What it sets

| Secret | Where | Value | Used by |
|---|---|---|---|
| `UNITY_LICENSE` | every repo given | the contents of `C:\ProgramData\Unity\Unity_lic.ulf` | `ci.yml` Unity jobs, `nightly-smoke.yml`, `game-smoke.yml`, a game's `nightly.yml` |
| `UNITY_EMAIL` | every repo given | your Unity account email | `game-smoke.yml` (GameCI needs it with a Personal license) |
| `UNITY_PASSWORD` | every repo given | your Unity account password | `game-smoke.yml` |
| `RENDEZVOUS_SMOKE_SECRET` | Unity.Multiplayer only, if missing | 48 random hex characters | `nightly-smoke.yml` |

Not set, because they're not needed today:

- `UNITY_SERIAL`: only for a Unity Pro license.
- `MINDATTIC_REPOS_TOKEN`: a token with read access to the sibling repos. `game-smoke.yml` and Cursory's nightly
  need it only if Unity.Multiplayer, Unity.Shared, MindAttic.Components or MindAttic.Capture is private; all are
  public.

## Doing it by hand

Each line below is one secret on one repo; the last two prompt for the value.

```bash
gh secret set UNITY_LICENSE -R mindattic/MyNewGame < C:/ProgramData/Unity/Unity_lic.ulf
gh secret set UNITY_EMAIL -R mindattic/MyNewGame
gh secret set UNITY_PASSWORD -R mindattic/MyNewGame
gh secret list -R mindattic/MyNewGame
```

Or on GitHub: the repo's **Settings → Secrets and variables → Actions → New repository secret**, and paste the whole
`.ulf` file as the value of `UNITY_LICENSE`.

## When it goes wrong

- **`No Unity license at C:\ProgramData\Unity\Unity_lic.ulf`**: open Unity Hub, sign in and activate a license, then
  run the script again.
- **`failed to fetch public key: HTTP 404` with `--org`**: `mindattic` is a user account, not an organization; set
  the secrets per repo (the script already does).
- **`No such file or directory` for `C:ProgramDataUnity...`**: the path went through bash, which dropped the
  backslashes. Use forward slashes.
- **The Unity job still fails on activation**: the license on this PC was reissued after you set the secret (Unity Hub
  refreshes it); run the script again.

See [Testing](testing.md) for what each CI job runs, and [Bots](bots.md#the-nightly-workflow) for the nightly smoke
test.
