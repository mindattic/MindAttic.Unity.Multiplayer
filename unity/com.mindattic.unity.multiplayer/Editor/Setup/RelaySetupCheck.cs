using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Multiplayer.Core;
using UnityEditor;
using UnityEngine;

namespace Unity.Multiplayer.Editor
{
    /// <summary>Where the open project stands on Unity Relay join codes (<see cref="RelaySetupCheck.Check"/>).</summary>
    public sealed class RelaySetupStatus
    {
        internal RelaySetupStatus()
        {
        }

        /// <summary>The manifest checked.</summary>
        public string ManifestPath { get; internal set; }

        /// <summary>True when Packages/manifest.json lists com.unity.services.multiplayer (or it is installed another way).</summary>
        public bool PackageInManifest { get; internal set; }

        /// <summary>True when the package has compiled in (<c>MINDATTIC_UGS</c> is defined).</summary>
        public bool PackageCompiled { get; internal set; }

        /// <summary>The linked Unity Cloud project's id; empty when the project is not linked.</summary>
        public string ProjectId { get; internal set; }

        /// <summary>The linked Cloud project's name, when known.</summary>
        public string ProjectName { get; internal set; }

        /// <summary>The linked Cloud project's organization, when known.</summary>
        public string OrganizationName { get; internal set; }

        /// <summary>The package (in the manifest) and the link, as <see cref="RelaySetup.Check"/> sees them.</summary>
        public RelayReadiness Readiness
        {
            get { return RelaySetup.Check(PackageInManifest, ProjectId); }
        }

        /// <summary>True when the package is installed and compiled and the project is linked.</summary>
        public bool IsReady
        {
            get { return Readiness == RelayReadiness.Ready && PackageCompiled; }
        }

        /// <summary>One line per requirement, with what to do about each one that is missing.</summary>
        public string Message
        {
            get
            {
                var text = new StringBuilder();
                if (!PackageInManifest)
                {
                    text.Append("Package: ").Append(RelaySetup.PackageName).Append(" is not in ").Append(ManifestPath)
                        .Append(". Add it (\"").Append(RelaySetup.PackageName).Append("\": \"").Append(RelaySetup.PackageVersion).Append("\").");
                }
                else if (!PackageCompiled)
                {
                    text.Append("Package: ").Append(RelaySetup.PackageName).Append(" is in the manifest; Unity has not finished importing it yet.");
                }
                else
                {
                    text.Append("Package: ").Append(RelaySetup.PackageName).Append(" is installed (MINDATTIC_UGS).");
                }

                text.Append('\n');
                if (string.IsNullOrWhiteSpace(ProjectId))
                {
                    text.Append("Cloud project: not linked. ").Append(RelaySetup.NotLinkedMessage);
                }
                else
                {
                    text.Append("Cloud project: linked to ").Append(string.IsNullOrWhiteSpace(ProjectName) ? "(unnamed)" : ProjectName)
                        .Append(" (").Append(ProjectId).Append(')');
                    if (!string.IsNullOrWhiteSpace(OrganizationName))
                    {
                        text.Append(" in ").Append(OrganizationName);
                    }

                    text.Append('.');
                }

                text.Append("\nRelay: turn it on for the Cloud project at ").Append(RelaySetup.DashboardUrl)
                    .Append(" (Products > Relay). Players sign in anonymously and need no Unity account.");
                return text.ToString();
            }
        }
    }

    /// <summary>
    /// The Unity Relay side of the setup (UM-T71): is com.unity.services.multiplayer in the manifest and compiled, and is the
    /// project linked to a Unity Cloud project. MindAttic ▸ Multiplayer ▸ New Game shows it as step 3, MindAttic ▸
    /// Multiplayer ▸ Check Unity Relay Setup logs it, and <see cref="CheckBatch"/> runs it from the command line
    /// (<c>-executeMethod Unity.Multiplayer.Editor.RelaySetupCheck.CheckBatch</c>, exit code 0 when Relay is ready).
    /// Linking itself is done in Project Settings ▸ Services, or with the editor closed by tools/link-unity-cloud.ps1.
    /// </summary>
    public static class RelaySetupCheck
    {
        /// <summary>The menu item.</summary>
        public const string MenuPath = "MindAttic/Multiplayer/Check Unity Relay Setup";

        /// <summary>The Project Settings page where a project is linked to a Unity Cloud project.</summary>
        public const string ServicesSettingsPath = "Project/Services";

