// The Minimal sample's menu: host, join by IP, or host and join with a join code, then leave.
// It drives the scene's NetBootstrap, the one component that networks a scene. Drawn with IMGUI so it needs no UI
// package. README.md walks through it.
using System;
using System.Threading.Tasks;
using MindAttic.Unity.Multiplayer;      // NetBootstrap and NetSession
using MindAttic.Unity.Multiplayer.Core; // Disconnection
using UnityEngine;

namespace MindAttic.Unity.Multiplayer.Samples.Minimal
{
    public sealed class MinimalMenu : MonoBehaviour
    {
        // The scene's NetBootstrap (on the NetworkManager). Its Inspector holds the port, transport, join-code and
        // player settings every button below uses.
        [SerializeField] private NetBootstrap bootstrap;

        // What the player typed.
        private string _address = "127.0.0.1";
        private string _port = string.Empty;
        private string _code = string.Empty;

        // The last thing worth telling the player (a failure, or why the session ended).
        private string _status = string.Empty;

        // True while a join-code call is in flight, so it cannot be clicked twice.
        private bool _busy;

        private void Start()
        {
            if (bootstrap == null)
            {
                bootstrap = NetBootstrap.Instance;
            }

            // Join by IP starts out on the port the host listens on.
            _port = bootstrap.BuildConfig().Port.ToString();

            // The session tells us why it ended, and why a join code could not be made or used.
            bootstrap.Session.Ended += OnEnded;
            bootstrap.Session.JoinCodeFailed += OnJoinCodeFailed;
        }

        private void OnDestroy()
        {
            if (bootstrap != null && bootstrap.Session != null)
            {
                bootstrap.Session.Ended -= OnEnded;
                bootstrap.Session.JoinCodeFailed -= OnJoinCodeFailed;
            }
        }

        private void OnEnded(Disconnection disconnection)
        {
            _status = disconnection.Reason == DisconnectReason.None ? string.Empty : disconnection.ToString();
        }

        private void OnJoinCodeFailed(string message)
        {
            _status = message;
        }

        private void OnGUI()
        {
            NetSession session = bootstrap.Session;

            // Draw behind the players (a higher depth is further back), then the arena (MinimalPlayer draws the players).
            GUI.depth = 1;
            if (session.IsActive)
            {
                GUI.Box(MinimalPlayer.ArenaOnScreen(), GUIContent.none);
            }

            GUILayout.BeginArea(new Rect(10f, 10f, 260f, Screen.height - 20f));
            GUI.enabled = !_busy;

            if (!session.IsActive)
            {
                // Listen server: this game is the server and a player.
                if (GUILayout.Button("Host"))
                {
                    Try(() => bootstrap.StartHost());
                }

                // Join by IP: the host's address and port (127.0.0.1 is this computer).
                GUILayout.Label("Address and port");
                GUILayout.BeginHorizontal();
                _address = GUILayout.TextField(_address, GUILayout.Width(170f));
                _port = GUILayout.TextField(_port);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Join by IP"))
                {
                    ushort port;
                    if (ushort.TryParse(_port, out port))
                    {
                        Try(() => bootstrap.JoinByAddress(_address.Trim(), port));
                    }
                    else
                    {
                        _status = "The port must be a number from 1 to 65535.";
                    }
                }

                // Join codes: Unity Relay for a listen server (com.unity.services.multiplayer), or the rendezvous
                // service when the NetBootstrap has a Rendezvous URL.
                GUILayout.Space(10f);
                if (GUILayout.Button("Host with code"))
                {
                    Await(bootstrap.HostWithCode);
                }

                GUILayout.Label("Join code");
                _code = GUILayout.TextField(_code);
                if (GUILayout.Button("Join by code"))
                {
                    Await(() => bootstrap.JoinByCode(_code));
                }
            }
            else
            {
                // Connected (or connecting): what we are, the code to share, and a way out.
                GUILayout.Label(session.IsHost ? "Hosting" : session.IsServer ? "Serving" : "Client: " + session.State);
                if (session.PublishedCodeText.Length > 0)
                {
                    GUILayout.Label("Join code (share it):");
                    GUILayout.TextField(session.PublishedCodeText);
                }

                GUILayout.Label("Move with the arrow keys or WASD.");
                if (GUILayout.Button("Leave"))
                {
                    bootstrap.Leave();
                }
            }

            GUI.enabled = true;
            GUILayout.Label(_status);
            GUILayout.EndArea();
        }

        // Runs a start or join call and shows what went wrong instead of throwing out of OnGUI.
        private void Try(Func<bool> start)
        {
            try
            {
                _status = start() ? string.Empty : "Could not start: is a session already running?";
            }
            catch (Exception ex)
            {
                _status = ex.Message;
            }
        }

        // Runs a join-code call, which takes a moment (it talks to Relay or the rendezvous service).
        private async void Await<T>(Func<Task<T>> call)
        {
            _busy = true;
            _status = "Working...";
            try
            {
                await call();
                if (_status == "Working...")
                {
                    _status = string.Empty;
                }
            }
            catch (Exception ex)
            {
                _status = ex.Message;
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
