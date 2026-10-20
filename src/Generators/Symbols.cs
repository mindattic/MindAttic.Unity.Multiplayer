using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace MindAttic.Unity.Multiplayer.Generators
{
    /// <summary>Metadata names the generator matches on. Core's types are matched by name, never referenced.</summary>
    internal static class Names
    {
        public const string CoreNamespace = "MindAttic.Unity.Multiplayer.Core";
        public const string ToServer = CoreNamespace + ".ToServerAttribute";
        public const string ToClients = CoreNamespace + ".ToClientsAttribute";
        public const string ToOwner = CoreNamespace + ".ToOwnerAttribute";
        public const string ToClient = CoreNamespace + ".ToClientAttribute";
        public const string Synced = CoreNamespace + ".SyncedAttribute";
        public const string NetSender = CoreNamespace + ".NetSender";
        public const string NetProtocol = CoreNamespace + ".NetProtocolAttribute";
        public const string NetEntity = "MindAttic.Unity.Multiplayer.NetEntity";

        /// <summary>The domain line of <c>ProtocolHash.Compute</c>; the two must stay identical.</summary>
        public const string ProtocolDomain = "MindAttic.Unity.Multiplayer.Protocol/1";
    }

    /// <summary>The four RPC directions, in the order the attributes are documented.</summary>
    internal enum Direction
    {
        ToServer,
        ToClients,
        ToOwner,
        ToClient,
    }

    internal static class Symbols
    {
        /// <summary>Fully qualified, <c>global::</c>-prefixed: what generated code writes.</summary>
        public static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;

        /// <summary>Fully qualified without <c>global::</c>: what protocol signatures hash.</summary>
        public static readonly SymbolDisplayFormat Signature =
            SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

        public static string AttributeName(Direction direction)
        {
            switch (direction)
            {
                case Direction.ToServer: return Names.ToServer;
                case Direction.ToClients: return Names.ToClients;
                case Direction.ToOwner: return Names.ToOwner;
                default: return Names.ToClient;
            }
        }

        public static string ShortName(Direction direction)
        {
            return direction.ToString();
        }

        public static AttributeData Find(ISymbol symbol, string metadataName)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass != null && attribute.AttributeClass.ToDisplayString() == metadataName)
                {
                    return attribute;
                }
            }

            return null;
        }

        public static T Named<T>(AttributeData attribute, string name, T fallback)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == name && !argument.Value.IsNull && argument.Value.Value is T value)
                {
                    return value;
                }
            }

            return fallback;
        }

        public static bool DerivesFrom(INamedTypeSymbol type, string metadataName)
        {
            for (INamedTypeSymbol t = type.BaseType; t != null; t = t.BaseType)
            {
                if (t.OriginalDefinition.ToDisplayString() == metadataName)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsGenericOrInGeneric(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol t = type; t != null; t = t.ContainingType)
            {
                if (t.TypeParameters.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary><c>_mass</c>, <c>m_mass</c> and <c>mass</c> all become <c>Mass</c>.</summary>
        public static string PropertyName(string field)
        {
            string name = field.StartsWith("m_", StringComparison.Ordinal) ? field.Substring(2) : field.TrimStart('_');
            if (name.Length == 0)
            {
                return field;
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>An identifier as C# source, <c>@</c>-escaped when it is a keyword.</summary>
        public static string Identifier(string name)
        {
            return Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
                ? "@" + name
                : name;
        }

        /// <summary>Methods named <paramref name="name"/> on the type and its bases that the type can call.</summary>
        public static IEnumerable<IMethodSymbol> CallableMethods(INamedTypeSymbol type, string name, Compilation compilation)
        {
            for (INamedTypeSymbol t = type; t != null; t = t.BaseType)
            {
                foreach (IMethodSymbol method in t.GetMembers(name).OfType<IMethodSymbol>())
                {
                    if (!method.IsStatic && method.MethodKind == MethodKind.Ordinary && compilation.IsSymbolAccessibleWithin(method, type))
                    {
                        yield return method;
                    }
                }
            }
        }
    }

    /// <summary>What Netcode for GameObjects can put on the wire, as far as the generator can tell from types alone.</summary>
    internal static class Serializability
    {
        private static readonly HashSet<string> UnityValueTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.Vector2",
            "UnityEngine.Vector3",
            "UnityEngine.Vector4",
            "UnityEngine.Vector2Int",
            "UnityEngine.Vector3Int",
            "UnityEngine.Quaternion",
            "UnityEngine.Color",
            "UnityEngine.Color32",
            "UnityEngine.Ray",
            "UnityEngine.Ray2D",
            "Unity.Netcode.NetworkObjectReference",
            "Unity.Netcode.NetworkBehaviourReference",
        };

        /// <summary>A type an RPC can carry: a value type below, a string, or a one-dimensional array of value types.</summary>
        public static bool CanSend(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String)
            {
                return true;
            }

            if (type is IArrayTypeSymbol array)
            {
                return array.Rank == 1 && CanReplicate(array.ElementType);
            }

            return CanReplicate(type);
        }

        /// <summary>A type a network variable can hold (no strings, no arrays).</summary>
        public static bool CanReplicate(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return true;
            }

            string name = type.OriginalDefinition.ToDisplayString();
            if (UnityValueTypes.Contains(name))
            {
                return true;
            }

            string ns = type.ContainingNamespace != null ? type.ContainingNamespace.ToDisplayString() : string.Empty;
            if (ns == "Unity.Collections" && type.Name.StartsWith("FixedString", StringComparison.Ordinal) && type.Name.EndsWith("Bytes", StringComparison.Ordinal))
            {
                return true;
            }

            if (ns == "Unity.Mathematics" && type.IsValueType)
            {
                return true;
            }

            foreach (INamedTypeSymbol contract in type.AllInterfaces)
            {
                string contractName = contract.ToDisplayString();
                if (contractName == "Unity.Netcode.INetworkSerializable" || contractName == "Unity.Netcode.INetworkSerializeByMemcpy")
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The generator's copy of <c>ProtocolHash.Compute</c> (Core is netstandard2.1 and cannot be referenced here):
    /// trim, drop empties, sort ordinally, de-duplicate, join after the domain line with '\n', 64-bit FNV-1a over UTF-8,
    /// zero becomes one. Generators.Tests pins it to Core's implementation.
    /// </summary>
    internal static class ProtocolHasher
    {
        public static IReadOnlyList<string> Normalize(IEnumerable<string> signatures)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string signature in signatures)
            {
                string trimmed = signature == null ? string.Empty : signature.Trim();
                if (trimmed.Length > 0)
                {
                    set.Add(trimmed);
                }
            }

            return set.ToList();
        }

        public static ulong Compute(IReadOnlyList<string> normalized)
        {
            var text = new StringBuilder(Names.ProtocolDomain);
            foreach (string signature in normalized)
            {
                text.Append('\n').Append(signature);
            }

            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(text.ToString()))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }
    }
}
