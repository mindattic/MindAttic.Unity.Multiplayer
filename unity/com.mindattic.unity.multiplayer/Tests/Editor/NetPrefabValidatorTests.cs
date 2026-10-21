using System.Collections.Generic;
using System.Linq;
using Unity.Multiplayer.Tests;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer.Editor.Tests
{
    public class NetPrefabValidatorTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
            TempAssets.Delete();
        }

        private GameObject Make(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            _created.Add(go);
            return go;
        }

        private NetPrefabList List(params GameObject[] prefabs)
        {
            var list = ScriptableObject.CreateInstance<NetPrefabList>();
            list.name = "TestList";
            foreach (GameObject prefab in prefabs)
            {
                list.Add(prefab);
            }

            _created.Add(list);
            return list;
        }

        private static NetPrefabValidationReport Validate(GameObject[] prefabs, params NetPrefabList[] lists)
        {
            return NetPrefabValidator.Validate(prefabs, lists);
        }

        [Test]
        public void RegisteredPrefabWithRootNetworkObject_IsClean()
        {
            GameObject avatar = Make("Avatar", typeof(NetworkObject), typeof(PlainProbeEntity));

            NetPrefabValidationReport report = Validate(new[] { avatar }, List(avatar));

            Assert.That(report.Issues, Is.Empty, report.ToString());
            Assert.That(report.PrefabsChecked, Is.EqualTo(1));
            Assert.That(report.HasErrors, Is.False);
        }

        [Test]
        public void NetEntityWithoutNetworkObject_IsMissingNetworkObject()
        {
            GameObject broken = Make("Broken", typeof(PlainProbeEntity));

            Assert.That(broken.GetComponent<PlainProbeEntity>(), Is.Not.Null);

            NetPrefabValidationReport report = Validate(new[] { broken }, List());

            NetPrefabIssue issue = report.Issues.Single();
            Assert.That(issue.Kind, Is.EqualTo(NetPrefabIssueKind.MissingNetworkObject));
            Assert.That(issue.Severity, Is.EqualTo(NetPrefabIssueSeverity.Error));
            Assert.That(issue.Context, Is.SameAs(broken));
            Assert.That(issue.Message, Does.Contain("'Broken' has networked components").And.Contain("add a NetworkObject to its root"));
        }

        [Test]
        public void ListedPlainPrefab_IsMissingNetworkObject()
        {
            GameObject plain = Make("Plain");

            NetPrefabValidationReport report = Validate(new GameObject[0], List(plain));

            NetPrefabIssue issue = report.Issues.Single();
            Assert.That(issue.Kind, Is.EqualTo(NetPrefabIssueKind.MissingNetworkObject));
            Assert.That(issue.Message, Does.Contain("is in a prefab list"));
        }

        [Test]
        public void NetworkObjectOnAChildOnly_IsMissingNetworkObjectOnTheRoot()
        {
            GameObject root = Make("Root");
            var child = new GameObject("Body", typeof(NetworkObject), typeof(PlainProbeEntity));
            child.transform.SetParent(root.transform);

            NetPrefabValidationReport report = Validate(new[] { root }, List(root));

            NetPrefabIssue issue = report.Issues.Single();
            Assert.That(issue.Kind, Is.EqualTo(NetPrefabIssueKind.MissingNetworkObject));
            Assert.That(issue.Message, Does.Contain("child 'Body'").And.Contain("move it to the root"));
        }

        [Test]
        public void UnregisteredNetworkPrefab_IsNotRegistered()
        {
            GameObject loose = Make("Loose", typeof(NetworkObject), typeof(PlainProbeEntity));

            NetPrefabValidationReport report = Validate(new[] { loose }, List());

            NetPrefabIssue issue = report.Issues.Single();
            Assert.That(issue.Kind, Is.EqualTo(NetPrefabIssueKind.NotRegistered));
            Assert.That(issue.Severity, Is.EqualTo(NetPrefabIssueSeverity.Error));
            Assert.That(issue.Message, Does.Contain("'Loose'").And.Contain("NetPrefabList"));
        }

        [Test]
        public void NgoNetworkPrefabsList_CountsAsRegistered()
        {
            GameObject avatar = Make("Avatar", typeof(NetworkObject));
            var ngoList = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            _created.Add(ngoList);
            ngoList.Add(new NetworkPrefab { Prefab = avatar });

            NetPrefabValidationReport report = NetPrefabValidator.Validate(new[] { avatar }, null, new[] { ngoList });

            Assert.That(report.Issues, Is.Empty, report.ToString());
        }

        [Test]
        public void EntityWithAGenericBase_IsAGenericNetEntity()
        {
            GameObject generic = Make("Generic", typeof(NetworkObject), typeof(ClosedGenericProbeEntity));

            NetPrefabValidationReport report = Validate(new[] { generic }, List(generic));

            NetPrefabIssue issue = report.Issues.Single();
            Assert.That(issue.Kind, Is.EqualTo(NetPrefabIssueKind.GenericNetEntity));
            Assert.That(issue.Severity, Is.EqualTo(NetPrefabIssueSeverity.Error));
            Assert.That(issue.Message, Does.Contain("ClosedGenericProbeEntity").And.Contain("GenericProbeEntityBase<Int32>"));
        }

        [Test]
        public void GenericEntityOnAChild_IsReportedOncePerType()
        {
            GameObject root = Make("Squad", typeof(NetworkObject));
            for (int i = 0; i < 2; i++)
            {
                new GameObject("Member" + i, typeof(ClosedGenericProbeEntity)).transform.SetParent(root.transform);
            }

            NetPrefabValidationReport report = Validate(new[] { root }, List(root));

            Assert.That(report.Issues.Count(i => i.Kind == NetPrefabIssueKind.GenericNetEntity), Is.EqualTo(1));
        }

        [Test]
        public void FindGenericAncestor_WalksTheChainUpToNetEntity()
        {
            Assert.That(NetPrefabValidator.FindGenericAncestor(typeof(PlainProbeEntity)), Is.Null);
            Assert.That(NetPrefabValidator.FindGenericAncestor(typeof(NetEntity)), Is.Null);
            Assert.That(NetPrefabValidator.FindGenericAncestor(typeof(ClosedGenericProbeEntity)), Is.EqualTo(typeof(GenericProbeEntityBase<int>)));
            Assert.That(NetPrefabValidator.FindGenericAncestor(typeof(GenericProbeEntityBase<string>)), Is.EqualTo(typeof(GenericProbeEntityBase<string>)));
        }

        [Test]
        public void PrefabWithNothingNetworked_IsIgnored()
        {
            GameObject scenery = Make("Scenery", typeof(BoxCollider));

            NetPrefabValidationReport report = Validate(new[] { scenery }, List());

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.PrefabsChecked, Is.EqualTo(0));
        }

        [Test]
        public void EmptyListEntry_IsAWarningAndErrorsComeFirst()
        {
            GameObject loose = Make("Loose", typeof(NetworkObject));
            NetPrefabList list = List();
            var serialized = new UnityEditor.SerializedObject(list);
            UnityEditor.SerializedProperty prefabs = serialized.FindProperty("prefabs");
            prefabs.arraySize = 1;
            prefabs.GetArrayElementAtIndex(0).objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            NetPrefabValidationReport report = Validate(new[] { loose }, list);

            Assert.That(report.Issues.Select(i => i.Kind), Is.EqualTo(new[] { NetPrefabIssueKind.NotRegistered, NetPrefabIssueKind.EmptyListEntry }));
            Assert.That(report.Issues[1].Severity, Is.EqualTo(NetPrefabIssueSeverity.Warning));
            Assert.That(report.Issues[1].Context, Is.SameAs(list));
            Assert.That(report.ErrorCount, Is.EqualTo(1));
            Assert.That(report.WarningCount, Is.EqualTo(1));
        }

        [Test]
        public void ValidateProject_ChecksPrefabAssetsAgainstTheListAssets()
        {
            TempAssets.Create();
            var registered = new GameObject("RegisteredAvatar", typeof(NetworkObject), typeof(PlainProbeEntity));
            GameObject registeredPrefab = TempAssets.SavePrefab(registered);
            var forgotten = new GameObject("ForgottenAvatar", typeof(NetworkObject), typeof(PlainProbeEntity));
            GameObject forgottenPrefab = TempAssets.SavePrefab(forgotten);
            TempAssets.SavePrefab(new GameObject("Scenery", typeof(BoxCollider)));
            TempAssets.SaveList("Prefabs", registeredPrefab);

            NetPrefabValidationReport report = NetPrefabValidator.ValidateProject(TempAssets.Folders);

            NetPrefabIssue issue = report.Issues.Single();
            Assert.That(issue.Kind, Is.EqualTo(NetPrefabIssueKind.NotRegistered));
            Assert.That(issue.Context, Is.SameAs(forgottenPrefab));
            Assert.That(report.PrefabsChecked, Is.EqualTo(2));
            Assert.That(NetPrefabValidator.FindUnregisteredPrefabs(TempAssets.Folders), Is.EqualTo(new[] { forgottenPrefab }));
        }
    }
}
