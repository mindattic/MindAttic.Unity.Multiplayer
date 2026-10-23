using UnityEditor;
using UnityEngine;

namespace Unity.Multiplayer.Editor
{
    /// <summary>
    /// The window behind MindAttic ▸ Multiplayer ▸ New Game: the manifest check, the game's name and Create, and the optional
    /// Unity Relay step (the package, the Cloud project link) for join codes that need no port forwarding.
    /// </summary>
    internal sealed class NewGameWindow : EditorWindow
    {
        private string _name;
        private ManifestCheck _check;
        private RelaySetupStatus _relay;
        private string _status;
        private Vector2 _scroll;

        internal static void Open()
        {
            var window = GetWindow<NewGameWindow>(true, "New Multiplayer Game", true);
            window.minSize = new Vector2(560f, 420f);
            window.Refresh();
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_name))
            {
                _name = NewGame.SanitizeName(PlayerSettings.productName);
            }

            Refresh();
        }

        private void OnFocus()
        {
            Refresh();
        }

        private void Refresh()
        {
            _check = NewGame.CheckProject();
            _relay = RelaySetupCheck.Check();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("A complete networked game in this project", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Menus (Host, Join by IP or code, lobby, reconnect, Esc menu), a player moved by the server in its roster name and "
                + "colour, crates found by key, a restart vote, a bot, EditMode and PlayMode tests, and mp-host.json for "
                + "'mp-host game local | deploy | smoke'. Built from Unity.Multiplayer, MindAttic Components and Unity.Shared.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("1. Packages", EditorStyles.boldLabel);
            if (_check.Missing.Count == 0)
            {
                EditorGUILayout.HelpBox(_check.Message, MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(_check.IsComplete ? "Everything required is installed; one optional package is not." : "Missing packages: the game cannot be created yet.",
                    _check.IsComplete ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.SelectableLabel(_check.Message, EditorStyles.textArea, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * (_check.Missing.Count * 2 + 4)));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add missing lines to Packages/manifest.json"))
                    {
                        _status = NewGame.AddMissingPackages();
                        Refresh();
                    }

                    if (GUILayout.Button("Copy", GUILayout.Width(60f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = _check.Message;
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. Your game", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField(new GUIContent("Name", "Its C# namespace, assembly name and folder under Assets."), _name);
            string clean = NewGame.SanitizeName(_name);
            EditorGUILayout.LabelField("Creates", "Assets/" + clean + "  (namespace " + clean + ")");

            using (new EditorGUI.DisabledScope(!_check.IsComplete || EditorApplication.isCompiling))
            {
                if (GUILayout.Button("Create game", GUILayout.Height(28f)))
                {
                    NewGameResult result = NewGame.Create(new NewGameOptions { Name = clean });
                    _status = result.Message;
                    if (result.Succeeded)
                    {
                        Debug.Log("[MindAttic.Multiplayer] " + result.Message);
                        NewGame.OpenScene(result);
                    }
                    else
                    {
                        Debug.LogWarning("[MindAttic.Multiplayer] " + result.Message);
                    }
                }
            }

            EditorGUILayout.Space();
            DrawRelayStep();

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.SelectableLabel(_status, EditorStyles.textArea, GUILayout.MinHeight(120f));
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>Step 3: Unity Relay, so a game hosted at home gets a join code without port forwarding.</summary>
        private void DrawRelayStep()
        {
            EditorGUILayout.LabelField("3. Join codes without port forwarding (Unity Relay, optional)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_relay.Message, _relay.IsReady ? MessageType.Info : MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_relay.PackageInManifest))
                {
                    if (GUILayout.Button("Add " + Core.RelaySetup.PackageName))
                    {
                        _status = RelaySetupCheck.AddPackage();
                        Refresh();
                    }
                }

                if (GUILayout.Button("Link project (Services settings)"))
                {
                    RelaySetupCheck.OpenServicesSettings();
                }

                if (GUILayout.Button("Unity Cloud dashboard"))
                {
                    RelaySetupCheck.OpenDashboard();
                }
            }
        }
    }
}
