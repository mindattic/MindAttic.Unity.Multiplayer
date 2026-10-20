using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace MindAttic.Unity.Multiplayer.Editor
{
    /// <summary>What is wrong with a network prefab.</summary>
    public enum NetPrefabIssueKind
    {
        /// <summary>The prefab is listed for spawning, or carries networked components, but has no NetworkObject on its root.</summary>
        MissingNetworkObject,

        /// <summary>The prefab has a NetworkObject but no prefab list registers it, so spawning it fails.</summary>
        NotRegistered,

        /// <summary>A NetEntity component on the prefab is generic or derives from a generic class (no stable RPC ids).</summary>
        GenericNetEntity,

        /// <summary>A prefab list has an empty entry.</summary>
        EmptyListEntry,
    }

    /// <summary>How bad a <see cref="NetPrefabIssue"/> is.</summary>
    public enum NetPrefabIssueSeverity
    {
        /// <summary>Breaks spawning or the build; the dedicated server build refuses to run.</summary>
        Error,

        /// <summary>Harmless at runtime but worth tidying.</summary>
        Warning,
    }

    /// <summary>One problem the <see cref="NetPrefabValidator"/> found.</summary>
    public sealed class NetPrefabIssue
    {
        internal NetPrefabIssue(NetPrefabIssueKind kind, NetPrefabIssueSeverity severity, UnityEngine.Object context, string message)
        {
            Kind = kind;
            Severity = severity;
            Context = context;
            Message = message;
        }

        /// <summary>What is wrong.</summary>
        public NetPrefabIssueKind Kind { get; }

        /// <summary>Error or warning.</summary>
        public NetPrefabIssueSeverity Severity { get; }

        /// <summary>The prefab (or prefab list) to fix; selecting the logged message pings it.</summary>
        public UnityEngine.Object Context { get; }

        /// <summary>A sentence saying what is wrong and how to fix it.</summary>
        public string Message { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Severity + " " + Kind + ": " + Message;
        }
    }

    /// <summary>The outcome of a validation run.</summary>
    public sealed class NetPrefabValidationReport
    {
        internal NetPrefabValidationReport(List<NetPrefabIssue> issues, int prefabsChecked)
        {
            Issues = issues;
            PrefabsChecked = prefabsChecked;
        }

        /// <summary>Every problem found, errors first.</summary>
        public IReadOnlyList<NetPrefabIssue> Issues { get; }

        /// <summary>How many networked or listed prefabs were checked.</summary>
        public int PrefabsChecked { get; }

        /// <summary>How many errors.</summary>
        public int ErrorCount
        {
            get { return Count(NetPrefabIssueSeverity.Error); }
        }

        /// <summary>How many warnings.</summary>
        public int WarningCount
        {
            get { return Count(NetPrefabIssueSeverity.Warning); }
        }

        /// <summary>True when any issue is an error.</summary>
        public bool HasErrors
        {
            get { return ErrorCount > 0; }
        }

        /// <summary>A one-line summary.</summary>
        public string Summary
        {
            get
            {
                return "Checked " + PrefabsChecked + " network prefab(s): " + ErrorCount + " error(s), " + WarningCount + " warning(s).";
            }
        }

        /// <summary>Logs each issue (with its prefab as the context) and the summary.</summary>
        public void Log()
        {
            foreach (NetPrefabIssue issue in Issues)
            {
                string line = "[MindAttic.Multiplayer] " + issue.Message;
                if (issue.Severity == NetPrefabIssueSeverity.Error)
                {
                    Debug.LogError(line, issue.Context);
                }
                else
                {
                    Debug.LogWarning(line, issue.Context);
                }
            }

            Debug.Log("[MindAttic.Multiplayer] " + Summary);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var text = new StringBuilder(Summary);
            foreach (NetPrefabIssue issue in Issues)
            {
                text.Append('\n').Append(issue);
            }

            return text.ToString();
        }

        private int Count(NetPrefabIssueSeverity severity)
        {
            int n = 0;
            foreach (NetPrefabIssue issue in Issues)
            {
                if (issue.Severity == severity)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>
    /// MindAttic ▸ Multiplayer ▸ Validate Prefabs: checks every network prefab in the project. A prefab is a network
    /// prefab when it carries a NetworkObject or any networked component (a <see cref="NetEntity"/> or other
    /// NetworkBehaviour), or when a prefab list names it. Each must have a NetworkObject on its root, be registered in a
    /// <see cref="NetPrefabList"/> (or an NGO NetworkPrefabsList) asset, and have no <see cref="NetEntity"/> that is
    /// generic or derives from a generic class. The dedicated server build runs it first and stops on errors.
    /// </summary>
    /// <remarks>Any NetworkPrefabsList asset counts, NGO's own DefaultNetworkPrefabs included (NGO adds every
    /// NetworkObject prefab it imports to that list).</remarks>
    public static class NetPrefabValidator
    {
        /// <summary>The menu item.</summary>
        public const string MenuPath = "MindAttic/Multiplayer/Validate Prefabs";

        [MenuItem(MenuPath, false, 20)]
        private static void ValidateMenu()
        {
            NetPrefabValidationReport report = ValidateProject();
            report.Log();
            if (report.Issues.Count > 0)
            {
                EditorUtility.DisplayDialog("Validate Network Prefabs", report.Summary + "\n\nSee the Console; click a message to find its prefab.", "OK");
            }
        }

        /// <summary>
        /// Validates the prefabs under <paramref name="searchFolders"/> (default: Assets) against the prefab lists there.
        /// </summary>
        public static NetPrefabValidationReport ValidateProject(string[] searchFolders = null)
        {
            string[] folders = NetSceneSetup.ValidFolders(searchFolders);
            return Validate(LoadPrefabs(folders), LoadAssets<NetPrefabList>(folders), LoadAssets<NetworkPrefabsList>(folders));
        }

        /// <summary>
        /// Validates <paramref name="prefabs"/> plus every prefab the lists name, counting as registered what
        /// <paramref name="lists"/> and <paramref name="ngoLists"/> contain.
        /// </summary>
        public static NetPrefabValidationReport Validate(IEnumerable<GameObject> prefabs, IEnumerable<NetPrefabList> lists, IEnumerable<NetworkPrefabsList> ngoLists = null)
        {
            var issues = new List<NetPrefabIssue>();
            var registered = new HashSet<GameObject>();
            var listed = new List<GameObject>();

            foreach (NetPrefabList list in lists ?? new NetPrefabList[0])
            {
                if (list == null)
                {
                    continue;
                }

                for (int i = 0; i < list.Prefabs.Count; i++)
                {
                    GameObject prefab = list.Prefabs[i];
                    if (prefab == null)
                    {
                        issues.Add(new NetPrefabIssue(NetPrefabIssueKind.EmptyListEntry, NetPrefabIssueSeverity.Warning, list,
                            "Prefab list '" + list.name + "' has an empty entry (element " + i + "); remove it."));
                        continue;
                    }

                    registered.Add(prefab);
                    listed.Add(prefab);
                }
            }

            foreach (NetworkPrefabsList list in ngoLists ?? new NetworkPrefabsList[0])
            {
                if (list == null)
                {
                    continue;
                }

                foreach (NetworkPrefab entry in list.PrefabList)
                {
                    if (entry != null && entry.Prefab != null)
                    {
                        registered.Add(entry.Prefab);
                        listed.Add(entry.Prefab);
                    }
                }
            }

            var seen = new HashSet<GameObject>();
            int checkedCount = 0;
            foreach (GameObject prefab in Concat(prefabs, listed))
            {
                if (prefab == null || !seen.Add(prefab))
                {
                    continue;
                }

                bool isListed = registered.Contains(prefab);
                if (!isListed && !IsNetworked(prefab))
                {
                    continue;
                }

                checkedCount++;
                CheckPrefab(prefab, isListed, issues);
            }

            // Errors first, otherwise in the order found (OrderBy is stable).
            return new NetPrefabValidationReport(issues.OrderBy(i => i.Severity).ToList(), checkedCount);
        }

        /// <summary>
        /// The prefabs under <paramref name="searchFolders"/> (default: Assets) that have a NetworkObject on their root and
        /// that no prefab list there registers yet.
        /// </summary>
        public static List<GameObject> FindUnregisteredPrefabs(string[] searchFolders = null)
        {
            string[] folders = NetSceneSetup.ValidFolders(searchFolders);
            var registered = new HashSet<GameObject>();
            foreach (NetPrefabList list in LoadAssets<NetPrefabList>(folders))
            {
                foreach (GameObject prefab in list.Prefabs)
                {
                    if (prefab != null)
                    {
                        registered.Add(prefab);
                    }
                }
            }

            foreach (NetworkPrefabsList list in LoadAssets<NetworkPrefabsList>(folders))
            {
                foreach (NetworkPrefab entry in list.PrefabList)
                {
                    if (entry != null && entry.Prefab != null)
                    {
                        registered.Add(entry.Prefab);
                    }
                }
            }

            var found = new List<GameObject>();
            foreach (GameObject prefab in LoadPrefabs(folders))
            {
                if (prefab.GetComponent<NetworkObject>() != null && !registered.Contains(prefab))
                {
                    found.Add(prefab);
                }
            }

            return found;
        }

        /// <summary>True when <paramref name="prefab"/> carries a NetworkObject or a NetworkBehaviour anywhere in its hierarchy.</summary>
        public static bool IsNetworked(GameObject prefab)
        {
            return prefab != null
                && (prefab.GetComponentInChildren<NetworkObject>(true) != null || prefab.GetComponentInChildren<NetworkBehaviour>(true) != null);
        }

        /// <summary>
        /// The generic class in <paramref name="entityType"/>'s inheritance chain up to <see cref="NetEntity"/> (the type
        /// itself included, and a type nested in a generic one), or null when there is none.
        /// </summary>
        public static Type FindGenericAncestor(Type entityType)
        {
            for (Type type = entityType; type != null && type != typeof(NetEntity); type = type.BaseType)
            {
                if (type.IsGenericType)
                {
                    return type;
                }
            }

            return null;
        }

        private static void CheckPrefab(GameObject prefab, bool isRegistered, List<NetPrefabIssue> issues)
        {
            if (prefab.GetComponent<NetworkObject>() == null)
            {
                NetworkObject nested = prefab.GetComponentInChildren<NetworkObject>(true);
                string where = nested != null
                    ? "its NetworkObject is on the child '" + nested.name + "'; move it to the root"
                    : "add a NetworkObject to its root";
                string why = isRegistered ? "is in a prefab list" : "has networked components";
                issues.Add(new NetPrefabIssue(NetPrefabIssueKind.MissingNetworkObject, NetPrefabIssueSeverity.Error, prefab,
                    "Prefab '" + prefab.name + "' " + why + " but no NetworkObject on its root, so it cannot be spawned over the network: " + where + "."));
            }
            else if (!isRegistered)
            {
                issues.Add(new NetPrefabIssue(NetPrefabIssueKind.NotRegistered, NetPrefabIssueSeverity.Error, prefab,
                    "Prefab '" + prefab.name + "' has a NetworkObject but no prefab list registers it, so spawning it fails: add it to the NetPrefabList on your NetBootstrap."));
            }

            var reported = new HashSet<Type>();
            foreach (NetEntity entity in prefab.GetComponentsInChildren<NetEntity>(true))
            {
                Type type = entity.GetType();
                Type generic = FindGenericAncestor(type);
                if (generic == null || !reported.Add(type))
                {
                    continue;
                }

                string what = generic == type
                    ? "is generic"
                    : "derives from the generic class " + Pretty(generic);
                issues.Add(new NetPrefabIssue(NetPrefabIssueKind.GenericNetEntity, NetPrefabIssueSeverity.Error, prefab,
                    "Prefab '" + prefab.name + "': the NetEntity " + Pretty(type) + " " + what
                    + ", so its RPCs and synced fields have no stable network ids. Make every class between it and NetEntity non-generic."));
            }
        }

        private static string Pretty(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.Name;
            }

            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick >= 0)
            {
                name = name.Substring(0, tick);
            }

            var args = new List<string>();
            foreach (Type arg in type.GetGenericArguments())
            {
                args.Add(arg.IsGenericParameter ? arg.Name : Pretty(arg));
            }

            return name + "<" + string.Join(", ", args) + ">";
        }

        private static IEnumerable<GameObject> Concat(IEnumerable<GameObject> first, IEnumerable<GameObject> second)
        {
            if (first != null)
            {
                foreach (GameObject item in first)
                {
                    yield return item;
                }
            }

            foreach (GameObject item in second)
            {
                yield return item;
            }
        }

        private static List<GameObject> LoadPrefabs(string[] folders)
        {
            var prefabs = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            return prefabs;
        }

        private static List<T> LoadAssets<T>(string[] folders) where T : UnityEngine.Object
        {
            var assets = new List<T>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, folders))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }
    }
}
