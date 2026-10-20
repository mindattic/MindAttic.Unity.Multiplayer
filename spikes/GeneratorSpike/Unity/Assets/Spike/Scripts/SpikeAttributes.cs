using System;

namespace MindAttic.Spike
{
    /// <summary>UM-T4 spike stand-in for the real [ToServer]: the generator writes the partial body and an NGO Rpc stub.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ToServerAttribute : Attribute { }

    /// <summary>UM-T4 spike stand-in for the real [Synced]: the generator emits a NetworkVariable and a property.</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class SyncedAttribute : Attribute { }
}
