using System;
using System.Collections.Generic;
using System.Text;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>Why bytes did not decode as a <see cref="ConnectionPayload"/>.</summary>
    public enum PayloadDecodeError
    {
        /// <summary>It decoded.</summary>
        None = 0,

        /// <summary>No bytes at all (a client that sent no connection data).</summary>
        Empty = 1,

        /// <summary>More than <see cref="ConnectionPayloadCodec.MaxEncodedLength"/> bytes.</summary>
        TooLarge = 2,

        /// <summary>The first two bytes are not the payload magic: not a MindAttic connection request.</summary>
        BadMagic = 3,

        /// <summary>
        /// A format version this build cannot read (newer, or zero). <see cref="PayloadDecodeResult.GameVersion"/>
        /// still holds the sender's game version when it could be read, since every format starts with it.
        /// </summary>
        UnsupportedFormatVersion = 4,

        /// <summary>The bytes end before the last field.</summary>
        Truncated = 5,

        /// <summary>Bytes remain after the last field.</summary>
        TrailingBytes = 6,

        /// <summary>A string field is not valid UTF-8.</summary>
        InvalidText = 7,

        /// <summary>Every field decoded but one fails <see cref="ConnectionPayload.Validate"/>.</summary>
        InvalidField = 8,
    }

    /// <summary>The outcome of <see cref="ConnectionPayloadCodec.TryDecode"/>.</summary>
    public readonly struct PayloadDecodeResult
    {
        private PayloadDecodeResult(ConnectionPayload payload, PayloadDecodeError error, int formatVersion, string gameVersion, string message)
        {
            Payload = payload;
            Error = error;
            FormatVersion = formatVersion;
            GameVersion = gameVersion;
            Message = message;
        }

        /// <summary>True when the bytes decoded to a valid payload.</summary>
        public bool Success
        {
            get { return Error == PayloadDecodeError.None; }
        }

        /// <summary>The decoded payload; null on failure.</summary>
        public ConnectionPayload Payload { get; }

        /// <summary>Why decoding failed, or <see cref="PayloadDecodeError.None"/>.</summary>
        public PayloadDecodeError Error { get; }

        /// <summary>The format version byte, or -1 when the bytes were too short or not a payload.</summary>
        public int FormatVersion { get; }

        /// <summary>The sender's game version when it could be read (even from an unsupported format); otherwise null.</summary>
        public string GameVersion { get; }

        /// <summary>What went wrong, in a sentence; empty on success.</summary>
        public string Message { get; }

        internal static PayloadDecodeResult Ok(ConnectionPayload payload, int formatVersion)
        {
            return new PayloadDecodeResult(payload, PayloadDecodeError.None, formatVersion, payload.GameVersion, string.Empty);
        }

        internal static PayloadDecodeResult Fail(PayloadDecodeError error, int formatVersion, string gameVersion, string message)
        {
            return new PayloadDecodeResult(null, error, formatVersion, gameVersion, message);
        }
    }

    /// <summary>
    /// The versioned binary form of a <see cref="ConnectionPayload"/>, carried in the transport's connection data.
    /// </summary>
    /// <remarks>
    /// <para>Format version 1, all integers little-endian, every string a one-byte length then that many UTF-8 bytes:</para>
    /// <code>
    /// offset 0   2 bytes  magic 'M' 'A' (0x4D 0x41)
    ///        2   1 byte   format version (1)
    ///        3   string   game version
    ///            8 bytes  protocol hash (ulong)
    ///            string   player token
    ///            string   display name
    ///            4 bytes  colour R, G, B, A
    /// </code>
    /// <para>
    /// Rule for every future format version: the magic, the version byte and the game-version string keep their
    /// place and encoding. A server can then tell an out-of-date client "wrong game version" even when it cannot
    /// read the rest of the payload. Decoding is strict: missing or extra bytes fail.
    /// </para>
    /// </remarks>
    public static class ConnectionPayloadCodec
    {
        /// <summary>The format version <see cref="Encode"/> writes.</summary>
        public const byte CurrentFormatVersion = 1;

        /// <summary>The first magic byte, <c>'M'</c>.</summary>
        public const byte Magic0 = 0x4D;

        /// <summary>The second magic byte, <c>'A'</c>.</summary>
        public const byte Magic1 = 0x41;

        /// <summary>Largest payload <see cref="TryDecode"/> looks at; anything longer is rejected unread.</summary>
        public const int MaxEncodedLength = 1024;

        private const int HeaderLength = 3;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Encodes a valid payload in the current format.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
        /// <exception cref="ArgumentException">The payload fails <see cref="ConnectionPayload.Validate"/>.</exception>
        public static byte[] Encode(ConnectionPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            payload.EnsureValid();

            var bytes = new List<byte>(64);
            bytes.Add(Magic0);
            bytes.Add(Magic1);
            bytes.Add(CurrentFormatVersion);
            WriteString(bytes, payload.GameVersion, "GameVersion");

            ulong hash = payload.ProtocolHash.Value;
            for (int i = 0; i < 8; i++)
            {
                bytes.Add((byte)(hash >> (8 * i)));
            }

            WriteString(bytes, payload.PlayerToken, "PlayerToken");
            WriteString(bytes, payload.DisplayName, "DisplayName");
            bytes.Add(payload.Color.R);
            bytes.Add(payload.Color.G);
            bytes.Add(payload.Color.B);
            bytes.Add(payload.Color.A);
            return bytes.ToArray();
        }

        /// <summary>
        /// Decodes connection data. Never throws on bad input: every failure comes back as a
        /// <see cref="PayloadDecodeResult"/> with a <see cref="PayloadDecodeError"/> and a message.
        /// </summary>
        public static PayloadDecodeResult TryDecode(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return PayloadDecodeResult.Fail(PayloadDecodeError.Empty, -1, null, "The connection request carried no data.");
            }

            if (data.Length > MaxEncodedLength)
            {
                return PayloadDecodeResult.Fail(PayloadDecodeError.TooLarge, -1, null,
                    "The connection request is " + data.Length + " bytes; the limit is " + MaxEncodedLength + ".");
            }

            if (data.Length < HeaderLength)
            {
                return PayloadDecodeResult.Fail(PayloadDecodeError.Truncated, -1, null, "The connection request is too short to hold a header.");
            }

            if (data[0] != Magic0 || data[1] != Magic1)
            {
                return PayloadDecodeResult.Fail(PayloadDecodeError.BadMagic, -1, null, "The connection request is not a MindAttic payload.");
            }

            int formatVersion = data[2];
            int offset = HeaderLength;
            string gameVersion;
            PayloadDecodeError error = ReadString(data, ref offset, out gameVersion);

            if (formatVersion != CurrentFormatVersion)
            {
                return PayloadDecodeResult.Fail(PayloadDecodeError.UnsupportedFormatVersion, formatVersion,
                    error == PayloadDecodeError.None ? gameVersion : null,
                    "The connection request uses payload format " + formatVersion + "; this build reads format " + CurrentFormatVersion + ".");
            }

            if (error != PayloadDecodeError.None)
            {
                return FieldFailure(error, formatVersion, null, "GameVersion");
            }

            if (data.Length - offset < 8)
            {
                return FieldFailure(PayloadDecodeError.Truncated, formatVersion, gameVersion, "ProtocolHash");
            }

            ulong hash = 0UL;
            for (int i = 0; i < 8; i++)
            {
                hash |= (ulong)data[offset + i] << (8 * i);
            }

            offset += 8;

            string playerToken;
            error = ReadString(data, ref offset, out playerToken);
            if (error != PayloadDecodeError.None)
            {
                return FieldFailure(error, formatVersion, gameVersion, "PlayerToken");
            }

            string displayName;
            error = ReadString(data, ref offset, out displayName);
            if (error != PayloadDecodeError.None)
            {
                return FieldFailure(error, formatVersion, gameVersion, "DisplayName");
            }

            if (data.Length - offset < 4)
            {
                return FieldFailure(PayloadDecodeError.Truncated, formatVersion, gameVersion, "Color");
            }

            var color = new PlayerColor(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);
            offset += 4;

            if (offset != data.Length)
            {
                return PayloadDecodeResult.Fail(PayloadDecodeError.TrailingBytes, formatVersion, gameVersion,
                    (data.Length - offset) + " unexpected bytes follow the last field.");
            }

            var payload = new ConnectionPayload(gameVersion, new ProtocolHash(hash), playerToken, displayName, color);
            IReadOnlyList<string> problems = payload.Validate();
            if (problems.Count > 0)
            {
                return PayloadDecodeResult.Fail(PayloadDecodeError.InvalidField, formatVersion, gameVersion, string.Join(" ", problems));
            }

            return PayloadDecodeResult.Ok(payload, formatVersion);
        }

        private static PayloadDecodeResult FieldFailure(PayloadDecodeError error, int formatVersion, string gameVersion, string field)
        {
            string message = error == PayloadDecodeError.InvalidText
                ? field + " is not valid UTF-8."
                : "The connection request ends inside " + field + ".";
            return PayloadDecodeResult.Fail(error, formatVersion, gameVersion, message);
        }

        private static void WriteString(List<byte> bytes, string value, string field)
        {
            byte[] encoded = StrictUtf8.GetBytes(value);
            if (encoded.Length > byte.MaxValue)
            {
                throw new ArgumentException(field + " is " + encoded.Length + " UTF-8 bytes; the limit is " + byte.MaxValue + ".");
            }

            bytes.Add((byte)encoded.Length);
            bytes.AddRange(encoded);
        }

        private static PayloadDecodeError ReadString(byte[] data, ref int offset, out string value)
        {
            value = null;
            if (offset >= data.Length)
            {
                return PayloadDecodeError.Truncated;
            }

            int length = data[offset];
            if (data.Length - offset - 1 < length)
            {
                return PayloadDecodeError.Truncated;
            }

            try
            {
                value = StrictUtf8.GetString(data, offset + 1, length);
            }
            catch (DecoderFallbackException)
            {
                return PayloadDecodeError.InvalidText;
            }

            offset += 1 + length;
            return PayloadDecodeError.None;
        }
    }
}
