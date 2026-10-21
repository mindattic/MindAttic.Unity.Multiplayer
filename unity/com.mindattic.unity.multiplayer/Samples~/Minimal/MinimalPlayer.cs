// The Minimal sample's player: one square per connected player, moved by the server.
// Everything networked about it is three declarations: one [ToServer] method, one [ToClients] method and one [Synced]
// field. The package's source generator writes the rest when this file compiles. README.md walks through it.
using MindAttic.Unity.Multiplayer;      // NetEntity, the base class of anything networked
using MindAttic.Unity.Multiplayer.Core; // the attributes: [ToServer], [ToClients], [Synced]
using UnityEngine;

namespace MindAttic.Unity.Multiplayer.Samples.Minimal
{
    // "partial" because the generator writes the other half of this class. NetEntity is the package's networked base
    // class. It must sit on a prefab that has a NetworkObject; this one is the NetworkManager's player prefab, so every
    // player who joins gets one, owned by them.
    public partial class MinimalPlayer : NetEntity
    {
        // How fast a player moves, in arena units per second.
        public const float Speed = 4f;

        // The arena is a square from -ArenaHalfSize to +ArenaHalfSize on both axes.
        public const float ArenaHalfSize = 4f;

        // client -> server. The owner calls SetDirection(...) like an ordinary method; the generator sends it to the
        // server, which runs OnSetDirection. Only the owner may call it (RequireOwnership is on by default), and at
        // most 20 calls a second per player get through (RateLimit).
        [ToServer(RateLimit = 20)]
        public partial void SetDirection(Vector2 direction);

        // server -> every client. The server calls Bumped(); every client (the host's own player too) runs OnBumped.
        [ToClients]
        public partial void Bumped();

        // Synced state. The generator turns this field into a Position property that only the server may set; every
        // peer reads the same value. Use Position, never _position.
        [Synced] private Vector2 _position;

        // Server only: the direction the owner last asked for.
        private Vector2 _direction;

        // Server only: whether the square was pressed against a wall last frame, so Bumped fires once per bump.
        private bool _againstWall;

        // Owner only: the last direction sent, so input is sent only when it changes.
        private Vector2 _sent;

        // Every peer: when the white flash from the last bump ends.
        private float _flashUntil;

        // Runs on every peer once the player has spawned. The server places a new player in a free-ish spot.
        protected override void OnSpawned()
        {
            if (IsServer && Position == Vector2.zero)
            {
                int seat = Owner.HasSeat ? Owner.SeatIndex : 0;
                Position = new Vector2(-3f + 2f * (seat % 4), 1.5f - 3f * (seat / 4 % 2));
            }
        }

        // Runs on the server when the owner called SetDirection. The server checks what a client sends: a longer
        // vector would mean a faster player, so it is clamped to length 1.
        private void OnSetDirection(Vector2 direction)
        {
            _direction = Vector2.ClampMagnitude(direction, 1f);
        }

        // Runs on every client (and the host) when the server called Bumped.
        private void OnBumped()
        {
            _flashUntil = Time.time + 0.2f;
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            // The owner reads its keyboard and tells the server when the direction changes.
            if (IsOwner)
            {
                Vector2 direction = MinimalInput.Direction();
                if (direction != _sent)
                {
                    _sent = direction;
                    SetDirection(direction);
                }
            }

            // The server moves the square, keeps it inside the arena and tells everyone when it hits a wall.
            if (IsServer)
            {
                Vector2 wanted = Position + _direction * Speed * Time.deltaTime;
                Vector2 kept = new Vector2(
                    Mathf.Clamp(wanted.x, -ArenaHalfSize, ArenaHalfSize),
                    Mathf.Clamp(wanted.y, -ArenaHalfSize, ArenaHalfSize));
                if (kept != Position)
                {
                    Position = kept; // replicated to every client
                }

                bool againstWall = kept != wanted;
                if (againstWall && !_againstWall)
                {
                    Bumped(); // sent to every client
                }

                _againstWall = againstWall;
            }

            // Every peer shows the server's position.
            transform.position = Position;
        }

        // Draws the square with IMGUI, so the sample needs no sprites, materials or render pipeline.
        private void OnGUI()
        {
            if (!IsSpawned)
            {
                return;
            }

            Rect arena = ArenaOnScreen();
            float unit = arena.width / (2f * ArenaHalfSize);
            Vector2 centre = new Vector2(
                arena.center.x + Position.x * unit,
                arena.center.y - Position.y * unit);
            Rect square = new Rect(centre.x - unit * 0.4f, centre.y - unit * 0.4f, unit * 0.8f, unit * 0.8f);

            Color previous = GUI.color;
            GUI.color = Time.time < _flashUntil ? Color.white : ColorFor(OwnerClientId);
            GUI.DrawTexture(square, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(square.x, square.yMax, 120f, 20f), IsOwner ? "you" : "player " + OwnerClientId);
        }

        // Where the arena is on screen: a centred square filling most of the window. MinimalMenu draws it too.
        public static Rect ArenaOnScreen()
        {
            float size = Mathf.Min(Screen.width, Screen.height) * 0.8f;
            return new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);
        }

        // Each player's colour, worked out the same way on every peer from the owner's client id.
        private static Color ColorFor(ulong clientId)
        {
            return Color.HSVToRGB(clientId * 0.137f % 1f, 0.65f, 1f);
        }
    }
}
