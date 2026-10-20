using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MindAttic.Unity.Multiplayer.Generators
{
    /// <summary>
    /// Writes the network plumbing for every <c>NetEntity</c> subclass (RFC 0001, "Attribute API"):
    /// <list type="bullet">
    /// <item><c>[ToServer] &lt;mods&gt; partial void Move(args);</c> gets a body that calls <c>MoveServerRpc(args)</c>, and
    /// <c>[Rpc(SendTo.Server, Delivery = ...)] void MoveServerRpc(args, RpcParams)</c>, which checks ownership and rate through
    /// <c>NetEntity.NetReceiveToServer</c> and then calls <c>OnMove(args)</c> or <c>OnMove(args, NetSender)</c>.</item>
    /// <item><c>[ToClients]</c> / <c>[ToOwner]</c>: the body sends only from the server (<c>NetCanSendToClients</c>);
    /// <c>NameClientsRpc</c> / <c>NameOwnerRpc</c> (SendTo.ClientsAndHost / SendTo.Owner, server-invoked only) check the
    /// sender (<c>NetReceiveFromServer</c>) and call <c>OnName(args)</c>.</item>
    /// <item><c>[ToClient]</c>: the first parameter is the target client id; <c>NameClientRpc(rest, RpcTarget.Single(id))</c>
    /// with SendTo.SpecifiedInParams calls <c>OnName(rest)</c> on that client.</item>
    /// <item><c>[Synced(OnChanged = ...)] T _name;</c> becomes a server-write <c>NetworkVariable&lt;T&gt; __NameNetVar</c> and a
    /// <c>Name</c> property whose setter goes through <c>NetWriteSynced</c>; OnChanged handlers are wired in
    /// <c>NetBindSynced</c> / <c>NetUnbindSynced</c>.</item>
    /// <item>One <c>[assembly: NetProtocol(hash, count)]</c> per assembly over every RPC and synced signature.</item>
    /// </list>
    /// The RPC key passed to the gate is the declaring type's full name plus the method name. Output is C# 9 (UM-LAW-3).
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class NetEntityGenerator : IIncrementalGenerator
    {
        /// <summary>Name of the per-assembly protocol file.</summary>
        public const string ProtocolHintName = "MindAttic.Multiplayer.Protocol.g.cs";

        private const string Rpc = "global::Unity.Netcode.Rpc";
        private const string SendTo = "global::Unity.Netcode.SendTo.";
        private const string RpcDelivery = "global::Unity.Netcode.RpcDelivery.";
        private const string RpcParams = "global::Unity.Netcode.RpcParams";
        private const string ServerOnly = "InvokePermission = global::Unity.Netcode.RpcInvokePermission.Server";
        private const string NetSender = "global::MindAttic.Unity.Multiplayer.Core.NetSender";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<INamedTypeSymbol> types = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is TypeDeclarationSyntax t
                        && !(node is InterfaceDeclarationSyntax)
                        && (t.BaseList != null || t.Members.Any(m => m.AttributeLists.Count > 0)),
                    static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)ctx.Node, ct) as INamedTypeSymbol)
                .Where(static t => t != null);

            context.RegisterSourceOutput(context.CompilationProvider.Combine(types.Collect()), static (spc, input) => Execute(spc, input.Left, input.Right));
        }

        private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol> found)
        {
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var types = new List<INamedTypeSymbol>();
            foreach (INamedTypeSymbol type in found)
            {
                if (seen.Add(type))
                {
                    types.Add(type); // a partial type split over files shows up once per part
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));

            var signatures = new List<string>();
            foreach (INamedTypeSymbol type in types)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                EntityModel model = EntityModel.Build(type, compilation, context.ReportDiagnostic);
                if (model == null)
                {
                    continue;
                }

                signatures.AddRange(model.Signatures());
                if (model.Rpcs.Count > 0 || model.Synced.Count > 0)
                {
                    context.AddSource(HintName(type), SourceText.From(EmitEntity(model), Encoding.UTF8));
                }
            }

            if (signatures.Count > 0 && compilation.GetTypeByMetadataName(Names.NetProtocol) != null)
            {
                context.AddSource(ProtocolHintName, SourceText.From(EmitProtocol(signatures), Encoding.UTF8));
            }
        }

        private static string HintName(INamedTypeSymbol type)
        {
            return type.ToDisplayString(Symbols.Signature).Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_') + ".Net.g.cs";
        }

        // -------------------------------------------------------------------------------------------------------------
        // Emission
        // -------------------------------------------------------------------------------------------------------------

        private static string EmitProtocol(List<string> signatures)
        {
            IReadOnlyList<string> normalized = ProtocolHasher.Normalize(signatures);
            ulong hash = ProtocolHasher.Compute(normalized);
            var text = new StringBuilder();
            text.Append("// <auto-generated>\n");
            text.Append("// MindAttic.Unity.Multiplayer generator: this assembly's network protocol hash (ProtocolHash.Compute) over\n");
            text.Append("// these signatures. Connection approval refuses a peer whose builds hash differently.\n");
            foreach (string signature in normalized)
            {
                text.Append("//   ").Append(signature).Append('\n');
            }

            text.Append("// </auto-generated>\n");
            text.Append("[assembly: global::MindAttic.Unity.Multiplayer.Core.NetProtocol(\"")
                .Append(hash.ToString("x16", CultureInfo.InvariantCulture))
                .Append("\", ")
                .Append(normalized.Count.ToString(CultureInfo.InvariantCulture))
                .Append(")]\n");
            return text.ToString();
        }

        private static string EmitEntity(EntityModel model)
        {
            var w = new CodeWriter();
            foreach (RpcModel rpc in model.Rpcs)
            {
                EmitRpc(w, model, rpc);
            }

            foreach (SyncedModel synced in model.Synced)
            {
                EmitSynced(w, synced);
            }

            List<SyncedModel> watched = model.Synced.Where(s => s.OnChanged != null).ToList();
            if (watched.Count > 0)
            {
                w.Line("protected override void NetBindSynced()");
                w.Open();
                w.Line("base.NetBindSynced();");
                foreach (SyncedModel s in watched)
                {
                    w.Line(s.VariableName + ".OnValueChanged += " + Symbols.Identifier(s.OnChanged) + ";");
                }

                w.Close();
                w.Blank();
                w.Line("protected override void NetUnbindSynced()");
                w.Open();
                foreach (SyncedModel s in watched)
                {
                    w.Line(s.VariableName + ".OnValueChanged -= " + Symbols.Identifier(s.OnChanged) + ";");
                }

                w.Line("base.NetUnbindSynced();");
                w.Close();
                w.Blank();
            }

            return Wrap(model.Type, w.ToString().TrimEnd('\n') + "\n");
        }

        private static void EmitRpc(CodeWriter w, EntityModel model, RpcModel rpc)
        {
            string key = Literal(model.Type.ToDisplayString() + "." + rpc.Name);
            string delivery = RpcDelivery + (rpc.Unreliable ? "Unreliable" : "Reliable");
            IReadOnlyList<ParameterModel> wire = rpc.WireParameters;
            string wireParams = string.Join(", ", wire.Select(p => p.Type + " " + p.Name));
            string wireArgs = string.Join(", ", wire.Select(p => p.Name));
            string allParams = string.Join(", ", rpc.Parameters.Select(p => p.Type + " " + p.Name));
            string stub = rpc.Name + StubSuffix(rpc.Direction);

            w.Line(rpc.Modifiers + " void " + Symbols.Identifier(rpc.Name) + "(" + allParams + ")");
            w.Open();
            if (rpc.Direction != Direction.ToServer)
            {
                w.Line("if (!NetCanSendToClients(" + key + "))");
                w.Open();
                w.Line("return;");
                w.Close();
                w.Blank();
            }

            if (rpc.Direction == Direction.ToClient)
            {
                string target = "((global::Unity.Netcode.NetworkBehaviour)this).RpcTarget.Single(" + rpc.Parameters[0].Name + ", global::Unity.Netcode.RpcTargetUse.Temp)";
                w.Line(stub + "(" + Join(wireArgs, target) + ");");
            }
            else
            {
                w.Line(stub + "(" + wireArgs + ");");
            }

            w.Close();
            w.Blank();

            string handlerArgs;
            switch (rpc.Direction)
            {
                case Direction.ToServer:
                    w.Line("[" + Rpc + "(" + SendTo + "Server, Delivery = " + delivery + ")]");
                    w.Line("private void " + stub + "(" + Join(wireParams, RpcParams + " __rpcParams = default") + ")");
                    w.Open();
                    w.Line(NetSender + " __sender;");
                    w.Line("if (!NetReceiveToServer(" + key + ", __rpcParams.Receive.SenderClientId, "
                        + (rpc.RequireOwnership ? "true" : "false") + ", "
                        + rpc.RateLimit.ToString(CultureInfo.InvariantCulture) + ", "
                        + rpc.Burst.ToString(CultureInfo.InvariantCulture) + ", out __sender))");
                    handlerArgs = rpc.PassSender ? Join(wireArgs, "__sender") : wireArgs;
                    break;
                case Direction.ToClient:
                    w.Line("[" + Rpc + "(" + SendTo + "SpecifiedInParams, Delivery = " + delivery + ", " + ServerOnly + ")]");
                    w.Line("private void " + stub + "(" + Join(wireParams, RpcParams + " __rpcParams") + ")");
                    w.Open();
                    w.Line("if (!NetReceiveFromServer(" + key + ", __rpcParams.Receive.SenderClientId))");
                    handlerArgs = wireArgs;
                    break;
                default:
                    string target = rpc.Direction == Direction.ToOwner ? "Owner" : "ClientsAndHost";
                    w.Line("[" + Rpc + "(" + SendTo + target + ", Delivery = " + delivery + ", " + ServerOnly + ")]");
                    w.Line("private void " + stub + "(" + Join(wireParams, RpcParams + " __rpcParams = default") + ")");
                    w.Open();
                    w.Line("if (!NetReceiveFromServer(" + key + ", __rpcParams.Receive.SenderClientId))");
                    handlerArgs = wireArgs;
                    break;
            }

            w.Open();
            w.Line("return;");
            w.Close();
            w.Blank();
            w.Line(Symbols.Identifier(rpc.Handler) + "(" + handlerArgs + ");");
            w.Close();
            w.Blank();
        }

        private static void EmitSynced(CodeWriter w, SyncedModel s)
        {
            string variableType = "global::Unity.Netcode.NetworkVariable<" + s.Type + ">";
            w.Line("private " + variableType + " " + s.VariableName + " = new " + variableType + "(" + s.Initial
                + ", global::Unity.Netcode.NetworkVariableReadPermission.Everyone, global::Unity.Netcode.NetworkVariableWritePermission.Server);");
            w.Blank();
            w.Line("public " + s.Type + " " + s.Property);
            w.Open();
            w.Line("get { return " + s.VariableName + ".Value; }");
            w.Line("set { NetWriteSynced(" + s.VariableName + ", value, " + Literal(s.Property) + "); }");
            w.Close();
            w.Blank();
        }

        private static string StubSuffix(Direction direction)
        {
            switch (direction)
            {
                case Direction.ToServer: return "ServerRpc";
                case Direction.ToClients: return "ClientsRpc";
                case Direction.ToOwner: return "OwnerRpc";
                default: return "ClientRpc";
            }
        }

        private static string Join(string first, string second)
        {
            return first.Length == 0 ? second : first + ", " + second;
        }

        private static string Literal(string text)
        {
            return SymbolDisplay.FormatLiteral(text, quote: true);
        }

        /// <summary>Wraps members in their containing types and a block-scoped namespace (C# 9: no file-scoped namespaces).</summary>
        private static string Wrap(INamedTypeSymbol type, string members)
        {
            var chain = new List<INamedTypeSymbol>();
            for (INamedTypeSymbol t = type; t != null; t = t.ContainingType)
            {
                chain.Insert(0, t);
            }

            bool hasNamespace = !type.ContainingNamespace.IsGlobalNamespace;
            int depth = hasNamespace ? 1 : 0;
            var text = new StringBuilder();
            text.Append("// <auto-generated>\n");
            text.Append("// MindAttic.Unity.Multiplayer generator: network plumbing for ").Append(type.ToDisplayString()).Append(".\n");
            text.Append("// Edit the [ToServer]/[ToClients]/[ToOwner]/[ToClient] methods and [Synced] fields it comes from, not this file.\n");
            text.Append("// </auto-generated>\n");
            text.Append("#pragma warning disable\n");
            if (hasNamespace)
            {
                text.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).Append("\n{\n");
            }

            for (int i = 0; i < chain.Count; i++)
            {
                string indent = new string(' ', 4 * (depth + i));
                string keyword = chain[i].TypeKind == TypeKind.Struct ? "struct" : chain[i].IsRecord ? "record" : "class";
                text.Append(indent).Append("partial ").Append(keyword).Append(' ').Append(Symbols.Identifier(chain[i].Name)).Append('\n');
                text.Append(indent).Append("{\n");
            }

            string memberIndent = new string(' ', 4 * (depth + chain.Count));
            foreach (string line in members.Split('\n'))
            {
                if (line.Length > 0)
                {
                    text.Append(memberIndent).Append(line);
                }

                text.Append('\n');
            }

            text.Length -= 1; // the split leaves one empty trailing line
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                text.Append(new string(' ', 4 * (depth + i))).Append("}\n");
            }

            if (hasNamespace)
            {
                text.Append("}\n");
            }

            return text.ToString();
        }

        private sealed class CodeWriter
        {
            private readonly StringBuilder _text = new StringBuilder();
            private int _depth;

            public void Line(string line)
            {
                _text.Append(' ', 4 * _depth).Append(line).Append('\n');
            }

            public void Blank()
            {
                _text.Append('\n');
            }

            public void Open()
            {
                Line("{");
                _depth++;
            }

            public void Close()
            {
                _depth--;
                Line("}");
            }

            public override string ToString()
            {
                return _text.ToString();
            }
        }
    }
}
