using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>How far a project is from Unity Relay join codes working (<see cref="RelaySetup.Check"/>).</summary>
    public enum RelayReadiness
    {
        /// <summary>com.unity.services.multiplayer is not installed, so <c>MINDATTIC_UGS</c> is not defined.</summary>
        NotInstalled = 0,

        /// <summary>The package is installed but the project is not linked to a Unity Cloud project (no cloud project id).</summary>
        NotLinked = 1,

        /// <summary>Installed and linked: Relay can be asked for allocations (Relay must still be on for the Cloud project).</summary>
        Ready = 2,
    }

    /// <summary>
    /// What Unity Relay join codes need, and what to say when something is missing (UM-T71). Engine-free: the runtime
    /// (<c>RelayJoinCodeService</c>), the drop-in front end and the editor's New Game wizard all ask it, so a developer
    /// reads the same sentence in a menu toast, the Console and the wizard.
    /// </summary>
    /// <remarks>
    /// Relay needs three things: the com.unity.services.multiplayer package (<see cref="PackageName"/>), the Unity
    /// project linked to a Unity Cloud project (a cloud project id, which Unity bakes into every build), and Relay turned
    /// on for that Cloud project in the dashboard. Players sign in anonymously (Unity Authentication) and need no account.
    /// </remarks>
    public static class RelaySetup
    {
        /// <summary>The package that brings Unity Relay (and defines <c>MINDATTIC_UGS</c>).</summary>
        public const string PackageName = "com.unity.services.multiplayer";

        /// <summary>The package version this library is verified against; the wizard and tools/link-unity-cloud.ps1 add it.</summary>
        public const string PackageVersion = "2.4.1";

        /// <summary>The Unity Services environment every Cloud project has; used when none is set.</summary>
        public const string DefaultEnvironment = "production";

        /// <summary>Where a Cloud project is created and Relay is turned on.</summary>
        public const string DashboardUrl = "https://cloud.unity.com/";

        /// <summary>The package is missing.</summary>
        public const string NotInstalledMessage =
            "Unity Relay join codes need the " + PackageName + " package (it defines MINDATTIC_UGS). "
            + "Add it with MindAttic > Multiplayer > New Game... (step 3) or tools/link-unity-cloud.ps1, link the project to a Unity Cloud "
            + "project with Relay on, or use the rendezvous service (NetConfig.RendezvousUrl) with a dedicated server.";

        /// <summary>The project has no cloud project id.</summary>
        public const string NotLinkedMessage =
            "Unity Relay isn't set up: this project isn't linked to a Unity Cloud project. In the editor open Edit > Project Settings > "
            + "Services, pick the organization and the Cloud project and link it (or run tools/link-unity-cloud.ps1 with the project id), "
            + "then turn on Relay for that project at " + DashboardUrl + " and rebuild.";

        /// <summary>Relay answered that it is not enabled (or not allowed) for the linked Cloud project.</summary>
        public const string RelayOffMessage =
            "Unity Relay refused the request: turn on Relay for the linked Cloud project at " + DashboardUrl
            + " (Products > Relay) and check that the project id in Project Settings > Services is that project's.";

        /// <summary>Unity Gaming Services could not be reached.</summary>
        public const string UnreachableMessage =
            "Could not reach Unity Gaming Services: check this computer's internet connection and try again.";

        /// <summary>Where a project stands: <paramref name="installed"/> is whether the package is compiled in.</summary>
        public static RelayReadiness Check(bool installed, string cloudProjectId)
        {
            if (!installed)
            {
                return RelayReadiness.NotInstalled;
            }

            return string.IsNullOrWhiteSpace(cloudProjectId) ? RelayReadiness.NotLinked : RelayReadiness.Ready;
        }

        /// <summary>One sentence for <paramref name="readiness"/>: what is missing, or that Relay is set up.</summary>
        public static string Describe(RelayReadiness readiness)
        {
            switch (readiness)
            {
                case RelayReadiness.NotInstalled:
                    return NotInstalledMessage;
                case RelayReadiness.NotLinked:
                    return NotLinkedMessage;
                default:
                    return "Unity Relay is set up: the package is installed and the project is linked to a Unity Cloud project "
                        + "(Relay must also be on for it at " + DashboardUrl + ").";
            }
        }

        /// <summary><paramref name="environment"/> trimmed, or <see cref="DefaultEnvironment"/> when it is empty.</summary>
        public static string EnvironmentOrDefault(string environment)
        {
            return string.IsNullOrWhiteSpace(environment) ? DefaultEnvironment : environment.Trim();
        }

        /// <summary>
        /// True when <paramref name="value"/> looks like a Unity Cloud project id: a GUID, with or without dashes or
        /// braces. (Organization ids are free-form, so only the project id is checked.)
        /// </summary>
        public static bool IsProjectId(string value)
        {
            Guid ignored;
            return !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out ignored);
        }

        /// <summary>
        /// A message to show for a Unity Services failure: the reason when it is one a developer can fix (not linked,
        /// Relay off, a missing environment, offline), else <paramref name="message"/> itself. <paramref name="typeName"/>
        /// is the exception's type name and <paramref name="reason"/> its UGS reason or error code as text, when it has one.
        /// </summary>
        public static string ExplainFailure(string typeName, string reason, string message, string environment)
        {
            string all = ((typeName ?? string.Empty) + " " + (reason ?? string.Empty) + " " + (message ?? string.Empty)).ToLowerInvariant();
            if (all.Contains("projectnotlinked") || all.Contains("link your unity project") || all.Contains("cloud project id")
                || (all.Contains("projectid") && (all.Contains("empty") || all.Contains("missing") || all.Contains("null"))))
            {
                return NotLinkedMessage;
            }

            if (all.Contains("environment") && (all.Contains("not found") || all.Contains("notfound") || all.Contains("invalid") || all.Contains("does not exist")))
            {
                return "The Unity Services environment \"" + EnvironmentOrDefault(environment) + "\" does not exist in the linked Cloud project: "
                    + "create it at " + DashboardUrl + " or set Relay Environment on the NetBootstrap to one that does (" + DefaultEnvironment + " by default).";
            }

            if (all.Contains("forbidden") || all.Contains("403") || all.Contains("not enabled") || all.Contains("unauthorized") || all.Contains("401"))
            {
                return RelayOffMessage;
            }

            if (all.Contains("cannot resolve") || all.Contains("could not resolve") || all.Contains("network") || all.Contains("timeout")
                || all.Contains("timed out") || all.Contains("unreachable") || all.Contains("no internet"))
            {
                return UnreachableMessage;
            }

            return string.IsNullOrWhiteSpace(message) ? "Unity Relay failed." : message.Trim();
        }
    }
}
