// The game's rules on the server, in a plain class so a test (or a bot test) can serve them on its own session.
// It spawns the keyed crates, lets players push them, and offers a "restart" vote that puts them back.
using System;
using Unity.Multiplayer;      // NetSession, NetRegistry
using Unity.Multiplayer.Core; // VoteKind
using UnityEngine;

namespace MyGame
{
    public sealed class Arena
    {
        public const float HalfSize = 6f;
        public const int CrateCount = 3;
        public const string CrateKeyPrefix = "crate:";
        public const string RestartVote = "restart";

        private readonly NetSession _session;
        private readonly GameObject _cratePrefab;

        public Arena(NetSession session, GameObject cratePrefab)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _cratePrefab = cratePrefab ?? throw new ArgumentNullException(nameof(cratePrefab));
        }

        /// <summary>The arena the server is running, for Player.Update to push crates (null on a client).</summary>
        public static Arena Current { get; private set; }

        /// <summary>Serves the rules on <paramref name="session"/>: crates spawn as soon as it runs as a server.</summary>
        public static Arena Serve(NetSession session, GameObject cratePrefab)
        {
            var arena = new Arena(session, cratePrefab);
            session.Started += () =>
            {
                if (session.IsServer)
                {
                    Current = arena;
                    arena.SpawnCrates();
                }
            };
            session.Ended += _ =>
            {
                if (Current == arena)
                {
                    Current = null;
                }
            };
            if (session.IsServer && session.IsLive)
            {
                Current = arena;
                arena.SpawnCrates();
            }

            return arena;
        }

        public static string CrateKey(int index) => CrateKeyPrefix + index;

        public static int CrateIndex(string key)
        {
            int index;
            return key != null && key.StartsWith(CrateKeyPrefix, StringComparison.Ordinal)
                && int.TryParse(key.Substring(CrateKeyPrefix.Length), out index) ? index : 0;
        }

        public static Vector2 CrateStart(int index) => new Vector2(-3f + 3f * (index % CrateCount), -2f);

        public static Vector2 SpawnPoint(int seat) => new Vector2(-3f + 3f * (seat % 3), 2f + 1.5f * (seat / 3 % 2));

        public static Vector2 Clamp(Vector2 p, float halfExtent) => new Vector2(
            Mathf.Clamp(p.x, -HalfSize + halfExtent, HalfSize - halfExtent),
            Mathf.Clamp(p.y, -HalfSize + halfExtent, HalfSize - halfExtent));

        /// <summary>Spawns every crate at its start, keyed crate:0, crate:1, ... (server only).</summary>
        public void SpawnCrates()
        {
            for (int i = 0; i < CrateCount; i++)
            {
                GameObject spawned = _session.Objects.Spawn(_cratePrefab, CrateKey(i), CrateStart(i));
                spawned.GetComponent<Crate>().Position = CrateStart(i);
            }
        }

        /// <summary>Despawns every crate and spawns it again at its start, under the same key, in the same frame.</summary>
        public void ResetCrates()
        {
            for (int i = 0; i < CrateCount; i++)
            {
                _session.Objects.Despawn(CrateKey(i));
            }

            SpawnCrates();
        }

        /// <summary>A crate a player at <paramref name="player"/> overlaps moves along with the player's step.</summary>
        public void Push(Vector2 player, Vector2 step)
        {
            foreach (NetKey key in _session.Objects.All)
            {
                Crate crate = key.GetComponent<Crate>();
                if (crate == null)
                {
                    continue;
                }

                Vector2 gap = crate.Position - player;
                if (Mathf.Abs(gap.x) < 0.8f && Mathf.Abs(gap.y) < 0.8f)
                {
                    crate.Position = Clamp(crate.Position + step, 0.4f);
                }
            }
        }

        /// <summary>The vote this game offers: put every crate back. Registered on NetVotes by Game.cs.</summary>
        public VoteKind CreateRestartVote() => new VoteKind(RestartVote, "Restart the round?")
        {
            Description = _ => "Every crate goes back to where it started.",
            Passed = _ => ResetCrates(),
        };
    }
}