        /// <summary>The manifest line the wizard adds.</summary>
        public static ManifestRequirement PackageRequirement
        {
            get
            {
                return new ManifestRequirement(RelaySetup.PackageName, RelaySetup.PackageVersion, ManifestSection.Dependencies, false,
                    "Unity Relay join codes: a home host needs no port forwarding");
            }
        }

        /// <summary>Checks the open project (or the project at <paramref name="projectFolder"/>'s manifest, with the open project's link).</summary>
        public static RelaySetupStatus Check(string projectFolder = null)
        {
            string openProject = NewGame.ProjectFolder();
            projectFolder = projectFolder ?? openProject;
            string manifestPath = Path.Combine(projectFolder, "Packages", "manifest.json");
            string text = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "{}";

            // Packages registered another way (embedded, pulled in by a package) count only for the open project itself.
            bool isOpenProject = string.Equals(Path.GetFullPath(projectFolder).TrimEnd('\\', '/'), Path.GetFullPath(openProject).TrimEnd('\\', '/'),
                System.StringComparison.OrdinalIgnoreCase);
            ManifestCheck manifest = NewGameManifest.Check(text, new List<ManifestRequirement> { PackageRequirement }, manifestPath.Replace('\\', '/'),
                isOpenProject ? IsRegistered : (System.Func<string, bool>)null);
            return new RelaySetupStatus
            {
                ManifestPath = manifest.ManifestPath,
                PackageInManifest = manifest.Missing.Count == 0,
                PackageCompiled = RelayJoinCodeService.IsAvailable,
                ProjectId = CloudProjectSettings.projectId ?? string.Empty,
                ProjectName = CloudProjectSettings.projectName,
                OrganizationName = CloudProjectSettings.organizationName,
            };
        }

        /// <summary>
        /// Adds com.unity.services.multiplayer to the project's Packages/manifest.json (when it is not there) and asks Package
        /// Manager to resolve. Returns what it did.
        /// </summary>
        public static string AddPackage(string projectFolder = null)
        {
            projectFolder = projectFolder ?? NewGame.ProjectFolder();
            RelaySetupStatus status = Check(projectFolder);
            if (status.PackageInManifest)
            {
                return RelaySetup.PackageName + " is already in " + status.ManifestPath + ".";
            }

            string before = File.ReadAllText(status.ManifestPath);
            File.WriteAllText(status.ManifestPath, NewGameManifest.AddLines(before, new[] { PackageRequirement }));
            if (!Application.isBatchMode)
            {
                UnityEditor.PackageManager.Client.Resolve();
            }

            return "Added to " + status.ManifestPath + ": " + PackageRequirement.Line
                + ". Next: link the project to a Unity Cloud project (Project Settings > Services) and turn on Relay for it.";
        }

        /// <summary>Opens Project Settings ▸ Services, where the project is linked to a Unity Cloud project.</summary>
        public static void OpenServicesSettings()
        {
            SettingsService.OpenProjectSettings(ServicesSettingsPath);
        }

        /// <summary>Opens the Unity Cloud dashboard, where a Cloud project is made and Relay is turned on.</summary>
        public static void OpenDashboard()
        {
            Application.OpenURL(RelaySetup.DashboardUrl);
        }

        [MenuItem(MenuPath, false, 20)]
        private static void LogStatus()
        {
            RelaySetupStatus status = Check();
            string line = "[MindAttic.Multiplayer] Unity Relay setup:\n" + status.Message;
            if (status.IsReady)
            {
                Debug.Log(line);
            }
            else
            {
                Debug.LogWarning(line);
            }
        }

        /// <summary>
        /// Batch mode: logs <see cref="RelaySetupStatus.Message"/> and exits 0 when Relay is ready, 1 when not. Pass
        /// <c>-addRelayPackage</c> to add the package line first (it compiles in on the next import).
        /// </summary>
        public static void CheckBatch()
        {
            foreach (string arg in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, "-addRelayPackage", System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[MindAttic.Multiplayer] " + AddPackage());
                }
            }

            RelaySetupStatus status = Check();
            Debug.Log("[MindAttic.Multiplayer] Unity Relay setup (" + status.Readiness + (status.PackageCompiled ? "" : ", not compiled in") + "):\n" + status.Message);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(status.IsReady ? 0 : 1);
            }
        }

        private static bool IsRegistered(string package)
        {
            foreach (UnityEditor.PackageManager.PackageInfo info in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (info.name == package)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
