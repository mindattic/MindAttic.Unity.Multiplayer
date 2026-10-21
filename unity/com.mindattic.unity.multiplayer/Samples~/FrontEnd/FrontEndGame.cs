// The game side of the Front End sample: everything a game adds to the drop-in front end. The menus themselves need no
// code at all (the NetFrontEnd component on the NetworkManager builds them); this shows the hooks.
using Unity.Multiplayer.Core; // Disconnection
using Unity.Multiplayer.UI;   // NetFrontEnd, MenuPlacement
using UnityEngine;

namespace Unity.Multiplayer.Samples.FrontEnd
{
    public sealed class FrontEndGame : MonoBehaviour
    {
        [SerializeField] private NetFrontEnd frontEnd;

        private bool _showGrid = true;

        private void Awake()
        {
            if (frontEnd == null)
            {
                frontEnd = FindAnyObjectByType<NetFrontEnd>();
            }

            // A screen of the game's own, reachable from the main menu and the in-game Esc menu.
            frontEnd.AddScreen("How to play", s =>
            {
                s.AddText("Move your square with the arrow keys or WASD.");
                s.AddText("Your name and colour come from Settings; everyone sees them.");
            }, MenuPlacement.Both);

            // A game setting on the front end's Settings screen, under the name and colour.
            frontEnd.AddSettingsItems(s => s.AddToggle("Show the arena grid", _showGrid, on => _showGrid = on));

            // What the game does when play starts and ends.
            frontEnd.GameStarted += () => Debug.Log("[FrontEnd sample] In the game as " + frontEnd.Session.Identity.DisplayName + ".");
            frontEnd.Left += why => Debug.Log("[FrontEnd sample] Back at the menu: " + why + ".");
            frontEnd.Reconnected += () => Debug.Log("[FrontEnd sample] Reconnected to the same seat.");
        }

        // The arena behind the squares (FrontEndPlayer draws the squares).
        private void OnGUI()
        {
            if (frontEnd == null || frontEnd.State != FrontEndState.InGame)
            {
                return;
            }

            GUI.depth = 1;
            float size = Mathf.Min(Screen.width, Screen.height) * 0.8f;
            var arena = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);
            GUI.Box(arena, GUIContent.none);
            if (_showGrid)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.08f);
                for (int i = 1; i < 8; i++)
                {
                    GUI.DrawTexture(new Rect(arena.x + arena.width * i / 8f, arena.y, 1f, arena.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(arena.x, arena.y + arena.height * i / 8f, arena.width, 1f), Texture2D.whiteTexture);
                }

                GUI.color = previous;
            }
        }
    }
}
