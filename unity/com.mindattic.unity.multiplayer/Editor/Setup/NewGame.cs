using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity.Multiplayer.Editor
{
    /// <summary>What <see cref="NewGame.Create"/> makes.</summary>
    public sealed class NewGameOptions
    {
        /// <summary>The game's name: its C# namespace, its assembly name and its folder under Assets. Made a valid identifier.</summary>
        public string Name { get; set; } = NewGame.TemplateToken;

        /// <summary>The Unity project's folder (the one holding Assets). Default: the open project.</summary>
        public string ProjectFolder { get; set; }

        /// <summary>The template to copy. Default: this package's Samples~/GameTemplate.</summary>
        public string TemplateFolder { get; set; }

        /// <summary>Write mp-host.json at the project root when it has none (server scene, bots, scenario).</summary>
        public bool WriteMpHostJson { get; set; } = true;

        /// <summary>Write .github/workflows/nightly.yml (the reusable game-smoke workflow) when the project has none.</summary>
        public bool WriteNightlyWorkflow { get; set; } = true;
    }

    /// <summary>What <see cref="NewGame.Create"/> did.</summary>
    public sealed class NewGameResult
    {
        internal NewGameResult()
        {
        }

        /// <summary>True when the game was created.</summary>
        public bool Succeeded { get; internal set; }

        /// <summary>The game's folder, as an asset path (Assets/&lt;Name&gt;).</summary>
        public string Folder { get; internal set; }

        /// <summary>The game's scene, as an asset path.</summary>
        public string ScenePath { get; internal set; }

        /// <summary>The manifest check made first.</summary>
        public ManifestCheck Manifest { get; internal set; }

        /// <summary>One line per change.</summary>
        public List<string> Changes { get; } = new List<string>();

        /// <summary>What to tell the user.</summary>
        public string Message { get; internal set; }
    }

    /// <summary>
    /// MindAttic ▸ Multiplayer ▸ New Game: a complete networked game skeleton in the open project, from this package's
    /// Samples~/GameTemplate. It checks Packages/manifest.json first and says exactly which lines are missing (and can add
    /// them), then copies the template into Assets/&lt;Name&gt; with the game's own namespace and fresh asset GUIDs, puts its
    /// scene first in Build Settings, turns on Run In Background, and writes mp-host.json and a nightly smoke workflow.
    /// The skeleton: a scene with NetBootstrap + NetFrontEnd + NetVotes, a player (NetEntity) with one [ToServer] move and
    /// a [Synced] position drawn in its roster colour and name, keyed crates spawned through NetSession.Objects, a restart
    /// vote, an IBotDriver, and EditMode and PlayMode tests on the test kit.
    /// </summary>
    public static class NewGame
    {
        /// <summary>The menu item.</summary>
        public const string MenuPath = "MindAttic/Multiplayer/New Game...";

        /// <summary>The template's folder under the package's Samples~.</summary>
        public const string TemplateFolderName = "GameTemplate";

        /// <summary>The name in the template that becomes the game's name (namespace, assembly names).</summary>
        public const string TemplateToken = "MyGame";

        /// <summary>The template's scene, relative to the game's folder.</summary>
        public const string SceneFile = "Game.unity";

        /// <summary>The bot roles and scenario the template's mp-host.json gives `mp-host game smoke`.</summary>
        public const string SmokeBots = "A,B";

        /// <summary>See <see cref="SmokeBots"/>.</summary>
        public const string SmokeScenario = "crates";

        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".md", ".json", ".txt", ".unity", ".prefab", ".asset", ".meta",
        };

        private static readonly Regex MetaGuid = new Regex(@"^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.Multiline);

        [MenuItem(MenuPath, false, -10)]
        private static void Open()
        {
            NewGameWindow.Open();
        }

        /// <summary>The package's own folder (resolved path), or null when it cannot be found.</summary>
        public static string PackageFolder()
        {
            UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NewGame).Assembly);
            return info != null ? info.resolvedPath : null;
        }

        /// <summary>The template folder this package ships.</summary>
        public static string DefaultTemplateFolder()
        {
            string package = PackageFolder();
            return package == null ? null : Path.Combine(package, "Samples~", TemplateFolderName);
        }

        /// <summary>The open project's folder.</summary>
        public static string ProjectFolder()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }

        /// <summary>A valid C# identifier from <paramref name="name"/> ("my game!" -> "MyGame"); <see cref="TemplateToken"/> when nothing is left.</summary>
        public static string SanitizeName(string name)
        {
            var text = new StringBuilder();
            bool upper = true;
            foreach (char c in name ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    text.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else if (c == '_')
                {
                    text.Append(c);
                }
                else
                {
                    upper = true;
                }
            }

            string result = text.ToString();
            if (result.Length > 0 && char.IsDigit(result[0]))
            {
                result = "Game" + result;
            }

            return result.Length == 0 ? TemplateToken : result;
        }

        /// <summary>A lower-case name for servers and images ("MyGame" -> "mygame", "Space Race" -> "space-race").</summary>
        public static string Slug(string name)
        {
            string slug = Regex.Replace((name ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return slug.Length == 0 ? "game" : slug;
        }

        /// <summary>Checks the open project's Packages/manifest.json against what the template needs.</summary>
        public static ManifestCheck CheckProject(string projectFolder = null)
        {
            projectFolder = projectFolder ?? ProjectFolder();
            string manifestPath = Path.Combine(projectFolder, "Packages", "manifest.json");
            string text = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "{}";
            List<ManifestRequirement> requirements = NewGameManifest.Requirements(Path.Combine(projectFolder, "Packages"), PackageFolder());
            return NewGameManifest.Check(text, requirements, manifestPath.Replace('\\', '/'), IsRegistered);
        }

        /// <summary>
        /// Adds the missing lines whose source folders exist to the open project's Packages/manifest.json and asks Package
        /// Manager to resolve. Returns what it did and what is still missing (a sibling repo not on this machine).
        /// </summary>
        public static string AddMissingPackages(string projectFolder = null)
        {
            projectFolder = projectFolder ?? ProjectFolder();
            ManifestCheck check = CheckProject(projectFolder);
            if (check.Missing.Count == 0)
            {
                return check.Message;
            }

            string path = check.ManifestPath;
            string before = File.ReadAllText(path);
            string after = NewGameManifest.AddLines(before, check.Missing);
            if (after != before)
            {
                File.WriteAllText(path, after);
            }

            var added = check.Missing.Where(r => r.SourceFound).Select(r => r.ToString()).ToList();
            var notFound = check.Missing.Where(r => !r.SourceFound).ToList();
            string message = added.Count > 0
                ? "Added to " + path + ":\n  " + string.Join("\n  ", added)
                : "Nothing could be added to " + path + ".";
            if (notFound.Count > 0)
            {
                message += "\nNot added (the repo is not beside Unity.Multiplayer on this machine; clone it there, or add the line with your own path):\n  "
                    + string.Join("\n  ", notFound.Select(r => r.ToString()));
            }

            if (!Application.isBatchMode)
            {
                UnityEditor.PackageManager.Client.Resolve();
            }

            return message;
        }

        /// <summary>Creates the game (see the class summary). Refuses, changing nothing, when a required package is missing or the folder exists.</summary>
        public static NewGameResult Create(NewGameOptions options)
        {
            options = options ?? new NewGameOptions();
            string name = SanitizeName(options.Name);
            string project = options.ProjectFolder ?? ProjectFolder();
            string template = options.TemplateFolder ?? DefaultTemplateFolder();
            var result = new NewGameResult
            {
                Folder = "Assets/" + name,
                ScenePath = "Assets/" + name + "/" + SceneFile,
                Manifest = CheckProject(project),
            };

            if (!result.Manifest.IsComplete)
            {
                result.Message = "The game needs packages the project does not have yet. " + result.Manifest.Message
                    + "\nRun " + MenuPath.Replace("...", string.Empty) + " and click 'Add missing lines', or add them by hand, then try again.";
                return result;
            }

            if (Type.GetType(NetFrontEndSetup.FrontEndTypeName, false) == null)
            {
                result.Message = "MindAttic Components is in the manifest but Unity.Multiplayer.UI has not compiled yet. "
                    + "Wait for the editor to finish importing (or fix the errors in the Console), then try again.";
                return result;
            }

            if (template == null || !File.Exists(Path.Combine(template, SceneFile)))
            {
                result.Message = "The game template was not found at " + (template ?? "(unknown)") + ".";
                return result;
            }

            string destination = Path.Combine(project, "Assets", name);
            if (Directory.Exists(destination))
            {
                result.Message = result.Folder + " already exists. Pick another name, or delete it first.";
                return result;
            }

            int files = CopyTemplate(template, destination, name);
            result.Changes.Add("Created " + result.Folder + " (" + files + " files) from the game template, namespace " + name + ".");

            if (options.WriteMpHostJson)
            {
                string mpHost = Path.Combine(project, "mp-host.json");
                if (File.Exists(mpHost))
                {
                    result.Changes.Add("Kept the existing mp-host.json; for the smoke test give it \"scene\": \"" + result.ScenePath
                        + "\", \"bots\": \"" + SmokeBots + "\", \"scenario\": \"" + SmokeScenario + "\".");
                }
                else
                {
                    File.WriteAllText(mpHost, MpHostJson(name, result.ScenePath));
                    result.Changes.Add("Wrote mp-host.json (server scene " + result.ScenePath + ", smoke bots " + SmokeBots + ").");
                }
            }

            if (options.WriteNightlyWorkflow)
            {
                string workflow = Path.Combine(project, ".github", "workflows", "nightly.yml");
                if (!File.Exists(workflow))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(workflow));
                    File.WriteAllText(workflow, NightlyWorkflow(result.Manifest));
                    result.Changes.Add("Wrote .github/workflows/nightly.yml (the reusable game-smoke workflow: server + bots under Docker Compose).");
                }
            }

            bool inThisProject = string.Equals(Path.GetFullPath(project).TrimEnd('\\', '/'), Path.GetFullPath(ProjectFolder()).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
            if (inThisProject)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var scenes = EditorBuildSettings.scenes.Where(s => s.path != result.ScenePath).ToList();
                scenes.Insert(0, new EditorBuildSettingsScene(result.ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                result.Changes.Add("Put " + result.ScenePath + " first in Build Settings (the server, bot and client scene).");
                if (!PlayerSettings.runInBackground)
                {
                    PlayerSettings.runInBackground = true;
                    result.Changes.Add("Turned on Player Settings > Run In Background (a host keeps serving when its window loses focus).");
                }

                AssetDatabase.SaveAssets();
            }

            result.Succeeded = true;
            result.Message = "Created " + name + ":\n  " + string.Join("\n  ", result.Changes)
                + "\nOpen " + result.ScenePath + " and press Play, then Host. Tests: Window > General > Test Runner ("
                + name + ".Tests, " + name + ".EditorTests). Server: mp-host game local. Bots: mp-host game smoke."
                + (result.Manifest.Missing.Count > 0 ? "\n" + result.Manifest.Message : string.Empty);
            return result;
        }

        /// <summary>
        /// Copies <paramref name="template"/> into <paramref name="destination"/>: <see cref="TemplateToken"/> becomes
        /// <paramref name="name"/> in every text file, and every asset gets a fresh GUID (with every reference to it updated),
        /// so two games, or a game and the imported sample, never share one. Returns the number of files copied.
        /// </summary>
        /// <param name="template">The template folder.</param>
        /// <param name="destination">The new game's folder (created).</param>
        /// <param name="name">The game's name, already a valid identifier.</param>
        /// <param name="freshGuids">False keeps the template's GUIDs (for re-authoring the template itself).</param>
        public static int CopyTemplate(string template, string destination, string name, bool freshGuids = true)
        {
            string[] files = Directory.GetFiles(template, "*", SearchOption.AllDirectories);
            var guids = new Dictionary<string, string>();
            foreach (string meta in files.Where(f => freshGuids && f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                Match match = MetaGuid.Match(File.ReadAllText(meta));
                if (match.Success && !guids.ContainsKey(match.Groups[1].Value))
                {
                    guids.Add(match.Groups[1].Value, Guid.NewGuid().ToString("N"));
                }
            }

            int copied = 0;
            foreach (string file in files)
            {
                string relative = file.Substring(template.Length).TrimStart('\\', '/');
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (TextExtensions.Contains(Path.GetExtension(file)))
                {
                    string text = File.ReadAllText(file);
                    foreach (KeyValuePair<string, string> guid in guids)
                    {
                        text = text.Replace(guid.Key, guid.Value);
                    }

                    if (name != TemplateToken)
                    {
                        text = text.Replace(TemplateToken, name);
                    }

                    File.WriteAllText(target, text, new UTF8Encoding(false));
                }
                else
                {
                    File.Copy(file, target);
                }

                copied++;
            }

            // The template folder's own .meta (Assets/<Name>.meta) is left to Unity.
            return copied;
        }

        /// <summary>The mp-host.json the template writes: names, the server scene, and the smoke test's bots and scenario.</summary>
        public static string MpHostJson(string name, string scenePath)
        {
            return "{\n"
                + "  \"name\": \"" + Slug(name) + "\",\n"
                + "  \"scene\": \"" + scenePath + "\",\n"
                + "  \"bots\": \"" + SmokeBots + "\",\n"
                + "  \"scenario\": \"" + SmokeScenario + "\"\n"
                + "}\n";
        }

        /// <summary>A nightly workflow that runs the library's reusable game-smoke workflow, checking out the kit's repos the manifest references.</summary>
        public static string NightlyWorkflow(ManifestCheck manifest)
        {
            return "# The nightly end-to-end smoke (Unity.Multiplayer's reusable game-smoke workflow): one Linux player of this game\n"
                + "# runs as a dedicated server, the rendezvous service and two bots (GameBot) under Docker Compose until both pass.\n"
                + "# Needs the UNITY_LICENSE secret (GameCI), and MINDATTIC_REPOS_TOKEN if a MindAttic repo is private.\n"
                + "# mp-host.json gives the bots and scenario; see https://mindattic.github.io/Unity.Multiplayer/articles/bots.html\n"
                + "name: Nightly smoke\n"
                + "on:\n"
                + "  schedule: [{ cron: '0 10 * * *' }]\n"
                + "  workflow_dispatch: {}\n"
                + "jobs:\n"
                + "  smoke:\n"
                + "    uses: mindattic/Unity.Multiplayer/.github/workflows/game-smoke.yml@main\n"
                + "    with:\n"
                + "      siblings: mindattic/MindAttic.Components mindattic/Unity.Shared\n"
                + "    secrets: inherit\n";
        }

        /// <summary>
        /// Batch-mode entry point: <c>Unity -batchmode -projectPath &lt;project&gt; -executeMethod
        /// Unity.Multiplayer.Editor.NewGame.CreateFromCommandLine [-gameName &lt;Name&gt;] [-addPackages] [-checkOnly]</c>.
        /// Exit codes: 0 created (or, with -addPackages, lines added: start Unity again to create), 2 packages missing,
        /// 1 any other failure. Everything it says goes to the log.
        /// </summary>
        public static void CreateFromCommandLine()
        {
            int exit = 1;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string name = Arg(args, "-gameName") ?? TemplateToken;
                ManifestCheck check = CheckProject();
                Debug.Log("[NewGame] " + check.Message);
                if (args.Contains("-checkOnly"))
                {
                    exit = check.IsComplete ? 0 : 2;
                }
                else if (!check.IsComplete || (args.Contains("-addPackages") && check.Missing.Count > 0))
                {
                    if (args.Contains("-addPackages"))
                    {
                        Debug.Log("[NewGame] " + AddMissingPackages());
                        exit = CheckProject().IsComplete ? 0 : 2;
                    }
                    else
                    {
                        exit = 2;
                    }
                }
                else
                {
                    NewGameResult result = Create(new NewGameOptions { Name = name });
                    Debug.Log("[NewGame] " + result.Message);
                    exit = result.Succeeded ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                exit = 1;
            }

            EditorApplication.Exit(exit);
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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

        internal static void OpenScene(NewGameResult result)
        {
            if (result != null && result.Succeeded && !Application.isBatchMode && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(result.ScenePath);
            }
        }
    }
}
