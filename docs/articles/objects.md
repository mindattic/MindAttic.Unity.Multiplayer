# Networked objects by key

A game usually knows its objects by its own names: a level's block `b12`, the door `door:A`, the scoreboard. On a
client, though, a spawned object arrives as just another `NetworkObject`, and matching it back to the game's name by
spawn order or by network object id breaks as soon as the order changes, an object is replaced, or a player joins late.

A **network key** fixes that. The server gives each object a key when it spawns it; every peer looks the object up by
that key in the session's registry.

```csharp
// Server, building a level: each block's body is keyed by the block's id.
foreach (Block block in level.Blocks)
{
    GameObject body = session.Objects.Spawn(bodyPrefab, block.Id, block.Position);
    // ... configure the server's copy as usual
}

// Any peer, drawing the level: find the replicated body for each block.
foreach (Block block in level.Blocks)
{
    if (session.Objects.TryGet(block.Id, out Transform body))
        Draw(block, body.position);
}
```

## Set up

1. Add a **`NetKey`** component to the root of the network prefab, beside its `NetworkObject`. Register the prefab as
   usual (a `NetPrefabList`, or `session.Prefabs.Register` on every peer before the session starts).
2. On the server, spawn instances with **`session.Objects.Spawn(prefab, key, position, rotation, parent, ownerClientId)`**.
   It refuses a bad or already-used key before anything is created, sets the key, and spawns through this session.
3. On any peer, use **`session.Objects`** (or `NetRegistry.Current` for `NetBootstrap.Instance`'s session).

The key travels inside the object's spawn, so a client knows it before any of the object's `OnSpawned` runs, and a
late joiner gets every existing object's key the same way. A prefab whose `NetKey` has a key set in the Inspector
spawns with that key (handy for a one-of-a-kind object like a scoreboard).

## `NetRegistry` (`session.Objects`)

| Member | What it does |
| --- | --- |
| `TryGet<T>(key, out T)` | The spawned object holding `key`, as its `T` component: the `NetKey`, any component on the object or its children (`Transform`, your own `NetEntity`, ...). False if no object holds the key on this peer. |
| `TryGet(key, out GameObject)` | The same, as the `GameObject`. |
| `Get<T>(key)` | The same, or a `KeyNotFoundException` naming the key (an `InvalidOperationException` if the object has no `T`). |
| `Contains(key)`, `Count`, `Keys`, `All` | What is spawned on this peer, by key. |
| `Spawned`, `Despawned` | A keyed object spawned on, or is despawning from, this peer (`Action<NetKey>`; `.Key` is its key). A late joiner gets `Spawned` for each object already in the game as it arrives. |
| `Spawn(prefab, key, position, rotation, parent, ownerClientId)` | Server: spawns an instance of `prefab` holding `key`. |
| `Despawn(key)` | Server: despawns and destroys the object holding `key`; false if none does. |
| `NetRegistry.Current` | The registry of `NetBootstrap.Instance`'s session. |

Events are raised on Unity's main thread, after every component of the object has run its `OnSpawned` (and before any
runs `OnDespawned`). A handler that throws is logged and does not stop the others. The registry empties when its
session starts and ends.

## `NetKey`

| Member | What it does |
| --- | --- |
| `Key` | The key. Set it before the object spawns (`Spawn` does); setting it while spawned throws, because a key is stable for the object's whole life. |
| `IsRegistered` | True while the registry hands this object out for its key (false for a refused duplicate). |

## Rules for keys

- A key is a non-empty string of at most `NetKeys.MaxLength` (128) characters, with no control characters and no
  leading or trailing whitespace (`NetKeys.Validate` says what is wrong with one). Keys are compared ordinally: `B1`
  and `b1` are different keys.
- **One live object per key.** `Spawn` throws an `ArgumentException` for a key a spawned object already holds,
  naming the key and that object. To reuse a key, despawn the old object first: `Despawn(key)` frees it at once, so
  a level reset can despawn its old objects and spawn the new ones with the same keys in the same frame. (Unity's
  `Object.Destroy` despawns only at the end of the frame; despawn first if you rebuild in the same frame.)
- An object spawned some other way (`NetworkObject.Spawn`, the test kit's `Spawn`) with no key or a key in use is
  reported as an error on every peer and left out of the registry; the object that had the key keeps it.

## Where it is tested

`dotnet test tests/Core.Tests` covers the engine-free rules (`NetKeysTests`, `NetKeyTableTests`: validation, one
holder per key, a removal only by the holder). `tests/run-playmode.ps1` runs `NetRegistryTests` with a Host and with a
dedicated Server, each with two clients over real Unity Transport: every client finds each object by key (and its
components see the key in their own `OnSpawned`), a late joiner finds objects spawned before it came, objects spawned
out of order and replaced still map to the right key, a despawn is heard everywhere and frees the key for an object
spawned in the same frame, and a duplicate key is refused (by `Spawn` before anything spawns, and reported on every
peer for an object spawned another way).
