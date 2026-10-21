using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity.Multiplayer.Editor
{
    /// <summary>Where a <see cref="ManifestRequirement"/> goes in Packages/manifest.json.</summary>
    public enum ManifestSection
    {
        /// <summary>A <c>"name": "version or file: path"</c> line under <c>"dependencies"</c>.</summary>
        Dependencies,

        /// <summary>A package name in the <c>"testables"</c> array.</summary>
        Testables,
    }

    /// <summary>One thing the new-game template needs in the project's Packages/manifest.json.</summary>
    public sealed class ManifestRequirement
    {
        /// <summary>Creates a requirement.</summary>
        public ManifestRequirement(string package, string value, ManifestSection section, bool required, string why, bool sourceFound = true)
        {
            Package = package;
            Value = value;
            Section = section;
            Required = required;
            Why = why;
            SourceFound = sourceFound;
        }

        /// <summary>The package name, e.g. com.mindattic.components.unity.</summary>
        public string Package { get; }

        /// <summary>The dependency's value (a version or a <c>file:</c> path); null for a testable.</summary>
        public string Value { get; }

        /// <summary>Which part of the manifest it belongs in.</summary>
        public ManifestSection Section { get; }

        /// <summary>False for a package the game works without (it is still recommended).</summary>
        public bool Required { get; }

        /// <summary>One line on what it is for.</summary>
        public string Why { get; }

        /// <summary>False when <see cref="Value"/> is a <c>file:</c> path to a folder that was not found on this machine.</summary>
        public bool SourceFound { get; }

        /// <summary>The exact text to put in the manifest: <c>"name": "value"</c>, or <c>"name"</c> in testables.</summary>
        public string Line
        {
            get { return Section == ManifestSection.Dependencies ? "\"" + Package + "\": \"" + Value + "\"" : "\"" + Package + "\""; }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return (Section == ManifestSection.Testables ? "testables: " : "dependencies: ") + Line;
        }
    }

    /// <summary>What <see cref="NewGameManifest.Check"/> found missing from a manifest.</summary>
    public sealed class ManifestCheck
    {
        internal ManifestCheck(string manifestPath)
        {
            ManifestPath = manifestPath;
        }

        /// <summary>The manifest checked.</summary>
        public string ManifestPath { get; }

        /// <summary>Everything missing, required or not.</summary>
        public List<ManifestRequirement> Missing { get; } = new List<ManifestRequirement>();

        /// <summary>True when nothing required is missing.</summary>
        public bool IsComplete
        {
            get { return !Missing.Exists(r => r.Required); }
        }

        /// <summary>The exact lines to add and where, or a sentence saying nothing is missing.</summary>
        public string Message
        {
            get
            {
                if (Missing.Count == 0)
                {
                    return ManifestPath + " has every package the game template needs.";
                }

                var text = new StringBuilder();
                List<ManifestRequirement> deps = Missing.FindAll(r => r.Section == ManifestSection.Dependencies);
                List<ManifestRequirement> testables = Missing.FindAll(r => r.Section == ManifestSection.Testables);
                text.Append(IsComplete ? "Recommended additions to " : "Missing from ").Append(ManifestPath).Append(':');
                if (deps.Count > 0)
                {
                    text.Append("\n  in \"dependencies\": {");
                    foreach (ManifestRequirement r in deps)
                    {
                        text.Append("\n    ").Append(r.Line).Append(',').Append("   // ").Append(r.Required ? "" : "optional: ").Append(r.Why);
                        if (!r.SourceFound)
                        {
                            text.Append(" (not found at that path on this machine: clone the repo there or fix the path)");
                        }
                    }

                    text.Append("\n  }");
                }

                if (testables.Count > 0)
                {
                    text.Append("\n  in \"testables\": [");
                    foreach (ManifestRequirement r in testables)
                    {
                        text.Append("\n    ").Append(r.Line).Append("   // ").Append(r.Why);
                    }

                    text.Append("\n  ]");
                }

                return text.ToString();
            }
        }
    }

    /// <summary>
    /// The Packages/manifest.json side of MindAttic ▸ Multiplayer ▸ New Game: which lines the template needs (the kit's
    /// other two packages, by <c>file:</c> path to the repos beside this one; the test framework; NGO as a testable), which
    /// of them a manifest lacks, and the manifest text with them added. Plain text in, text out: no Unity calls.
    /// </summary>
    public static class NewGameManifest
    {
        /// <summary>MindAttic Components: the front end's menus and the vote overlay.</summary>
        public const string ComponentsPackage = NetFrontEndSetup.ComponentsPackage;

        /// <summary>Unity.Shared: the player's MindAttic profile name and colour.</summary>
        public const string SharedPackage = NetFrontEndSetup.SharedPackage;

        /// <summary>Unity's test framework, for the game's own tests.</summary>
        public const string TestFrameworkPackage = "com.unity.test-framework";

        /// <summary>The test framework version the kit is tested with.</summary>
        public const string TestFrameworkVersion = "1.6.0";

        /// <summary>Netcode for GameObjects: listed as a testable, the test kit's fixture builds on its test helpers.</summary>
        public const string NetcodePackage = "com.unity.netcode.gameobjects";

        /// <summary>
        /// The template's requirements for a project whose Packages folder is <paramref name="packagesFolder"/>, with the
        /// <c>file:</c> paths worked out from where this package sits: the kit's repos are expected beside its repository
        /// (<c>&lt;repos&gt;/Unity.Multiplayer/unity/com.mindattic.unity.multiplayer</c>,
        /// <c>&lt;repos&gt;/MindAttic.Components/unity/com.mindattic.components.unity</c>,
        /// <c>&lt;repos&gt;/Unity.Shared/unity/com.mindattic.unity.shared</c>). A path is relative to the Packages folder when
        /// both are on one drive, else absolute; when the repo is not there, the documented sibling path is given instead.
        /// </summary>
        /// <param name="packagesFolder">The project's Packages folder.</param>
        /// <param name="multiplayerPackageFolder">Where com.mindattic.unity.multiplayer is (its resolved path); may be null.</param>
        public static List<ManifestRequirement> Requirements(string packagesFolder, string multiplayerPackageFolder)
        {
            string repos = null;
            if (!string.IsNullOrEmpty(multiplayerPackageFolder))
            {
                // <repos>/Unity.Multiplayer/unity/com.mindattic.unity.multiplayer -> <repos>
                DirectoryInfo package = new DirectoryInfo(multiplayerPackageFolder);
                repos = package.Parent?.Parent?.Parent?.FullName;
            }

            return new List<ManifestRequirement>
            {
                Sibling(ComponentsPackage, "MindAttic.Components", "unity/com.mindattic.components.unity", repos, packagesFolder, true,
                    "MindAttic Components: the drop-in menus (NetFrontEnd) and the vote overlay"),
                Sibling(SharedPackage, "Unity.Shared", "unity/com.mindattic.unity.shared", repos, packagesFolder, false,
                    "Unity.Shared: the player's name and colour from their MindAttic profile (else PlayerPrefs)"),
                new ManifestRequirement(TestFrameworkPackage, TestFrameworkVersion, ManifestSection.Dependencies, true,
                    "Unity Test Framework: the game's EditMode and PlayMode tests"),
                new ManifestRequirement(NetcodePackage, null, ManifestSection.Testables, true,
                    "the test kit (NetSessionTest, NetBotTest) builds on NGO's test helpers"),
            };
        }

        /// <summary>Which of <paramref name="requirements"/> <paramref name="manifestJson"/> lacks.</summary>
        /// <param name="manifestJson">The manifest's text.</param>
        /// <param name="requirements">What to look for.</param>
        /// <param name="manifestPath">Named in the message.</param>
        /// <param name="isInstalledElsewhere">Optional: true for a dependency installed another way (embedded, or pulled in by a package); it then counts as present.</param>
        public static ManifestCheck Check(string manifestJson, IEnumerable<ManifestRequirement> requirements, string manifestPath = "Packages/manifest.json",
            Func<string, bool> isInstalledElsewhere = null)
        {
            var check = new ManifestCheck(manifestPath);
            string dependencies = Block(manifestJson, "dependencies", '{', '}');
            string testables = Block(manifestJson, "testables", '[', ']');
            foreach (ManifestRequirement requirement in requirements)
            {
                string quoted = "\"" + requirement.Package + "\"";
                bool present = requirement.Section == ManifestSection.Dependencies
                    ? (dependencies != null && Regex.IsMatch(dependencies, Regex.Escape(quoted) + @"\s*:"))
                        || (isInstalledElsewhere != null && isInstalledElsewhere(requirement.Package))
                    : testables != null && testables.Contains(quoted);
                if (!present)
                {
                    check.Missing.Add(requirement);
                }
            }

            return check;
        }

        /// <summary>
        /// <paramref name="manifestJson"/> with <paramref name="add"/> inserted: dependency lines at the top of
        /// <c>"dependencies"</c>, testables at the start of <c>"testables"</c> (created when the manifest has none). Requirements
        /// whose <c>file:</c> source was not found are left out, so the manifest never points at a folder that is not there.
        /// </summary>
        /// <exception cref="FormatException">The manifest has no <c>"dependencies"</c> object.</exception>
        public static string AddLines(string manifestJson, IEnumerable<ManifestRequirement> add)
        {
            var deps = new List<string>();
            var testables = new List<string>();
            foreach (ManifestRequirement r in add)
            {
                if (!r.SourceFound)
                {
                    continue;
                }

                (r.Section == ManifestSection.Dependencies ? deps : testables).Add(r.Line);
            }

            string text = manifestJson;
            if (deps.Count > 0)
            {
                Match open = Regex.Match(text, "\"dependencies\"\\s*:\\s*\\{");
                if (!open.Success)
                {
                    throw new FormatException("The manifest has no \"dependencies\" object.");
                }

                text = InsertAfter(text, open.Index + open.Length, deps, '}', "    ");
            }

            if (testables.Count > 0)
            {
                Match open = Regex.Match(text, "\"testables\"\\s*:\\s*\\[");
                if (open.Success)
                {
                    text = InsertAfter(text, open.Index + open.Length, testables, ']', "    ");
                }
                else
                {
                    int close = text.LastIndexOf('}');
                    if (close < 0)
                    {
                        throw new FormatException("The manifest is not a JSON object.");
                    }

                    string before = text.Substring(0, close).TrimEnd();
                    text = before + ",\n  \"testables\": [\n    " + string.Join(",\n    ", testables) + "\n  ]\n" + text.Substring(close);
                }
            }

            return text;
        }

        private static string InsertAfter(string text, int at, List<string> lines, char close, string indent)
        {
            int next = at;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }

            bool empty = next < text.Length && text[next] == close;
            string inserted = "\n" + indent + string.Join(",\n" + indent, lines) + (empty ? "\n  " : ",");
            if (empty)
            {
                return text.Substring(0, at) + inserted + text.Substring(next);
            }

            return text.Substring(0, at) + inserted + text.Substring(at);
        }

        private static ManifestRequirement Sibling(string package, string repo, string inRepo, string repos, string packagesFolder, bool required, string why)
        {
            if (!string.IsNullOrEmpty(repos) && !string.IsNullOrEmpty(packagesFolder))
            {
                string folder = Path.GetFullPath(Path.Combine(repos, repo, inRepo));
                if (Directory.Exists(folder))
                {
                    return new ManifestRequirement(package, "file:" + PathFrom(packagesFolder, folder), ManifestSection.Dependencies, required, why);
                }
            }

            // The documented layout: the game's folder beside the MindAttic repos.
            return new ManifestRequirement(package, "file:../../" + repo + "/" + inRepo, ManifestSection.Dependencies, required, why, sourceFound: false);
        }

        /// <summary>A path from <paramref name="fromFolder"/> to <paramref name="toFolder"/> with forward slashes: relative on one drive, else absolute.</summary>
        public static string PathFrom(string fromFolder, string toFolder)
        {
            string from = Path.GetFullPath(fromFolder);
            string to = Path.GetFullPath(toFolder);
            if (string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(from, to);
                if (!Path.IsPathRooted(relative))
                {
                    return relative.Replace('\\', '/');
                }
            }

            return to.Replace('\\', '/');
        }

        // The text between the brackets of "name": { ... } (or [ ... ]), or null when absent. Brackets in strings are rare
        // enough in a manifest's values (paths, versions) that counting them is safe.
        private static string Block(string json, string name, char open, char close)
        {
            Match start = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(name) + "\"\\s*:\\s*" + Regex.Escape(open.ToString()));
            if (!start.Success)
            {
                return null;
            }

            int depth = 1;
            int from = start.Index + start.Length;
            for (int i = from; i < json.Length; i++)
            {
                if (json[i] == open)
                {
                    depth++;
                }
                else if (json[i] == close && --depth == 0)
                {
                    return json.Substring(from, i - from);
                }
            }

            return json.Substring(from);
        }
    }
}
