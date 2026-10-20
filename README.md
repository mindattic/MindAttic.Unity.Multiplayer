# MindAttic.Unity.Multiplayer
Reusable Unity 6 multiplayer for MindAttic games: attribute RPCs, host or dedicated server, join by IP or code.

> **Status: skeleton.** Nothing runs yet. The design and build order are in
> [docs/rfc/0001-architecture.md](docs/rfc/0001-architecture.md); the rules are in
> [docs/BIBLE.md](docs/BIBLE.md). This README is rewritten to README-STYLE, with a link to the
> docs site, in UM-T19.

## Use it from a Unity project
Add the package to the game's `Packages/manifest.json` as a local package:

```json
"com.mindattic.unity.multiplayer": "file:../../MindAttic.Unity.Multiplayer/unity/com.mindattic.unity.multiplayer"
```

It pulls in Netcode for GameObjects 2.x and Unity Transport 2.x. Install
`com.unity.services.multiplayer` as well to enable Unity Relay join codes.

## Layout
| Path | What |
|---|---|
| `unity/com.mindattic.unity.multiplayer/` | The Unity package (`package.json`, Runtime, Editor, Tests) |
| `tests/Core.Tests/` | NUnit tests for the engine-free Core, compiled straight from `Runtime/Core` |
| `tests/Core.Build/` | Compiles `Runtime/Core` as netstandard2.1 / C# 9, the way Unity does |
| `docs/` | BIBLE, RFCs, user stories |

## Test
```
dotnet test tests/Core.Tests
```

## License
MIT. See [LICENSE](LICENSE).
