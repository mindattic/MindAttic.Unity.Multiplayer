using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;

namespace Unity.Multiplayer.Editor
{
    /// <summary>
    /// Switches the <c>MINDATTIC_NGO_TESTABLE</c> scripting define on while the project lists Netcode for GameObjects under
    /// <c>"testables"</c> (UM-T67). The test kit (<c>Unity.Multiplayer.Testing</c>) builds on NGO's test helpers, which only compile
    /// for a testable NGO, and an asmdef cannot see the manifest, so its define constraint waits for this symbol: a project that
    /// adds only the package line compiles without the kit, and one that lists NGO as testable gets it on the next domain reload.
    /// <para>
    /// UM-T72: this script only runs once the project has compiled, so on a clean checkout it cannot rescue a game test assembly
    /// that references the kit without the same constraint (CS0234, and a batch build aborts before any script runs). Two things
    /// make the first compile safe: every asmdef under Assets that references <c>Unity.Multiplayer.Testing</c> carries the
    /// constraint too (the New Game template has it, and this script adds it to an older game's asmdefs), and the define is
    /// committed with the game's ProjectSettings/ProjectSettings.asset (this script says so whenever it changes that file).
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class TestKitDefine
    {
        /// <summary>The scripting define the test kit's assembly requires.</summary>
        public const string Symbol = "MINDATTIC_NGO_TESTABLE";

        /// <summary>The test kit's assembly name, as a game's asmdef references it.</summary>
        public const string KitAssembly = "Unity.Multiplayer.Testing";

        /// <summary>The test kit asmdef's GUID, for an asmdef that references it as <c>GUID:...</c>.</summary>
        public const string KitAssemblyGuid = "21b099516d5e48d8a874a8cd8dd49423";

        private const string Ngo = "com.unity.netcode.gameobjects";

        static TestKitDefine()
        {
            // At once, not on a later update: a define change recompiles, and that must happen before a batch
            // -executeMethod starts, or its callbacks die with the domain.
            Apply();
            AddConstraintToGameAssemblies();
        }

        /// <summary>True when the manifest JSON lists NGO in its <c>"testables"</c> array.</summary>
        public static bool ListsNgoAsTestable(string manifestJson)
        {
            if (string.IsNullOrEmpty(manifestJson))
            {
                return false;
            }

            Match m = Regex.Match(manifestJson, @"""testables""\s*:\s*\[(?<list>[^\]]*)\]");
            return m.Success && m.Groups["list"].Value.IndexOf("\"" + Ngo + "\"", StringComparison.Ordinal) >= 0;
        }

        /// <summary><paramref name="defines"/> with the symbol present or absent as asked (a semicolon-separated list).</summary>
        public static string WithSymbol(string defines, bool present)
        {
            string[] parts = string.IsNullOrEmpty(defines) ? new string[0] : defines.Split(';');
            var kept = new System.Collections.Generic.List<string>();
            foreach (string p in parts)
            {
                if (p.Length > 0 && p != Symbol)
                {
                    kept.Add(p);
                }
            }

            if (present)
            {
                kept.Add(Symbol);
            }

            return string.Join(";", kept);
        }

        /// <summary>True when the asmdef JSON references the test kit, by name or by GUID.</summary>
        public static bool ReferencesKit(string asmdefJson)
        {
            Match m = Regex.Match(asmdefJson ?? string.Empty, @"""references""\s*:\s*\[(?<list>[^\]]*)\]");
            if (!m.Success)
            {
                return false;
            }

            string list = m.Groups["list"].Value;
            return list.IndexOf("\"" + KitAssembly + "\"", StringComparison.Ordinal) >= 0
                || list.IndexOf("\"GUID:" + KitAssemblyGuid + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// <paramref name="asmdefJson"/> with <see cref="Symbol"/> added to its <c>"defineConstraints"</c> (created when absent),
        /// or null when it needs no change: it does not reference the kit, or already carries the constraint.
        /// </summary>
        public static string WithKitConstraint(string asmdefJson)
        {
            if (!ReferencesKit(asmdefJson))
            {
                return null;
            }

            const string entry = "\"" + Symbol + "\"";
            Match m = Regex.Match(asmdefJson, @"""defineConstraints""\s*:\s*\[(?<list>[^\]]*)\]");
            if (m.Success)
            {
                Group list = m.Groups["list"];
                if (list.Value.IndexOf(entry, StringComparison.Ordinal) >= 0)
                {
                    return null;
                }

                string items = list.Value.Trim().Length == 0
                    ? "\n        " + entry + "\n    "
                    : list.Value.TrimEnd() + ",\n        " + entry + "\n    ";
                return asmdefJson.Substring(0, list.Index) + items + asmdefJson.Substring(list.Index + list.Length);
            }

            int close = asmdefJson.LastIndexOf('}');
            return asmdefJson.Substring(0, close).TrimEnd() + ",\n    \"defineConstraints\": [\n        " + entry + "\n    ]\n"
                + asmdefJson.Substring(close);
        }

        /// <summary>
        /// Switches the define on or off to match the manifest. Returns true when it changed ProjectSettings.asset, and then
        /// says to commit it: a clean checkout must find the define already in place (UM-T72).
        /// </summary>
        public static bool Apply()
        {
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
                bool want = File.Exists(path) && ListsNgoAsTestable(File.ReadAllText(path));
                bool changed = Set(NamedBuildTarget.Standalone, want);
                changed |= Set(NamedBuildTarget.Server, want);
                changed |= Set(NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)), want);
                if (changed)
                {
                    UnityEngine.Debug.Log("MindAttic Multiplayer: " + (want ? "added " + Symbol + " to" : "removed " + Symbol + " from")
                        + " Player Settings > Scripting Define Symbols, since Packages/manifest.json " + (want ? "lists " : "does not list ") + Ngo
                        + " under \"testables\". Commit ProjectSettings/ProjectSettings.asset with the manifest, so a clean checkout has the"
                        + " define on its first compile and does not change the file again.");
                }

                return changed;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("MindAttic Multiplayer: could not update " + Symbol + ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Adds the <see cref="Symbol"/> constraint to every asmdef under Assets that references the test kit without it (a game
        /// made before UM-T67), so where the define is not set that assembly is left out instead of failing the compile.
        /// Returns the project-relative paths it changed.
        /// </summary>
        public static System.Collections.Generic.List<string> AddConstraintToGameAssemblies(string projectFolder = null)
        {
            var changed = new System.Collections.Generic.List<string>();
            try
            {
                projectFolder = Path.GetFullPath(projectFolder ?? Directory.GetCurrentDirectory());
                string assets = Path.Combine(projectFolder, "Assets");
                if (!Directory.Exists(assets))
                {
                    return changed;
                }

                foreach (string file in Directory.GetFiles(assets, "*.asmdef", SearchOption.AllDirectories))
                {
                    string after = WithKitConstraint(File.ReadAllText(file));
                    if (after != null)
                    {
                        File.WriteAllText(file, after, new System.Text.UTF8Encoding(false));
                        changed.Add(file.Substring(projectFolder.Length).TrimStart('\\', '/').Replace('\\', '/'));
                    }
                }

                if (changed.Count > 0)
                {
                    UnityEngine.Debug.LogWarning("MindAttic Multiplayer: added the " + Symbol + " define constraint to " + string.Join(", ", changed)
                        + ". An assembly that references " + KitAssembly + " needs it, or a clean checkout fails its first compile (CS0234)"
                        + " before the define can be set. Commit the change.");
                    EditorApplication.delayCall += () => AssetDatabase.Refresh();
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("MindAttic Multiplayer: could not check the game's test assemblies for " + Symbol + ": " + e.Message);
            }

            return changed;
        }

        private static bool Set(NamedBuildTarget target, bool want)
        {
            if (target == NamedBuildTarget.Unknown)
            {
                return false;
            }

            string current = PlayerSettings.GetScriptingDefineSymbols(target);
            bool has = Array.IndexOf(current.Split(';'), Symbol) >= 0;
            if (has != want)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, WithSymbol(current, want));
                return true;
            }

            return false;
        }
    }
}
