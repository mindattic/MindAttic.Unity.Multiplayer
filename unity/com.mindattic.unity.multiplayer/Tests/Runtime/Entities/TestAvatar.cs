using System.Collections.Generic;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// What a game writes: attribute-marked partial methods, synced fields and their handlers, and nothing else. No NGO
    /// type appears here (BIBLE UM-LAW-2). The other half of this class is written by the package's source generator
    /// (<c>Runtime/Generators</c>) at compile time; <c>tests/Generators.Tests/Snapshots/TestAvatar.Net.g.cs</c> shows it.
    /// </summary>
    public partial class TestAvatar : NetEntity
    {
        public const int PingRateLimit = 5;

        // --- client -> server -----------------------------------------------------------------------------------

        [ToServer(Delivery = Delivery.Unreliable)]
        public partial void Move(Vector2 worldPos);

        [ToServer(RateLimit = PingRateLimit, RequireOwnership = false)]
        public partial void Ping(int n);

        [ToServer(RequireOwnership = false)]
        public partial void Vote(int choice);

        // --- server -> clients ----------------------------------------------------------------------------------

        [ToClients]
        public partial void Whistle(Color32 color);

        [ToOwner]
        public partial void Hint(int code);

        [ToClient]
        public partial void Tell(ulong clientId, int code);

        // --- synced state ---------------------------------------------------------------------------------------

#pragma warning disable CS0169, CS0414 // declarations the generator reads (its suppressor also hides these); the values live in the generated network variables
        [Synced(OnChanged = nameof(MassChanged))] private float _mass;
        [Synced] private int _score;
#pragma warning restore CS0169, CS0414

        // --- what each handler saw, for the tests -----------------------------------------------------------------

        public readonly List<KeyValuePair<Vector2, NetSender>> Moves = new List<KeyValuePair<Vector2, NetSender>>();
        public readonly List<KeyValuePair<int, NetSender>> Pings = new List<KeyValuePair<int, NetSender>>();
        public readonly List<KeyValuePair<int, NetSender>> Votes = new List<KeyValuePair<int, NetSender>>();
        public readonly List<Color32> Whistles = new List<Color32>();
        public readonly List<int> Hints = new List<int>();
        public readonly List<int> Tells = new List<int>();
        public readonly List<Vector2> MassChanges = new List<Vector2>();
        public int Spawns;
        public int Despawns;

        protected override void OnSpawned()
        {
            Spawns++;
        }

        protected override void OnDespawned()
        {
            Despawns++;
        }

        private void OnMove(Vector2 worldPos, NetSender sender)
        {
            Moves.Add(new KeyValuePair<Vector2, NetSender>(worldPos, sender));
        }

        private void OnPing(int n, NetSender sender)
        {
            Pings.Add(new KeyValuePair<int, NetSender>(n, sender));
        }

        private void OnVote(int choice, NetSender sender)
        {
            Votes.Add(new KeyValuePair<int, NetSender>(choice, sender));
            Tell(sender, 100 + choice); // answer the caller: a NetSender converts to its client id
        }

        private void OnWhistle(Color32 color)
        {
            Whistles.Add(color);
        }

        private void OnHint(int code)
        {
            Hints.Add(code);
        }

        private void OnTell(int code)
        {
            Tells.Add(code);
        }

        private void MassChanged(float previous, float current)
        {
            MassChanges.Add(new Vector2(previous, current));
        }
    }
}
