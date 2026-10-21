using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Editor
{
    /// <summary>What <see cref="NetFrontEndSetup.Run"/> did, or what is missing before it can.</summary>
    public sealed class NetFrontEndSetupResult
    {
        internal NetFrontEndSetupResult()
        {
        }

        /// <summary>True when the scene has a NetFrontEnd now (added by this run or already there).</summary>
        public bool Succeeded { get; internal set; }

        /// <summary>The NetFrontEnd component (typed as Component: its assembly is optional). Null when it could not be added.</summary>
        public Component FrontEnd { get; internal set; }

        /// <summary>The scene setup the front end sits on (null when a required package is missing).</summary>
        public NetSceneSetupResult Scene { get; internal set; }

        /// <summary>Required packages that are not installed, by name. Empty when nothing required is missing.</summary>
        public List<string> MissingPackages { get; } = new List<string>();

        /// <summary>Optional packages that are not installed, by name (the front end works without them).</summary>
        public List<string> MissingOptionalPackages { get; } = new List<string>();

        /// <summary>One line per change made; empty when the scene already had a front end.</summary>
        public List<string> Changes { get; } = new List<string>();

        /// <summary>What to tell the user: what changed, or exactly what to add to Packages/manifest.json.</summary>
        public string Message { get; internal set; }
    }

    /// <summary>
    /// MindAttic ▸ Multiplayer ▸ Add Front End: gives the open scene the drop-in multiplayer menus. It networks the scene
    /// first (<see cref="NetSceneSetup.Run"/>), then adds a NetFrontEnd beside the NetBootstrap. The front end lives in the
    /// optional Unity.Multiplayer.UI assembly, which compiles only when MindAttic Components is installed; when it is not,
    /// nothing changes and the result says exactly which manifest lines to add.
    /// </summary>
    public static class NetFrontEndSetup
    {
        /// <summary>The menu item.</summary>
        public const string MenuPath = "MindAttic/Multiplayer/Add Front End";

        /// <summary>The UI package the front end is built from (required).</summary>
        public const string ComponentsPackage = "com.mindattic.components.unity";

        /// <summary>The profiles package the player's name and colour come from (optional).</summary>
        public const string SharedPackage = "com.mindattic.unity.shared";

        /// <summary>The Packages/manifest.json line for <see cref="ComponentsPackage"/>, for a game kept beside the MindAttic repos.</summary>
        public const string ComponentsManifestLine = "\"" + ComponentsPackage + "\": \"file:../../MindAttic.Components/unity/com.mindattic.components.unity\"";

        /// <summary>The Packages/manifest.json line for <see cref="SharedPackage"/>.</summary>
        public const string SharedManifestLine = "\"" + SharedPackage + "\": \"file:../../Unity.Shared/unity/com.mindattic.unity.shared\"";

        /// <summary>The front end's type, looked up by name since its assembly is optional.</summary>
        public const string FrontEndTypeName = "Unity.Multiplayer.UI.NetFrontEnd, Unity.Multiplayer.UI";

        /// <summary>Whether a package is installed; replaceable for tests.</summary>
        internal static Func<string, bool> IsPackageInstalled = DefaultIsPackageInstalled;

        /// <summary>Finds the front end's type; replaceable for tests.</summary>
        internal static Func<Type> FindFrontEndType = () => Type.GetType(FrontEndTypeName, false);

        [MenuItem(MenuPath, false, 1)]
        private static void AddToActiveScene()
        {
            NetFrontEndSetupResult result = Run(SceneManager.GetActiveScene());
            if (result.Succeeded)
            {
                Selection.activeObject = result.FrontEnd;
                Debug.Log("[MindAttic.Multiplayer] " + result.Message, result.FrontEnd);
            }
            else
            {
                Debug.LogWarning("[MindAttic.Multiplayer] " + result.Message);
            }

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Add Front End", result.Message, "OK");
            }
        }

        /// <summary>Adds the front end to <paramref name="scene"/> (see the class summary). Running it again changes nothing.</summary>
        /// <param name="scene">A loaded scene.</param>
        /// <param name="options">Passed to <see cref="NetSceneSetup.Run"/> when the scene is networked first.</param>
        /// <exception cref="ArgumentException"><paramref name="scene"/> is not loaded.</exception>
        public static NetFrontEndSetupResult Run(Scene scene, NetSceneSetupOptions options = null)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException("The scene must be loaded.", nameof(scene));
            }

            var result = new NetFrontEndSetupResult();
            if (!IsPackageInstalled(ComponentsPackage))
            {
                result.MissingPackages.Add(ComponentsPackage);
            }

            if (!IsPackageInstalled(SharedPackage))
            {
                result.MissingOptionalPackages.Add(SharedPackage);
            }

            if (result.MissingPackages.Count > 0)
            {
                result.Message = MissingMessage(result);
                return result;
            }

            Type frontEndType = FindFrontEndType();
            if (frontEndType == null)
            {
                result.Message = "MindAttic Components is installed but Unity.Multiplayer.UI has not compiled yet. "
                    + "Wait for the editor to finish compiling (or fix the errors in the Console), then run " + MenuPath + " again.";
                return result;
            }

            int undoGroup = Undo.GetCurrentGroup();
            result.Scene = NetSceneSetup.Run(scene, options);
            result.Changes.AddRange(result.Scene.Changes);
            Undo.SetCurrentGroupName("Add Multiplayer Front End");

            GameObject root = result.Scene.Root;
            Component frontEnd = root.GetComponent(frontEndType);
            if (frontEnd == null)
            {
                frontEnd = Undo.AddComponent(root, frontEndType);
                var serialized = new SerializedObject(frontEnd);
                SerializedProperty bootstrap = serialized.FindProperty("bootstrap");
                if (bootstrap != null)
                {
                    bootstrap.objectReferenceValue = result.Scene.Bootstrap;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                result.Changes.Add("Added NetFrontEnd to '" + root.name + "'.");
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
            result.FrontEnd = frontEnd;
            result.Succeeded = true;
            result.Message = (result.Changes.Count == 0
                    ? "The scene already has the multiplayer front end."
                    : "Front end added. Press Play: the main menu offers Host, Join by IP, Join by code and Settings.\n  "
                        + string.Join("\n  ", result.Changes))
                + (result.MissingOptionalPackages.Count > 0
                    ? "\nOptional: add " + SharedManifestLine + " to Packages/manifest.json so the player's name and colour come from "
                        + "their MindAttic profile; without it they are kept in PlayerPrefs."
                    : string.Empty);
            return result;
        }

        private static string MissingMessage(NetFrontEndSetupResult result)
        {
            var lines = new List<string> { ComponentsManifestLine };
            if (result.MissingOptionalPackages.Contains(SharedPackage))
            {
                lines.Add(SharedManifestLine + "   (optional: the player's MindAttic profile name and colour)");
            }

            return "The front end needs MindAttic Components (" + ComponentsPackage + "), which is not installed. "
                + "Add these lines to the \"dependencies\" of Packages/manifest.json (the paths assume the MindAttic repos sit beside "
                + "your game's folder; adjust them if not), let Unity import, then run " + MenuPath + " again:\n  "
                + string.Join(",\n  ", lines);
        }

        private static bool DefaultIsPackageInstalled(string name)
        {
            foreach (UnityEditor.PackageManager.PackageInfo package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
