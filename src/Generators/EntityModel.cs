using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MindAttic.Unity.Multiplayer.Generators
{
    internal sealed class ParameterModel
    {
        public ParameterModel(IParameterSymbol symbol)
        {
            Symbol = symbol;
            Name = Symbols.Identifier(symbol.Name);
            Type = symbol.Type.ToDisplayString(Symbols.Qualified);
        }

        public IParameterSymbol Symbol { get; }

        public string Name { get; }

        public string Type { get; }
    }

    internal sealed class RpcModel
    {
        public Direction Direction;
        public string Name;
        public string Modifiers;
        public List<ParameterModel> Parameters;
        public bool Unreliable;
        public bool RequireOwnership;
        public int RateLimit;
        public int Burst;
        public string Handler;
        public bool PassSender;

        /// <summary>What goes on the wire: every parameter, except a [ToClient] call's target client id.</summary>
        public IReadOnlyList<ParameterModel> WireParameters
        {
            get { return Direction == Direction.ToClient ? Parameters.Skip(1).ToList() : Parameters; }
        }
    }

    internal sealed class SyncedModel
    {
        public string Field;
        public string Property;
        public string Type;
        public string SignatureType;
        public string Initial;
        public string OnChanged;

        public string VariableName
        {
            get { return "__" + Property + "NetVar"; }
        }
    }

    /// <summary>One NetEntity subclass's RPCs and synced fields, validated; invalid members are reported and left out.</summary>
    internal sealed class EntityModel
    {
        private EntityModel(INamedTypeSymbol type)
        {
            Type = type;
        }

        public INamedTypeSymbol Type { get; }

        public List<RpcModel> Rpcs { get; } = new List<RpcModel>();

        public List<SyncedModel> Synced { get; } = new List<SyncedModel>();

        /// <summary>One protocol signature per RPC and synced field (see ProtocolHash).</summary>
        public IEnumerable<string> Signatures()
        {
            string owner = Type.ToDisplayString(Symbols.Signature);
            foreach (RpcModel rpc in Rpcs)
            {
                string types = string.Join(",", rpc.WireParameters.Select(p => p.Symbol.Type.ToDisplayString(Symbols.Signature)));
                yield return "rpc " + Symbols.ShortName(rpc.Direction) + " " + owner + "." + rpc.Name + "(" + types + ") " + (rpc.Unreliable ? "Unreliable" : "Reliable");
            }

            for (int i = 0; i < Synced.Count; i++)
            {
                // Network variables are matched by declaration order, so the index is part of the protocol.
                yield return "synced " + owner + "#" + i.ToString(CultureInfo.InvariantCulture) + " " + Synced[i].Property + ": " + Synced[i].SignatureType;
            }
        }

        /// <summary>Validates the type's network members; returns null when it has none (or none that can be generated).</summary>
        public static EntityModel Build(INamedTypeSymbol type, Compilation compilation, Action<Diagnostic> report)
        {
            var methods = new List<KeyValuePair<IMethodSymbol, List<KeyValuePair<Direction, AttributeData>>>>();
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
            {
                var directions = new List<KeyValuePair<Direction, AttributeData>>();
                foreach (Direction direction in new[] { Direction.ToServer, Direction.ToClients, Direction.ToOwner, Direction.ToClient })
                {
                    AttributeData attribute = Symbols.Find(method, Symbols.AttributeName(direction));
                    if (attribute != null)
                    {
                        directions.Add(new KeyValuePair<Direction, AttributeData>(direction, attribute));
                    }
                }

                if (directions.Count > 0)
                {
                    methods.Add(new KeyValuePair<IMethodSymbol, List<KeyValuePair<Direction, AttributeData>>>(method, directions));
                }
            }

            var fields = new List<KeyValuePair<IFieldSymbol, AttributeData>>();
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>())
            {
                AttributeData attribute = Symbols.Find(field, Names.Synced);
                if (attribute != null)
                {
                    fields.Add(new KeyValuePair<IFieldSymbol, AttributeData>(field, attribute));
                }
            }

            bool isEntity = Symbols.DerivesFrom(type, Names.NetEntity);
            bool hasMembers = methods.Count > 0 || fields.Count > 0;

            if (isEntity && Symbols.IsGenericOrInGeneric(type) && (hasMembers || !type.IsAbstract))
            {
                report(Diagnostic.Create(Diagnostics.GenericEntity, Location(type), type.ToDisplayString()));
                return null;
            }

            if (!hasMembers)
            {
                return null;
            }

            if (!isEntity)
            {
                foreach (var m in methods)
                {
                    report(Diagnostic.Create(Diagnostics.NotANetEntity, Location(m.Key), Symbols.ShortName(m.Value[0].Key), m.Key.Name, type.ToDisplayString()));
                }

                foreach (var f in fields)
                {
                    report(Diagnostic.Create(Diagnostics.NotANetEntity, Location(f.Key), "Synced", f.Key.Name, type.ToDisplayString()));
                }

                return null;
            }

            INamedTypeSymbol notPartial = NonPartialType(type);
            if (notPartial != null)
            {
                report(Diagnostic.Create(
                    Diagnostics.MissingPartial,
                    Location(notPartial),
                    notPartial.ToDisplayString(),
                    "must be declared partial (as must every type containing it) so the generator can add the network plumbing of " + type.Name));
                return null;
            }

            var model = new EntityModel(type);
            INamedTypeSymbol netSender = compilation.GetTypeByMetadataName(Names.NetSender);
            foreach (var m in methods)
            {
                RpcModel rpc = BuildRpc(type, m.Key, m.Value, compilation, netSender, report);
                if (rpc != null)
                {
                    model.Rpcs.Add(rpc);
                }
            }

            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in fields)
            {
                SyncedModel synced = BuildSynced(type, f.Key, f.Value, compilation, taken, report);
                if (synced != null)
                {
                    model.Synced.Add(synced);
                }
            }

            return model;
        }

        private static RpcModel BuildRpc(
            INamedTypeSymbol type,
            IMethodSymbol method,
            List<KeyValuePair<Direction, AttributeData>> directions,
            Compilation compilation,
            INamedTypeSymbol netSender,
            Action<Diagnostic> report)
        {
            Location location = Location(method);
            if (directions.Count > 1)
            {
                report(Diagnostic.Create(Diagnostics.InvalidRpcSignature, location, method.Name,
                    "has more than one of [ToServer], [ToClients], [ToOwner] and [ToClient]; an RPC goes one way"));
                return null;
            }

            Direction direction = directions[0].Key;
            AttributeData attribute = directions[0].Value;

            string shapeProblem = ShapeProblem(method);
            if (shapeProblem != null)
            {
                report(Diagnostic.Create(Diagnostics.InvalidRpcSignature, location, method.Name, shapeProblem));
                return null;
            }

            MethodDeclarationSyntax declaration = method.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();
            if (declaration == null || !method.IsPartialDefinition || declaration.Body != null || declaration.ExpressionBody != null)
            {
                report(Diagnostic.Create(Diagnostics.MissingPartial, location, method.Name,
                    "must be a partial method declaration without a body, e.g. 'public partial void " + method.Name + "(...);'; the generator writes the body"));
                return null;
            }

            if (method.PartialImplementationPart != null)
            {
                report(Diagnostic.Create(Diagnostics.MissingPartial, location, method.Name,
                    "already has an implementing declaration; remove it, the generator writes the body"));
                return null;
            }

            var parameters = method.Parameters.Select(p => new ParameterModel(p)).ToList();
            if (direction == Direction.ToClient && (parameters.Count == 0 || parameters[0].Symbol.Type.SpecialType != SpecialType.System_UInt64))
            {
                report(Diagnostic.Create(Diagnostics.InvalidClientTarget, location, method.Name));
                return null;
            }

            bool sendable = true;
            for (int i = direction == Direction.ToClient ? 1 : 0; i < parameters.Count; i++)
            {
                IParameterSymbol p = parameters[i].Symbol;
                if (!Serializability.CanSend(p.Type))
                {
                    report(Diagnostic.Create(Diagnostics.UnserializableParameter, Location(p) ?? location, p.Name, method.Name, p.Type.ToDisplayString()));
                    sendable = false;
                }
            }

            if (!sendable)
            {
                return null;
            }

            var rpc = new RpcModel
            {
                Direction = direction,
                Name = method.Name,
                Modifiers = string.Join(" ", declaration.Modifiers.Select(t => t.Text)),
                Parameters = parameters,
                Unreliable = Symbols.Named(attribute, "Delivery", 0) == 1,
                RequireOwnership = direction == Direction.ToServer && Symbols.Named(attribute, "RequireOwnership", true),
                RateLimit = direction == Direction.ToServer ? Math.Max(0, Symbols.Named(attribute, "RateLimit", 0)) : 0,
                Burst = direction == Direction.ToServer ? Math.Max(0, Symbols.Named(attribute, "Burst", 0)) : 0,
                Handler = "On" + method.Name,
            };

            List<ITypeSymbol> wire = rpc.WireParameters.Select(p => p.Symbol.Type).ToList();
            bool plain = false;
            bool withSender = false;
            foreach (IMethodSymbol handler in Symbols.CallableMethods(type, rpc.Handler, compilation))
            {
                if (!handler.ReturnsVoid || handler.IsGenericMethod || handler.Parameters.Any(p => p.RefKind != RefKind.None))
                {
                    continue;
                }

                if (Matches(handler.Parameters, wire, 0))
                {
                    plain = true;
                }
                else if (direction == Direction.ToServer && netSender != null && handler.Parameters.Length == wire.Count + 1
                    && Matches(handler.Parameters, wire, 1)
                    && SymbolEqualityComparer.Default.Equals(handler.Parameters[wire.Count].Type, netSender))
                {
                    withSender = true;
                }
            }

            if (!plain && !withSender)
            {
                string expected = string.Join(", ", rpc.WireParameters.Select(p => p.Symbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + " " + p.Symbol.Name));
                if (direction == Direction.ToServer)
                {
                    expected = (expected.Length == 0 ? string.Empty : expected + ", ") + "NetSender sender";
                }

                report(Diagnostic.Create(Diagnostics.MissingHandler, location, Symbols.ShortName(direction), method.Name,
                    "void " + rpc.Handler + "(" + expected + ")", type.Name));
                return null;
            }

            rpc.PassSender = withSender;
            return rpc;
        }

        private static bool Matches(ImmutableArray<IParameterSymbol> parameters, List<ITypeSymbol> wire, int extra)
        {
            if (parameters.Length != wire.Count + extra)
            {
                return false;
            }

            for (int i = 0; i < wire.Count; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(parameters[i].Type, wire[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ShapeProblem(IMethodSymbol method)
        {
            if (!method.ReturnsVoid)
            {
                return "must return void; an RPC is one-way";
            }

            if (method.IsStatic)
            {
                return "must not be static; RPCs belong to a spawned entity";
            }

            if (method.IsGenericMethod)
            {
                return "must not be generic";
            }

            if (method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams))
            {
                return "must not have ref, out, in or params parameters";
            }

            return null;
        }

        private static SyncedModel BuildSynced(
            INamedTypeSymbol type,
            IFieldSymbol field,
            AttributeData attribute,
            Compilation compilation,
            HashSet<string> taken,
            Action<Diagnostic> report)
        {
            Location location = Location(field);
            if (field.IsStatic || field.IsConst || field.IsReadOnly)
            {
                report(Diagnostic.Create(Diagnostics.InvalidSyncedField, location, field.Name, "must be a non-static, non-readonly instance field"));
                return null;
            }

            if (!Serializability.CanReplicate(field.Type))
            {
                report(Diagnostic.Create(Diagnostics.UnserializableSyncedField, location, field.Name, field.Type.ToDisplayString()));
                return null;
            }

            string property = Symbols.PropertyName(field.Name);
            if (property == field.Name || !taken.Add(property) || HasMember(type, property) || HasMember(type, "__" + property + "NetVar"))
            {
                report(Diagnostic.Create(Diagnostics.InvalidSyncedField, location, field.Name,
                    "would generate the property '" + property + "', but that name is the field itself or already taken; rename the field (e.g. '_" + Lower(property) + "')"));
                return null;
            }

            string typeName = field.Type.ToDisplayString(Symbols.Qualified);
            string initial = "default(" + typeName + ")";
            VariableDeclaratorSyntax declarator = field.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>().FirstOrDefault();
            if (declarator != null && declarator.Initializer != null)
            {
                Optional<object> constant = compilation.GetSemanticModel(declarator.SyntaxTree).GetConstantValue(declarator.Initializer.Value);
                string literal = constant.HasValue ? ConstantLiteral(field.Type, constant.Value) : null;
                if (literal == null)
                {
                    report(Diagnostic.Create(Diagnostics.NonConstantSyncedInitializer, declarator.Initializer.GetLocation(), field.Name, property));
                    return null;
                }

                initial = "(" + typeName + ")(" + literal + ")";
            }

            string onChanged = Symbols.Named<string>(attribute, "OnChanged", null);
            if (onChanged != null)
            {
                bool found = Symbols.CallableMethods(type, onChanged, compilation).Any(m =>
                    m.ReturnsVoid && !m.IsGenericMethod && m.Parameters.Length == 2
                    && m.Parameters.All(p => p.RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(p.Type, field.Type)));
                if (!found)
                {
                    report(Diagnostic.Create(Diagnostics.InvalidOnChanged, location, field.Name, onChanged, type.Name,
                        field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                    return null;
                }
            }

            return new SyncedModel
            {
                Field = field.Name,
                Property = property,
                Type = typeName,
                SignatureType = field.Type.ToDisplayString(Symbols.Signature),
                Initial = initial,
                OnChanged = onChanged,
            };
        }

        /// <summary>A C# literal for a constant of a replicable type, or null when the generator cannot write one.</summary>
        private static string ConstantLiteral(ITypeSymbol type, object value)
        {
            if (value == null)
            {
                return null;
            }

            switch (value)
            {
                case bool b:
                    return b ? "true" : "false";
                case float f:
                    if (float.IsNaN(f)) return "float.NaN";
                    if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
                    if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
                    return f.ToString("R", CultureInfo.InvariantCulture) + "F";
                case double d:
                    if (double.IsNaN(d)) return "double.NaN";
                    if (double.IsPositiveInfinity(d)) return "double.PositiveInfinity";
                    if (double.IsNegativeInfinity(d)) return "double.NegativeInfinity";
                    return d.ToString("R", CultureInfo.InvariantCulture) + "D";
                case char c:
                    return SymbolDisplay.FormatLiteral(c, quote: true);
                case string _:
                    return null;
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture) + "L";
                case ulong ul:
                    return ul.ToString(CultureInfo.InvariantCulture) + "UL";
                case uint ui:
                    return ui.ToString(CultureInfo.InvariantCulture) + "U";
                case int _:
                case short _:
                case ushort _:
                case byte _:
                case sbyte _:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                    return null;
            }
        }

        private static bool HasMember(INamedTypeSymbol type, string name)
        {
            for (INamedTypeSymbol t = type; t != null; t = t.BaseType)
            {
                if (t.GetMembers(name).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Lower(string name)
        {
            return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static INamedTypeSymbol NonPartialType(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol t = type; t != null; t = t.ContainingType)
            {
                foreach (SyntaxReference reference in t.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is TypeDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        private static Location Location(ISymbol symbol)
        {
            return symbol.Locations.FirstOrDefault(l => l.IsInSource);
        }
    }
}
