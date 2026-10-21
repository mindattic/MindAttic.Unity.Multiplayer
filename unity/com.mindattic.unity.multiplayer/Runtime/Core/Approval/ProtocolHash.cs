using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A 64-bit fingerprint of a game's network protocol: every <c>[ToServer]</c> / <c>[ToClients]</c> /
    /// <c>[ToOwner]</c> / <c>[ToClient]</c> call and every <c>[Synced]</c> field signature. Connection approval
    /// rejects a client whose hash differs from the server's (RFC 0001, "Protocol hash").
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contract the source generator fills: it collects one signature string per RPC and synced field,
    /// computes <see cref="Compute(IEnumerable{string})"/> over them at compile time, and emits
    /// <c>[assembly: NetProtocol("&lt;16 hex digits&gt;", &lt;count&gt;)]</c> (<see cref="NetProtocolAttribute"/>)
    /// into the game assembly. At runtime <see cref="FromAssemblies(IEnumerable{Assembly})"/> reads it back.
    /// </para>
    /// <para>
    /// The algorithm is fixed so the generator (netstandard2.0, no reference to this assembly) can reproduce it:
    /// trim each signature, drop empty ones, sort the rest ordinally and remove duplicates, join them with
    /// <c>'\n'</c> after the line <see cref="Domain"/>, and take 64-bit FNV-1a over the UTF-8 bytes. A result of
    /// zero is replaced by one, so <see cref="None"/> (zero) always means "no protocol was generated".
    /// </para>
    /// </remarks>
    public readonly struct ProtocolHash : IEquatable<ProtocolHash>
    {
        /// <summary>The first line hashed, so the format can change later without colliding with this one.</summary>
        public const string Domain = "Unity.Multiplayer.Protocol/1";

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>Wraps a raw hash value. Zero is <see cref="None"/>.</summary>
        public ProtocolHash(ulong value)
        {
            Value = value;
        }

        /// <summary>"No protocol generated": the value of <c>default(ProtocolHash)</c>.</summary>
        public static ProtocolHash None
        {
            get { return default(ProtocolHash); }
        }

        /// <summary>The raw 64-bit value.</summary>
        public ulong Value { get; }

        /// <summary>True for <see cref="None"/>.</summary>
        public bool IsNone
        {
            get { return Value == 0UL; }
        }

        /// <summary>
        /// Hashes a set of RPC / synced-field signatures. Order and duplicates do not matter; leading and
        /// trailing whitespace is ignored. An empty set still yields a non-zero hash (a game with no RPCs).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="signatures"/> is null.</exception>
        public static ProtocolHash Compute(IEnumerable<string> signatures)
        {
            if (signatures == null)
            {
                throw new ArgumentNullException(nameof(signatures));
            }

            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string signature in signatures)
            {
                if (signature == null)
                {
                    continue;
                }

                string trimmed = signature.Trim();
                if (trimmed.Length > 0)
                {
                    set.Add(trimmed);
                }
            }

            var text = new StringBuilder(Domain);
            foreach (string signature in set)
            {
                text.Append('\n').Append(signature);
            }

            ulong hash = Fnv1a64(Encoding.UTF8.GetBytes(text.ToString()));
            return new ProtocolHash(hash == 0UL ? 1UL : hash);
        }

        /// <summary>Hashes the given signatures; see <see cref="Compute(IEnumerable{string})"/>.</summary>
        public static ProtocolHash Compute(params string[] signatures)
        {
            return Compute((IEnumerable<string>)signatures);
        }

        /// <summary>
        /// Combines the hashes of several assemblies into one, independent of order. <see cref="None"/> parts are
        /// skipped; no parts gives <see cref="None"/> and a single part is returned unchanged.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="parts"/> is null.</exception>
        public static ProtocolHash Combine(IEnumerable<ProtocolHash> parts)
        {
            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

            var present = new List<string>();
            ProtocolHash only = None;
            foreach (ProtocolHash part in parts)
            {
                if (!part.IsNone)
                {
                    only = part;
                    present.Add("assembly:" + part.ToString());
                }
            }

            if (present.Count == 0)
            {
                return None;
            }

            return present.Count == 1 ? only : Compute(present);
        }

        /// <summary>
        /// Reads the generator's <see cref="NetProtocolAttribute"/> from each assembly and combines what it
        /// finds (<see cref="Combine"/>). Assemblies without the attribute are skipped; if none has it the
        /// result is <see cref="None"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> is null.</exception>
        /// <exception cref="FormatException">An attribute holds a value that is not 16 hex digits.</exception>
        public static ProtocolHash FromAssemblies(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
            {
                throw new ArgumentNullException(nameof(assemblies));
            }

            var parts = new List<ProtocolHash>();
            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null)
                {
                    continue;
                }

                var attribute = (NetProtocolAttribute)Attribute.GetCustomAttribute(assembly, typeof(NetProtocolAttribute));
                if (attribute != null)
                {
                    parts.Add(Parse(attribute.Hash));
                }
            }

            return Combine(parts);
        }

        /// <summary>Reads and combines the generator's hash from the given assemblies; see <see cref="FromAssemblies(IEnumerable{Assembly})"/>.</summary>
        public static ProtocolHash FromAssemblies(params Assembly[] assemblies)
        {
            return FromAssemblies((IEnumerable<Assembly>)assemblies);
        }

        /// <summary>Parses 16 hex digits (case-insensitive, optional <c>0x</c> prefix).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="FormatException">The text is not 16 hex digits.</exception>
        public static ProtocolHash Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            ProtocolHash hash;
            if (!TryParse(text, out hash))
            {
                throw new FormatException("A protocol hash is 16 hex digits; got '" + text + "'.");
            }

            return hash;
        }

        /// <summary>Parses 16 hex digits (case-insensitive, optional <c>0x</c> prefix); false if the text is anything else.</summary>
        public static bool TryParse(string text, out ProtocolHash hash)
        {
            hash = None;
            if (text == null)
            {
                return false;
            }

            string digits = text.Trim();
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                digits = digits.Substring(2);
            }

            if (digits.Length != 16)
            {
                return false;
            }

            ulong value;
            if (!ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            hash = new ProtocolHash(value);
            return true;
        }

        /// <summary>64-bit FNV-1a, exposed so tests can pin the algorithm the generator must match.</summary>
        internal static ulong Fnv1a64(byte[] bytes)
        {
            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }

            return hash;
        }

        /// <inheritdoc />
        public bool Equals(ProtocolHash other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ProtocolHash other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>16 lower-case hex digits.</summary>
        public override string ToString()
        {
            return Value.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>Equality.</summary>
        public static bool operator ==(ProtocolHash left, ProtocolHash right)
        {
            return left.Equals(right);
        }

        /// <summary>Inequality.</summary>
        public static bool operator !=(ProtocolHash left, ProtocolHash right)
        {
            return !left.Equals(right);
        }
    }
}
