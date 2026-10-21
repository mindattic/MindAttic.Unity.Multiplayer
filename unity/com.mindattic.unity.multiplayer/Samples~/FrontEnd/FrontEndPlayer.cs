// The Front End sample's player: a square per player in the player's own colour, with their name under it, moved by
// the server. The name and colour are the ones the player picked in the front end's Settings screen: they travel to the
// server in the connection request, the server puts them on the player's seat, and two [Synced] fields share them.
using Unity.Collections;      // FixedString64Bytes, a string the network can carry
using Unity.Multiplayer;      // NetEntity
using Unity.Multiplayer.Core; // [ToServer], [Synced], Seat
using UnityEngine;

namespace Unity.Multiplayer.Samples.FrontEnd
{
    public partial class FrontEndPlayer : NetEntity
    {
        public const float Speed = 4f;
        public const float ArenaHalfSize = 4f;

        // client -> server: the owner's arrow keys, at most 20 times a second.
        [ToServer(RateLimit = 20)]
        public partial void SetDirection(Vector2 direction);

        // Synced state: only the server writes these; every peer reads the same values.
        [Synced] private Vector2 _position;
        [Synced] private Color32 _color;
        [Synced] private FixedString64Bytes _name;

        private Vector2 _direction; // server only
        private Vector2 _sent;      // owner only

        protected override void OnSpawned()
        {
            if (!IsServer)
            {
                return;
            }

            // The seat holds what the player sent when they joined (and keeps it if they reconnect).
            Seat seat;
            if (Session.TryGetSeat(OwnerClientId, out seat))
            {
                PlayerColor c = seat.Color;
                Color = new Color32(c.R, c.G, c.B, 255);
                var name = new FixedString64Bytes();
                name.CopyFromTruncated(seat.DisplayName);
                Name = name;
            }

            if (Position == Vector2.zero)
            {
                int index = Owner.HasSeat ? Owner.SeatIndex : 0;
                Position = new Vector2(-3f + 2f * (index % 4), 1.5f - 3f * (index / 4 % 2));
            }
        }

        // The server never trusts the input as-is: a longer vector would mean a faster player.
        private void OnSetDirection(Vector2 direction)
        {
            _direction = Vector2.ClampMagnitude(direction, 1f);
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner)
            {
                Vector2 direction = FrontEndInput.Direction();
                if (direction != _sent)
                {
                    _sent = direction;
                    SetDirection(direction);
                }
            }

            if (IsServer)
            {
                Vector2 wanted = Position + _direction * Speed * Time.deltaTime;
                Vector2 kept = new Vector2(
                    Mathf.Clamp(wanted.x, -ArenaHalfSize, ArenaHalfSize),
                    Mathf.Clamp(wanted.y, -ArenaHalfSize, ArenaHalfSize));
                if (kept != Position)
                {
                    Position = kept;
                }
            }

            transform.position = Position;
        }

        // IMGUI, so the sample needs no sprites or render pipeline. The front end's menus draw on top of it.
        private void OnGUI()
        {
            if (!IsSpawned)
            {
                return;
            }

            float size = Mathf.Min(Screen.width, Screen.height) * 0.8f;
            Rect arena = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);
            float unit = arena.width / (2f * ArenaHalfSize);
            Vector2 centre = new Vector2(arena.center.x + Position.x * unit, arena.center.y - Position.y * unit);
            Rect square = new Rect(centre.x - unit * 0.4f, centre.y - unit * 0.4f, unit * 0.8f, unit * 0.8f);

            Color previous = GUI.color;
            GUI.color = Color;
            GUI.DrawTexture(square, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(square.x - unit, square.yMax, square.width + 2f * unit, 22f), Name.ToString() + (IsOwner ? " (you)" : string.Empty),
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter });
        }
    }
}
