using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Records a game assembly's protocol hash. The source generator emits it, once per assembly that declares
    /// RPCs or synced fields: <c>[assembly: NetProtocol("0123456789abcdef", 12)]</c>. Games do not write it by hand.
    /// </summary>
    /// <remarks>
    /// <see cref="ProtocolHash.FromAssemblies(System.Collections.Generic.IEnumerable{System.Reflection.Assembly})"/>
    /// reads it back at runtime. <see cref="Hash"/> is the 16-hex-digit form of
    /// <see cref="ProtocolHash.Compute(System.Collections.Generic.IEnumerable{string})"/> over the assembly's signatures.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class NetProtocolAttribute : Attribute
    {
        /// <summary>Records the hash and how many signatures went into it.</summary>
        /// <param name="hash">16 hex digits, as <see cref="ProtocolHash.ToString"/> writes them.</param>
        /// <param name="signatureCount">The number of distinct RPC and synced-field signatures hashed.</param>
        public NetProtocolAttribute(string hash, int signatureCount)
        {
            Hash = hash;
            SignatureCount = signatureCount;
        }

        /// <summary>The protocol hash as 16 hex digits.</summary>
        public string Hash { get; }

        /// <summary>The number of distinct RPC and synced-field signatures hashed, for diagnostics.</summary>
        public int SignatureCount { get; }
    }
}
