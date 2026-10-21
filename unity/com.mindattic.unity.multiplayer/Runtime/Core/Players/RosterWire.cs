using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The bytes the roster travels in (UM-T55): roster updates from the server, and a player's change request and the
    /// server's reply. Engine-free so <c>dotnet test</c> covers it; the adapter carries the bytes in named messages.
    /// Decoding is strict and never throws: anything malformed is refused.
    /// </summary>
    /// <remarks>
    /// Every message starts with <see cref="Version"/>. A roster update is the flags byte (bit 0: full), then the
    /// players (count, then per player: seat, client id, name, colour as RGBA, flags host/connected, ping), then the
    /// freed seats. <see cref="NetPlayer.IsLocal"/> never travels: each peer works it out.
    /// </remarks>
    public static class RosterWire
    {
        /// <summary>The format version every message starts with.</summary>
        public const byte Version = 1;

        /// <summary>The most players or seats one message may carry.</summary>
        public const int MaxEntries = 1024;

        /// <summary>The longest refusal reason a reply carries, in characters.</summary>
        public const int MaxReasonLength = 256;

        private const int MaxWireNameLength = 256;
        private const byte FlagFull = 1;
        private const byte FlagHost = 1;
        private const byte FlagConnected = 2;
        private const byte FlagHasName = 1;
        private const byte FlagHasColor = 2;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        /// <summary>Encodes a roster update.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
        /// <exception cref="ArgumentException">It carries more than <see cref="MaxEntries"/> players or seats.</exception>
        public static byte[] EncodeUpdate(RosterUpdate update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            if (update.Players.Count > MaxEntries || update.Removed.Count > MaxEntries)
            {
                throw new ArgumentException("A roster update carries at most " + MaxEntries + " players and seats.", nameof(update));
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Version);
                writer.Write(update.Full ? FlagFull : (byte)0);
                writer.Write((ushort)update.Players.Count);
                foreach (NetPlayer player in update.Players)
                {
                    writer.Write(player.Seat);
                    writer.Write(player.ClientId);
                    WriteString(writer, player.DisplayName, MaxWireNameLength);
                    writer.Write(player.Color.ToRgba32());
                    writer.Write((byte)((player.IsHost ? FlagHost : 0) | (player.IsConnected ? FlagConnected : 0)));
                    writer.Write(player.PingMs);
                }

                writer.Write((ushort)update.Removed.Count);
                foreach (int seat in update.Removed)
                {
                    writer.Write(seat);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Decodes a roster update; false (and null) for anything malformed.</summary>
        public static bool TryDecodeUpdate(byte[] data, out RosterUpdate update)
        {
            update = null;
            try
            {
                using (var reader = Reader(data))
                {
                    if (reader == null)
                    {
                        return false;
                    }

                    bool full = (reader.ReadByte() & FlagFull) != 0;
                    int count = reader.ReadUInt16();
                    if (count > MaxEntries)
                    {
                        return false;
                    }

                    var players = new List<NetPlayer>(count);
                    var seats = new HashSet<int>();
                    for (int i = 0; i < count; i++)
                    {
                        int seat = reader.ReadInt32();
                        ulong clientId = reader.ReadUInt64();
                        string name = ReadString(reader, MaxWireNameLength);
                        PlayerColor color = PlayerColor.FromRgba32(reader.ReadUInt32());
                        byte flags = reader.ReadByte();
                        int ping = reader.ReadInt32();
                        if (seat < 0 || name == null || !seats.Add(seat))
                        {
                            return false;
                        }

                        players.Add(new NetPlayer(seat, clientId, name, color, (flags & FlagHost) != 0, (flags & FlagConnected) != 0, ping));
                    }

                    int removedCount = reader.ReadUInt16();
                    if (removedCount > MaxEntries)
                    {
                        return false;
                    }

                    var removed = new List<int>(removedCount);
                    for (int i = 0; i < removedCount; i++)
                    {
                        int seat = reader.ReadInt32();
                        if (seat < 0)
                        {
                            return false;
                        }

                        removed.Add(seat);
                    }

                    if (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        return false;
                    }

                    update = new RosterUpdate(full, players, removed);
                    return true;
                }
            }
            catch (Exception)
            {
                update = null;
                return false;
            }
        }

        /// <summary>Encodes a change request (<paramref name="requestId"/> matches the reply to it).</summary>
        public static byte[] EncodeChangeRequest(uint requestId, PlayerChangeRequest request)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Version);
                writer.Write(requestId);
                writer.Write((byte)((request.DisplayName != null ? FlagHasName : 0) | (request.Color.HasValue ? FlagHasColor : 0)));
                if (request.DisplayName != null)
                {
                    WriteString(writer, request.DisplayName, MaxWireNameLength);
                }

                if (request.Color.HasValue)
                {
                    writer.Write(request.Color.Value.ToRgba32());
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Decodes a change request; false for anything malformed (the request itself is checked with <see cref="PlayerChangeRequest.Validate"/>).</summary>
        public static bool TryDecodeChangeRequest(byte[] data, out uint requestId, out PlayerChangeRequest request)
        {
            requestId = 0;
            request = default(PlayerChangeRequest);
            try
            {
                using (var reader = Reader(data))
                {
                    if (reader == null)
                    {
                        return false;
                    }

                    requestId = reader.ReadUInt32();
                    byte flags = reader.ReadByte();
                    string name = null;
                    PlayerColor? color = null;
                    if ((flags & FlagHasName) != 0)
                    {
                        name = ReadString(reader, MaxWireNameLength);
                        if (name == null)
                        {
                            return false;
                        }
                    }

                    if ((flags & FlagHasColor) != 0)
                    {
                        color = PlayerColor.FromRgba32(reader.ReadUInt32());
                    }

                    if (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        return false;
                    }

                    request = new PlayerChangeRequest(name, color);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Encodes the server's reply to change request <paramref name="requestId"/>.</summary>
        public static byte[] EncodeChangeReply(uint requestId, bool accepted, string reason)
        {
            string text = reason ?? string.Empty;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Version);
                writer.Write(requestId);
                writer.Write(accepted);
                WriteString(writer, accepted ? string.Empty : text, MaxReasonLength);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Decodes a reply; false for anything malformed.</summary>
        public static bool TryDecodeChangeReply(byte[] data, out uint requestId, out bool accepted, out string reason)
        {
            requestId = 0;
            accepted = false;
            reason = null;
            try
            {
                using (var reader = Reader(data))
                {
                    if (reader == null)
                    {
                        return false;
                    }

                    requestId = reader.ReadUInt32();
                    accepted = reader.ReadBoolean();
                    reason = ReadString(reader, MaxReasonLength);
                    return reason != null && reader.BaseStream.Position == reader.BaseStream.Length;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>A reader past the version byte, or null when the data is empty or another version.</summary>
        private static BinaryReader Reader(byte[] data)
        {
            if (data == null || data.Length < 1 || data[0] != Version)
            {
                return null;
            }

            var reader = new BinaryReader(new MemoryStream(data, false), Utf8);
            reader.ReadByte();
            return reader;
        }

        private static void WriteString(BinaryWriter writer, string text, int maxChars)
        {
            string value = text ?? string.Empty;
            if (value.Length > maxChars)
            {
                int cut = char.IsHighSurrogate(value[maxChars - 1]) ? maxChars - 1 : maxChars; // never split a pair
                value = value.Substring(0, cut);
            }

            byte[] bytes = Utf8.GetBytes(value);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        /// <summary>A length-prefixed UTF-8 string, or null when it is too long or not valid UTF-8.</summary>
        private static string ReadString(BinaryReader reader, int maxChars)
        {
            int length = reader.ReadUInt16();
            if (length > maxChars * 4)
            {
                return null;
            }

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                return null;
            }

            string text = Utf8.GetString(bytes);
            return text.Length > maxChars ? null : text;
        }
    }
}
