// The game's one scene component: it serves the rules (Arena) on the server, registers the votes the game offers, and
// adds the game's own items to the drop-in front end. The menus, lobby, join codes, reconnect, roster and vote overlay
// are the kit's; this is the only glue a game writes. Runs on every peer: a dedicated server and a bot run this scene too.
using Unity.Multiplayer;    // NetBootstrap, NetVotes
using Unity.Multiplayer.UI; // NetFrontEnd, MenuPlacement
using UnityEngine;

namespace MyGame
{
    [DefaultExecutionOrder(-800)] // before NetBootstrap.Start, which may start a headless server at once
    public sealed class Game : MonoBehaviour
    {
        [Tooltip("The crate prefab (NetworkObject + NetKey + Crate), also in the scene's prefab list.")]
        [SerializeField] private GameObject cratePrefab;

        private Arena _arena;

        /// <summary>The rules this scene serves (null before Start).</summary>
        public Arena Rules
        {
            get { return _arena; }
        }

        private void Start()
        {
            NetBootstrap bootstrap = NetBootstrap.Instance;
            if (bootstrap == null || bootstrap.Session == null)
            {
                Debug.LogError("[Game] No NetBootstrap in the scene: run MindAttic > Multiplayer > Set Up Scene.", this);
                return;
            }

            // Server rules: crates spawn when the session runs as a host or dedicated server.
            _arena = Arena.Serve(bootstrap.Session, cratePrefab);

            // Votes: NetVotes (beside the NetBootstrap) spawns the vote board; the front end shows the overlay in game.
            if (NetVotes.Instance != null)
            {
                NetVotes.Instance.AddKind(_arena.CreateRestartVote());
            }

            NetFrontEnd frontEnd = NetFrontEnd.Instance;
            if (frontEnd == null)
            {
                return;
            }

            frontEnd.AddScreen("How to play", s =>
            {
                s.AddText("Move with the arrow keys or WASD. Walk into a crate to push it.");
                s.AddText("Esc opens the menu: invite players, change your name and colour, or vote to restart.");
            }, MenuPlacement.Both);

            frontEnd.AddPauseMenuItems(s => s.AddButton("Vote to restart", StartRestartVote));
            frontEnd.GameStarted += () => Debug.Log("[Game] In the game as " + frontEnd.Session.Identity.DisplayName + ".");
            frontEnd.Left += why => Debug.Log("[Game] Back at the main menu: " + why + ".");
        }

        private static void StartRestartVote()
        {
            if (NetVotes.Instance != null && NetVotes.Instance.Board != null)
            {
                NetVotes.Instance.Board.StartVote(Arena.RestartVote);
            }
        }
    }
}
