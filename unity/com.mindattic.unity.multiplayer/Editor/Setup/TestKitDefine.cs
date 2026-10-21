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
    /// </summary>
    [InitializeOnLoad]
    public static class TestKitDefine
    {
        /// <summary>The scripting define the test kit's assembly requires.</summary>
        public const string Symbol = "MINDATTIC_NGO_TESTABLE";

        private const string Ngo = "com.unity.netcode.gameobjects";

        static TestKitDefine()
        {
            // At once, not on a later update: a define change recompiles, and that must happen before a batch
            // -executeMethod starts, or its callbacks die with the domain.
            Apply();
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

        private static void Apply()
        {
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
                bool want = File.Exists(path) && ListsNgoAsTestable(File.ReadAllText(path));
                Set(NamedBuildTarget.Standalone, want);
                Set(NamedBuildTarget.Server, want);
                Set(NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)), want);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("MindAttic Multiplayer: could not update " + Symbol + ": " + e.Message);
            }
        }

        private static void Set(NamedBuildTarget target, bool want)
        {
            if (target == NamedBuildTarget.Unknown)
            {
                return;
            }

            string current = PlayerSettings.GetScriptingDefineSymbols(target);
            bool has = Array.IndexOf(current.Split(';'), Symbol) >= 0;
            if (has != want)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, WithSymbol(current, want));
            }
        }
    }
}
