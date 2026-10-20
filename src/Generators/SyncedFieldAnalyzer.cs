using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MindAttic.Unity.Multiplayer.Generators
{
    /// <summary>
    /// A <c>[Synced]</c> field is only a declaration: the value lives in the generated network variable behind the
    /// generated property. Writing the field (MAMP006, an error) would change nothing on the network, and reading it
    /// (MAMP007, a warning) always gives the default. <c>nameof(field)</c> is fine.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SyncedFieldAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(Diagnostics.SyncedFieldWrite, Diagnostics.SyncedFieldRead); }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(start =>
            {
                INamedTypeSymbol synced = start.Compilation.GetTypeByMetadataName(Names.Synced);
                if (synced == null)
                {
                    return;
                }

                start.RegisterOperationAction(ctx => Analyze(ctx, synced), OperationKind.FieldReference);
            });
        }

        private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol synced)
        {
            var reference = (IFieldReferenceOperation)context.Operation;
            IFieldSymbol field = reference.Field;
            if (!field.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, synced)))
            {
                return;
            }

            for (IOperation parent = reference.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is INameOfOperation)
                {
                    return;
                }
            }

            string property = Symbols.PropertyName(field.Name);
            DiagnosticDescriptor descriptor = IsWrite(reference) ? Diagnostics.SyncedFieldWrite : Diagnostics.SyncedFieldRead;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, reference.Syntax.GetLocation(), field.Name, property));
        }

        /// <summary>True when the reference (or a member of it, for a struct) is assigned, incremented or passed by ref/out.</summary>
        private static bool IsWrite(IOperation reference)
        {
            IOperation current = reference;
            while (current.Parent is IMemberReferenceOperation member && member.Instance == current && current.Type != null && current.Type.IsValueType)
            {
                current = member; // _pos.x = 1 writes _pos
            }

            IOperation parent = current.Parent;
            if (parent is IAssignmentOperation assignment && assignment.Target == current)
            {
                return true;
            }

            if (parent is IIncrementOrDecrementOperation)
            {
                return true;
            }

            if (parent is IArgumentOperation argument && argument.Parameter != null
                && (argument.Parameter.RefKind == RefKind.Ref || argument.Parameter.RefKind == RefKind.Out))
            {
                return true;
            }

            // (_a, _b) = (1, 2)
            IOperation tuple = current;
            while (tuple.Parent is ITupleOperation)
            {
                tuple = tuple.Parent;
            }

            if (tuple != current && tuple.Parent is IDeconstructionAssignmentOperation deconstruction && deconstruction.Target == tuple)
            {
                return true;
            }

            // ref float r = ref _mass;
            return current.Syntax.Parent is RefExpressionSyntax;
        }
    }
}
