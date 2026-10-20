using System;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>How an RPC travels.</summary>
    public enum Delivery
    {
        /// <summary>Arrives, in order with the object's other reliable RPCs. The default.</summary>
        Reliable = 0,

        /// <summary>May be dropped or arrive out of order; for high-rate state like cursor positions, where the next call supersedes this one.</summary>
        Unreliable = 1,
    }

    /// <summary>
    /// Marks a <c>partial void</c> method on a <c>NetEntity</c> as a call from a client to the server (intent in, BIBLE
    /// UM-LAW-1). Calling <c>Move(p)</c> anywhere sends it; the server runs the game's handler
    /// <c>void OnMove(&lt;same parameters&gt;, NetSender sender)</c>.
    /// </summary>
    /// <remarks>
    /// <para>The sending body and the transport stub (<c>MoveServerRpc</c>) are generated (hand-written in the same shape
    /// until the generator lands, RFC 0001). Before the handler runs, the server checks ownership
    /// (<see cref="RequireOwnership"/>) and the sender's rate (<see cref="RateLimit"/>); a call that fails either is
    /// dropped and reported, never handled.</para>
    /// <para>A host calling it on itself runs the handler locally with the host as the sender.</para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ToServerAttribute : Attribute
    {
        /// <summary>Reliable (default) or unreliable.</summary>
        public Delivery Delivery { get; set; } = Delivery.Reliable;

        /// <summary>
        /// The most calls per second the server accepts from one sender for this RPC (a token bucket refilled at this
        /// rate). 0, the default, means no limit.
        /// </summary>
        public int RateLimit { get; set; }

        /// <summary>
        /// How many calls a sender may make back to back before <see cref="RateLimit"/> applies (the bucket's size).
        /// 0, the default, means one second's worth: the same as <see cref="RateLimit"/>.
        /// </summary>
        public int Burst { get; set; }

        /// <summary>
        /// True (the default): only the client that owns the entity may call it; anyone else's call is dropped. False lets
        /// any connected client call it (a shared button, a vote).
        /// </summary>
        public bool RequireOwnership { get; set; } = true;
    }

    /// <summary>
    /// Marks a <c>partial void</c> method on a <c>NetEntity</c> as a call from the server to every client (the host's own
    /// player included). Only the server may call it; each client runs <c>void OnName(&lt;same parameters&gt;)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ToClientsAttribute : Attribute
    {
        /// <summary>Reliable (default) or unreliable.</summary>
        public Delivery Delivery { get; set; } = Delivery.Reliable;
    }

    /// <summary>
    /// Marks a <c>partial void</c> method on a <c>NetEntity</c> as a call from the server to the client that owns the
    /// entity. Only the server may call it; the owner runs <c>void OnName(&lt;same parameters&gt;)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ToOwnerAttribute : Attribute
    {
        /// <summary>Reliable (default) or unreliable.</summary>
        public Delivery Delivery { get; set; } = Delivery.Reliable;
    }

    /// <summary>
    /// Marks a <c>partial void</c> method on a <c>NetEntity</c> as a call from the server to one client. Its first
    /// parameter is the target, a <c>ulong</c> client id (a <see cref="NetSender"/> converts to one, so a handler can
    /// answer whoever called it). Only the server may call it; that client runs <c>void OnName(&lt;the other
    /// parameters&gt;)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ToClientAttribute : Attribute
    {
        /// <summary>Reliable (default) or unreliable.</summary>
        public Delivery Delivery { get; set; } = Delivery.Reliable;
    }

    /// <summary>
    /// Marks a field on a <c>NetEntity</c> as replicated state. For <c>[Synced] float _mass;</c> the generator emits a
    /// <c>Mass</c> property backed by a network variable: the server writes it, every client reads it, late joiners get
    /// the current value. Writing it anywhere but the server throws (BIBLE UM-LAW-1).
    /// </summary>
    /// <remarks>The field itself is only a declaration; read and write the generated property, never the field.</remarks>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class SyncedAttribute : Attribute
    {
        /// <summary>
        /// Optional: the name of a <c>void Handler(T previous, T current)</c> method on the same class, called on every peer
        /// (server included) whenever the value changes.
        /// </summary>
        public string OnChanged { get; set; }
    }
}
