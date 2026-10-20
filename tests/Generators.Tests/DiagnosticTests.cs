using System.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Generators.Tests
{
    /// <summary>Each analyzer diagnostic fires on the mistake it names, on the right line, and stops generation there.</summary>
    [TestFixture]
    public class DiagnosticTests
    {
        private const string Usings = "using MindAttic.Unity.Multiplayer;\nusing MindAttic.Unity.Multiplayer.Core;\nusing UnityEngine;\n";

        private static Diagnostic Single(RunResult result, string id)
        {
            var matches = result.GeneratorDiagnostics.Concat(result.CompileDiagnostics).Where(d => d.Id == id).ToList();
            Assert.That(matches, Has.Count.EqualTo(1), "expected one " + id + "; got:\n" + Harness.Format(result.GeneratorDiagnostics.Concat(result.CompileDiagnostics)));
            return matches[0];
        }

        private static int Line(Diagnostic diagnostic)
        {
            return diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        }

        [Test]
        public void GenericNetEntityIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Pool<T> : NetEntity
{
    [ToServer] public partial void Take(int n);
    void OnTake(int n) { }
}");
            Diagnostic d = Single(result, "MAMP005");
            Assert.That(d.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(d.GetMessage(), Does.Contain("Pool<T>"));
            Assert.That(result.Sources.Keys.Any(k => k.Contains("Pool")), Is.False);
        }

        [Test]
        public void EntityNestedInAGenericTypeIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Outer<T>
{
    public partial class Inner : NetEntity
    {
        [Synced] int _n;
    }
}");
            Single(result, "MAMP005");
        }

        [Test]
        public void AbstractGenericBaseWithoutNetworkMembersIsAllowed()
        {
            RunResult result = Harness.Run(Usings + @"
public abstract class Base<T> : NetEntity { }
public partial class Concrete : Base<int>
{
    [ToServer] public partial void Go();
    void OnGo() { }
}");
            Assert.That(Harness.Ids(result), Does.Not.Contain("MAMP005"));
            Assert.That(result.Errors, Is.Empty, Harness.Format(result.Errors));
        }

        [Test]
        public void ParameterTheNetworkCannotSendIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public class Payload { }
public partial class Avatar : NetEntity
{
    [ToServer]
    public partial void Send(int ok, Payload bad);
    void OnSend(int ok, Payload bad) { }
}");
            Diagnostic d = Single(result, "MAMP003");
            Assert.That(d.GetMessage(), Does.Contain("'bad'").And.Contain("Payload"));
            Assert.That(Line(d), Is.EqualTo(9));
        }

        [Test]
        public void NetSenderIsNotASendableParameter()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [ToClients] public partial void Echo(NetSender who);
    void OnEcho(NetSender who) { }
}");
            Single(result, "MAMP003");
        }

        [Test]
        public void SyncedFieldOfAnUnreplicableTypeIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [Synced] string _name;
}");
            Diagnostic d = Single(result, "MAMP004");
            Assert.That(d.GetMessage(), Does.Contain("_name").And.Contain("string"));
        }

        [TestCase("[ToServer] public partial void Move(Vector2 p);", "void OnMove(Vector2 p, NetSender sender)")]
        [TestCase("[ToClients] public partial void Boom(int size);", "void OnBoom(int size)")]
        [TestCase("[ToOwner] public partial void Hint(int code);", "void OnHint(int code)")]
        [TestCase("[ToClient] public partial void Tell(ulong to, int code);", "void OnTell(int code)")]
        public void MissingHandlerIsAnErrorThatNamesTheExpectedSignature(string declaration, string expected)
        {
            RunResult result = Harness.Run(Usings + "public partial class Avatar : NetEntity\n{\n    " + declaration + "\n}");

            Diagnostic d = Single(result, "MAMP002");
            Assert.That(d.GetMessage(), Does.Contain(expected));
            Assert.That(Line(d), Is.EqualTo(6));
        }

        [Test]
        public void HandlerWithTheWrongParameterTypesDoesNotCount()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [ToServer] public partial void Move(Vector2 p);
    void OnMove(Vector3 p, NetSender sender) { }
}");
            Single(result, "MAMP002");
        }

        [Test]
        public void HandlerInheritedFromABaseCounts()
        {
            RunResult result = Harness.Run(Usings + @"
public abstract class Base : NetEntity { protected void OnMove(Vector2 p) { } }
public partial class Avatar : Base
{
    [ToServer] public partial void Move(Vector2 p);
}");
            Assert.That(result.Errors, Is.Empty, Harness.Format(result.Errors));
        }

        [Test]
        public void DirectWriteToASyncedFieldIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [Synced] float _mass;
    [Synced] Vector2 _spot;
    void Hit()
    {
        _mass = 3f;
    }
    void Bump() { _mass += 1f; }
    void Nudge() { _spot.x = 1f; }
}");
            var writes = result.CompileDiagnostics.Where(d => d.Id == "MAMP006").OrderBy(Line).ToList();
            Assert.That(writes.Select(Line), Is.EqualTo(new[] { 11, 13, 14 }), Harness.Format(result.CompileDiagnostics));
            Assert.That(writes[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(writes[0].GetMessage(), Does.Contain("'_mass'").And.Contain("'Mass'"));
        }

        [Test]
        public void DirectReadOfASyncedFieldIsAWarningAndNameofIsFine()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [Synced] float _mass;
    float Peek() { return _mass; }
    string Name() { return nameof(_mass); }
}");
            Diagnostic d = Single(result, "MAMP007");
            Assert.That(d.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(Line(d), Is.EqualTo(8));
            Assert.That(Harness.Ids(result), Does.Not.Contain("MAMP006"));
        }

        [Test]
        public void NonPartialMethodIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [ToServer] public void Move(Vector2 p) { }
    void OnMove(Vector2 p) { }
}");
            Diagnostic d = Single(result, "MAMP001");
            Assert.That(d.GetMessage(), Does.Contain("partial method declaration without a body"));
        }

        [Test]
        public void NonPartialClassIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public class Avatar : NetEntity
{
    [Synced] int _score;
}");
            Diagnostic d = Single(result, "MAMP001");
            Assert.That(d.GetMessage(), Does.Contain("must be declared partial"));
            Assert.That(result.Sources, Is.Empty);
        }

        [Test]
        public void NonPartialContainingTypeIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public class Outer
{
    public partial class Avatar : NetEntity
    {
        [Synced] int _score;
    }
}");
            Diagnostic d = Single(result, "MAMP001");
            Assert.That(d.GetMessage(), Does.StartWith("'Outer'"));
        }

        [Test]
        public void HandWrittenImplementationIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [ToServer] public partial void Move(Vector2 p);
    public partial void Move(Vector2 p) { }
    void OnMove(Vector2 p) { }
}");
            Diagnostic d = Single(result, "MAMP001");
            Assert.That(d.GetMessage(), Does.Contain("already has an implementing declaration"));
        }

        [Test]
        public void AttributeOutsideANetEntityIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Menu : MonoBehaviour
{
    [ToServer] public partial void Click();
    void OnClick() { }
}");
            Diagnostic d = Single(result, "MAMP008");
            Assert.That(d.GetMessage(), Does.Contain("[ToServer]").And.Contain("Menu"));
        }

        [TestCase("[ToServer] public static partial void Go();", "must not be static")]
        [TestCase("[ToServer] public partial void Go<T>();", "must not be generic")]
        [TestCase("[ToServer] public partial void Go(ref int n);", "ref, out, in or params")]
        [TestCase("[ToServer, ToClients] public partial void Go();", "more than one")]
        public void RpcShapesThatCannotWorkAreErrors(string declaration, string reason)
        {
            RunResult result = Harness.Run(Usings + "public partial class Avatar : NetEntity\n{\n    " + declaration + "\n}");

            Diagnostic d = Single(result, "MAMP009");
            Assert.That(d.GetMessage(), Does.Contain(reason));
        }

        [TestCase("[ToClient] public partial void Tell(int code);")]
        [TestCase("[ToClient] public partial void Tell();")]
        public void ToClientWithoutAUlongTargetIsAnError(string declaration)
        {
            RunResult result = Harness.Run(Usings + "public partial class Avatar : NetEntity\n{\n    " + declaration + "\n    void OnTell(int code) { }\n    void OnTell() { }\n}");

            Single(result, "MAMP010");
        }

        [TestCase("[Synced(OnChanged = \"Missing\")] float _mass;")]
        [TestCase("[Synced(OnChanged = nameof(Wrong))] float _mass; void Wrong(int a, int b) { }")]
        public void OnChangedThatNamesNoMatchingHandlerIsAnError(string declaration)
        {
            RunResult result = Harness.Run(Usings + "public partial class Avatar : NetEntity\n{\n    " + declaration + "\n}");

            Diagnostic d = Single(result, "MAMP011");
            Assert.That(d.GetMessage(), Does.Contain("void").And.Contain("(float previous, float current)"));
        }

        [TestCase("[Synced] static int _count;", "non-static")]
        [TestCase("[Synced] readonly int _count;", "non-readonly")]
        [TestCase("[Synced] int _count; public int Count { get { return 0; } }", "already taken")]
        [TestCase("[Synced] int Count;", "already taken")]
        public void SyncedFieldsThatCannotBeGeneratedAreErrors(string declaration, string reason)
        {
            RunResult result = Harness.Run(Usings + "public partial class Avatar : NetEntity\n{\n    " + declaration + "\n}");

            Diagnostic d = Single(result, "MAMP012");
            Assert.That(d.GetMessage(), Does.Contain(reason));
        }

        [Test]
        public void NonConstantSyncedInitializerIsAnError()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    static float Start() { return 1f; }
    [Synced] float _mass = Start();
}");
            Diagnostic d = Single(result, "MAMP013");
            Assert.That(d.GetMessage(), Does.Contain("'Mass'"));
        }

        [Test]
        public void UnusedFieldWarningsOnSyncedFieldsAreSuppressedButNotOnOtherFields()
        {
            RunResult result = Harness.Run(Usings + @"
public partial class Avatar : NetEntity
{
    [Synced] private float _mass;
    private int _unused;
}");
            var warnings = result.CompileDiagnostics.Where(d => d.Id == "CS0169").ToList();
            Assert.That(warnings, Has.Count.EqualTo(1), Harness.Format(result.CompileDiagnostics));
            Assert.That(warnings[0].GetMessage(), Does.Contain("_unused"));
        }
    }
}
