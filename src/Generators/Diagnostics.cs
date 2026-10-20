using Microsoft.CodeAnalysis;

namespace MindAttic.Unity.Multiplayer.Generators
{
    /// <summary>
    /// Every diagnostic the generator and its analyzer report. IDs are stable: MAMP = MindAttic MultiPlayer.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>MAMP001 error: an RPC method or its type is not declared <c>partial</c> (or already has a body).</item>
    /// <item>MAMP002 error: an RPC method has no <c>On&lt;Name&gt;</c> handler.</item>
    /// <item>MAMP003 error: an RPC parameter's type cannot be sent.</item>
    /// <item>MAMP004 error: a <c>[Synced]</c> field's type cannot be a network variable.</item>
    /// <item>MAMP005 error: a generic <c>NetEntity</c> subclass.</item>
    /// <item>MAMP006 error: code writes a <c>[Synced]</c> field directly.</item>
    /// <item>MAMP007 warning: code reads a <c>[Synced]</c> field directly.</item>
    /// <item>MAMP008 error: an RPC or synced attribute on a type that is not a <c>NetEntity</c>.</item>
    /// <item>MAMP009 error: an RPC method's shape is not allowed (non-void, static, generic, ref/out/params, two directions).</item>
    /// <item>MAMP010 error: a <c>[ToClient]</c> method whose first parameter is not the <c>ulong</c> target client id.</item>
    /// <item>MAMP011 error: <c>[Synced(OnChanged = ...)]</c> names no <c>void Handler(T previous, T current)</c>.</item>
    /// <item>MAMP012 error: a <c>[Synced]</c> field that is static, readonly or const, or whose property name is taken.</item>
    /// <item>MAMP013 error: a <c>[Synced]</c> field initializer that is not a compile-time constant.</item>
    /// </list>
    /// </remarks>
    internal static class Diagnostics
    {
        private const string Category = "MindAttic.Multiplayer";

        public static readonly DiagnosticDescriptor MissingPartial = Error(
            "MAMP001",
            "Network member is not partial",
            "'{0}' {1}");

        public static readonly DiagnosticDescriptor MissingHandler = Error(
            "MAMP002",
            "Missing On<Name> handler",
            "[{0}] method '{1}' has no handler: add '{2}' to {3}");

        public static readonly DiagnosticDescriptor UnserializableParameter = Error(
            "MAMP003",
            "RPC parameter cannot be sent",
            "Parameter '{0}' of '{1}' has type '{2}', which the network cannot send; use a primitive, an enum, a string, a Unity value type (Vector2/3/4, Quaternion, Color, Color32...), a FixedString, a struct implementing INetworkSerializable or INetworkSerializeByMemcpy, or a one-dimensional array of those");

        public static readonly DiagnosticDescriptor UnserializableSyncedField = Error(
            "MAMP004",
            "[Synced] field type cannot be replicated",
            "[Synced] field '{0}' has type '{1}', which cannot be a network variable; use a primitive, an enum, a Unity value type, a FixedString or a struct implementing INetworkSerializable or INetworkSerializeByMemcpy (not string or an array)");

        public static readonly DiagnosticDescriptor GenericEntity = Error(
            "MAMP005",
            "NetEntity subclass is generic",
            "'{0}' is a generic NetEntity subclass; its RPCs and synced fields would have no stable network ids, so NetEntity subclasses must not be generic (nor nested in a generic type)");

        public static readonly DiagnosticDescriptor SyncedFieldWrite = Error(
            "MAMP006",
            "Direct write to a [Synced] field",
            "'{0}' is a [Synced] declaration: a write to it never reaches the network; write the generated '{1}' property on the server instead");

        public static readonly DiagnosticDescriptor SyncedFieldRead = new DiagnosticDescriptor(
            "MAMP007",
            "Direct read of a [Synced] field",
            "'{0}' is a [Synced] declaration and always holds its default; read the generated '{1}' property instead",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NotANetEntity = Error(
            "MAMP008",
            "Network attribute outside a NetEntity",
            "[{0}] on '{1}' does nothing: '{2}' does not derive from MindAttic.Unity.Multiplayer.NetEntity");

        public static readonly DiagnosticDescriptor InvalidRpcSignature = Error(
            "MAMP009",
            "RPC method shape not allowed",
            "RPC method '{0}' {1}");

        public static readonly DiagnosticDescriptor InvalidClientTarget = Error(
            "MAMP010",
            "[ToClient] needs a target client id",
            "[ToClient] method '{0}' must take the target client id as its first parameter, a ulong (a NetSender converts to one)");

        public static readonly DiagnosticDescriptor InvalidOnChanged = Error(
            "MAMP011",
            "[Synced] OnChanged handler not found",
            "[Synced] field '{0}' names OnChanged = \"{1}\", but {2} has no accessible instance method 'void {1}({3} previous, {3} current)'");

        public static readonly DiagnosticDescriptor InvalidSyncedField = Error(
            "MAMP012",
            "[Synced] field not allowed",
            "[Synced] field '{0}' {1}");

        public static readonly DiagnosticDescriptor NonConstantSyncedInitializer = Error(
            "MAMP013",
            "[Synced] initializer is not a constant",
            "[Synced] field '{0}' can only start from a compile-time constant; set '{1}' on the server in OnSpawned instead");

        private static DiagnosticDescriptor Error(string id, string title, string message)
        {
            return new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
        }
    }
}
