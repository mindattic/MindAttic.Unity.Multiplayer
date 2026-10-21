// The player: one per connection, spawned by the NetworkManager as its Player Prefab. The owner sends its input to the
// server with one [ToServer] call; the server moves the player and every peer reads the [Synced] position. The name and
// colour are not copied onto this object: they live in the session's roster (NetRoster), which every peer has.
using Unity.Multiplayer;      // NetEntity, NetPlayer, NetRoster
using Unity.Multiplayer.Core; // [ToServer], [Synced]
using UnityEngine;

namespace MyGame
{
    public partial class Player : NetEntity
    {
        public const float Speed = 4f;

        // client -> server: the owner's input, at most 20 times a second (more is dropped by the server).
        [ToServer(RateLimit = 20)]
        public partial void Move(Vector2 direction);

        // Only the server writes it; every peer, late joiners included, reads the same value through Position.
        [Synced] private Vector2 _position;

        private Vector2 _direction; // server only: the last input, clamped
        private Vector2 _sent;      // owner only: the last input sent
        private SpriteRenderer _body;

        /// <summary>The owner's name, from the roster ("Player" until the roster has it).</summary>
        public string DisplayName
        {
            get { return Roster(out NetPlayer player) ? player.DisplayName : "Player"; }
        }

        /// <summary>The owner's colour, from the roster (their pick, or their seat's palette colour).</summary>
        public Color32 Color
        {
            get { return Roster(out NetPlayer player) ? player.UnityColor() : new Color32(255, 255, 255, 255); }
        }

        protected override void OnSpawned()
        {
            _body = Shapes.Square(gameObject, 0.8f, 0);
            if (IsServer && Position == Vector2.zero)
            {
                Position = Arena.SpawnPoint(Owner.HasSeat ? Owner.SeatIndex : 0);
            }
        }

        // Runs on the server for every Move call. The server never trusts input as-is: a longer vector would be faster.
        private void OnMove(Vector2 direction)
        {
            _direction = Vector2.ClampMagnitude(direction, 1f);
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner && !NetBots.IsBot) // a bot's driver calls Move itself (GameBot.cs)
            {
                Vector2 input = GameInput.Direction();
                if (input != _sent)
                {
                    _sent = input;
                    Move(input);
                }
            }

            if (IsServer && _direction != Vector2.zero)
            {
                Vector2 before = Position;
                Position = Arena.Clamp(before + _direction * Speed * Time.deltaTime, 0.4f);
                Arena.Current?.Push(Position, Position - before);
            }

            transform.position = Position;
            if (_body != null)
            {
                _body.color = Color;
            }
        }

        // The name under the square. IMGUI keeps the template free of fonts and canvases; swap in your own UI.
        private void OnGUI()
        {
            Camera camera = Camera.main;
            if (!IsSpawned || camera == null)
            {
                return;
            }

            Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.down * 0.55f);
            var label = new Rect(screen.x - 80f, Screen.height - screen.y, 160f, 22f);
            GUI.Label(label, DisplayName + (IsOwner ? " (you)" : string.Empty),
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter });
        }

        private bool Roster(out NetPlayer player)
        {
            player = null;
            return Session != null && Session.Roster != null && Session.Roster.TryGet(OwnerClientId, out player);
        }
    }
}
