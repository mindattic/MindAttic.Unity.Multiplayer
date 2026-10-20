// UM-T4 spike: a toy IIncrementalGenerator. Throwaway: UM-T11 writes the real one.
//
// For every partial class it finds:
//   [ToServer] <mods> partial void Name(args);   ->  the partial body, which calls NameRpc(args), and
//                                                    [Rpc(SendTo.Server)] void NameRpc(args, RpcParams),
//                                                    which calls OnName(args, senderClientId).
//   [Synced] T _name;                            ->  NetworkVariable<T> plus a Name property.
//
// Output is C# 9 only (UM-LAW-3): block-scoped namespaces, no records, no raw strings.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MindAttic.Spike.Generators
{
    [Generator(LanguageNames.CSharp)]
    public sealed class SpikeRpcGenerator : IIncrementalGenerator
    {
        const string ToServerAttribute = "MindAttic.Spike.ToServerAttribute";
        const string SyncedAttribute = "MindAttic.Spike.SyncedAttribute";

        static readonly DiagnosticDescriptor MissingHandler = new DiagnosticDescriptor(
            "MASPIKE001",
            "Missing On<Name> handler",
            "[ToServer] method '{0}' needs a handler 'void On{0}({1}, ulong sender)'",
            "MindAttic.Spike",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<INamedTypeSymbol> classes = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax c
                        && c.Modifiers.Any(SyntaxKind.PartialKeyword)
                        && c.Members.Any(m => m.AttributeLists.Count > 0),
                    static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, ct) as INamedTypeSymbol)
                .Where(static s => s != null);

            IncrementalValueProvider<(Compilation, System.Collections.Immutable.ImmutableArray<INamedTypeSymbol>)> all =
                context.CompilationProvider.Combine(classes.Collect());

            context.RegisterSourceOutput(all, static (spc, pair) =>
            {
                var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (INamedTypeSymbol type in pair.Item2)
                {
                    if (!seen.Add(type)) continue;   // a partial class split over files shows up once per part
                    string source = Emit(spc, type);
                    if (source != null)
                        spc.AddSource(HintName(type), SourceText.From(source, Encoding.UTF8));
                }
            });
        }

        static string HintName(INamedTypeSymbol type)
        {
            string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "").Replace('<', '_').Replace('>', '_').Replace(',', '_');
            return name + ".Spike.g.cs";
        }

        static bool Has(ISymbol symbol, string attributeName) =>
            symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeName);

        static string Emit(SourceProductionContext spc, INamedTypeSymbol type)
        {
            List<IMethodSymbol> rpcs = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.IsPartialDefinition && m.PartialImplementationPart == null && m.ReturnsVoid && Has(m, ToServerAttribute))
                .ToList();
            List<IFieldSymbol> synced = type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => Has(f, SyncedAttribute))
                .ToList();
            if (rpcs.Count == 0 && synced.Count == 0) return null;

            var body = new StringBuilder();

            foreach (IMethodSymbol m in rpcs)
            {
                string paramList = string.Join(", ", m.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " " + p.Name));
                string argList = string.Join(", ", m.Parameters.Select(p => p.Name));
                string rpcParams = paramList.Length == 0 ? "global::Unity.Netcode.RpcParams rpcParams = default" : paramList + ", global::Unity.Netcode.RpcParams rpcParams = default";
                string handlerArgs = argList.Length == 0 ? "rpcParams.Receive.SenderClientId" : argList + ", rpcParams.Receive.SenderClientId";

                bool hasHandler = type.GetMembers("On" + m.Name).OfType<IMethodSymbol>()
                    .Any(h => h.Parameters.Length == m.Parameters.Length + 1);
                if (!hasHandler)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(MissingHandler, m.Locations.FirstOrDefault(), m.Name, paramList));
                    continue;
                }

                // The implementing declaration has to repeat the definition's modifiers exactly.
                var decl = m.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>().First();
                string modifiers = string.Join(" ", decl.Modifiers.Select(t => t.Text));

                body.AppendLine("        " + modifiers + " void " + m.Name + "(" + paramList + ")");
                body.AppendLine("        {");
                body.AppendLine("            " + m.Name + "Rpc(" + argList + ");");
                body.AppendLine("        }");
                body.AppendLine();
                body.AppendLine("        [global::Unity.Netcode.Rpc(global::Unity.Netcode.SendTo.Server)]");
                body.AppendLine("        private void " + m.Name + "Rpc(" + rpcParams + ")");
                body.AppendLine("        {");
                body.AppendLine("            On" + m.Name + "(" + handlerArgs + ");");
                body.AppendLine("        }");
                body.AppendLine();
            }

            foreach (IFieldSymbol f in synced)
            {
                string t = f.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string prop = PropertyName(f.Name);
                string nv = "__" + prop + "NetVar";
                body.AppendLine("        private global::Unity.Netcode.NetworkVariable<" + t + "> " + nv + " = new global::Unity.Netcode.NetworkVariable<" + t + ">();");
                body.AppendLine("        public global::Unity.Netcode.NetworkVariable<" + t + "> " + prop + "Variable { get { return " + nv + "; } }");
                body.AppendLine("        public " + t + " " + prop + " { get { return " + nv + ".Value; } set { " + nv + ".Value = value; } }");
                body.AppendLine();
            }

            // Wrap in containing types, then the namespace (block-scoped: C# 9).
            string text = body.ToString().TrimEnd() + "\n";
            for (INamedTypeSymbol t = type; t != null; t = t.ContainingType)
            {
                string kw = t.TypeKind == TypeKind.Struct ? "struct" : "class";
                string header = "    partial " + kw + " " + t.Name + TypeParams(t);
                text = header + "\n    {\n" + text + "    }\n";
            }
            var sb = new StringBuilder();
            sb.Append("// <auto-generated>MindAttic UM-T4 spike generator</auto-generated>\n");
            sb.Append("#pragma warning disable\n");
            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                sb.Append("namespace " + type.ContainingNamespace.ToDisplayString() + "\n{\n");
                sb.Append(text);
                sb.Append("}\n");
            }
            else
            {
                sb.Append(text);
            }
            return sb.ToString();
        }

        static string TypeParams(INamedTypeSymbol t) =>
            t.TypeParameters.Length == 0 ? "" : "<" + string.Join(", ", t.TypeParameters.Select(p => p.Name)) + ">";

        static string PropertyName(string field)
        {
            string n = field.StartsWith("m_") ? field.Substring(2) : field.TrimStart('_');
            if (n.Length == 0) n = field;
            return char.ToUpperInvariant(n[0]) + n.Substring(1);
        }
    }
}
