using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MindAttic.Unity.Multiplayer.Generators
{
    /// <summary>
    /// A <c>[Synced]</c> field is a declaration the generator reads, never used directly, so the compiler's "never used",
    /// "assigned but never used" and "never assigned" warnings on it are noise. This suppresses them for those fields only.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SyncedFieldSuppressor : DiagnosticSuppressor
    {
        private static readonly SuppressionDescriptor NeverUsed = Describe("CS0169");
        private static readonly SuppressionDescriptor AssignedNeverUsed = Describe("CS0414");
        private static readonly SuppressionDescriptor NeverAssigned = Describe("CS0649");

        /// <inheritdoc />
        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions
        {
            get { return ImmutableArray.Create(NeverUsed, AssignedNeverUsed, NeverAssigned); }
        }

        /// <inheritdoc />
        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            foreach (Diagnostic diagnostic in context.ReportedDiagnostics)
            {
                SuppressionDescriptor descriptor = SupportedSuppressions.FirstOrDefault(s => s.SuppressedDiagnosticId == diagnostic.Id);
                SyntaxTree tree = diagnostic.Location.SourceTree;
                if (descriptor == null || tree == null)
                {
                    continue;
                }

                SyntaxNode node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
                VariableDeclaratorSyntax declarator = node as VariableDeclaratorSyntax ?? node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
                if (declarator == null)
                {
                    continue;
                }

                var field = context.GetSemanticModel(tree).GetDeclaredSymbol(declarator, context.CancellationToken) as IFieldSymbol;
                if (field != null && Symbols.Find(field, Names.Synced) != null)
                {
                    context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
                }
            }
        }

        private static SuppressionDescriptor Describe(string id)
        {
            return new SuppressionDescriptor("MAMPS" + id.Substring(2), id, "[Synced] fields are declarations the generator reads; the value lives in the generated property.");
        }
    }
}
