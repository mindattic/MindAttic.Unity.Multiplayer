using System;
using System.Globalization;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// A player's chosen colour as 8-bit RGBA, engine-free so Core can carry it in the approval payload.
    /// The adapter converts it to and from <c>UnityEngine.Color32</c>.
    /// </summary>
    public readonly struct PlayerColor : IEquatable<PlayerColor>
    {
        /// <summary>Builds a colour from its channels; alpha defaults to opaque.</summary>
        public PlayerColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>Opaque white.</summary>
        public static PlayerColor White
        {
            get { return new PlayerColor(255, 255, 255); }
        }

        /// <summary>Red channel.</summary>
        public byte R { get; }

        /// <summary>Green channel.</summary>
        public byte G { get; }

        /// <summary>Blue channel.</summary>
        public byte B { get; }

        /// <summary>Alpha channel; 255 is opaque.</summary>
        public byte A { get; }

        /// <summary>Packs the colour as <c>0xRRGGBBAA</c>.</summary>
        public uint ToRgba32()
        {
            return ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A;
        }

        /// <summary>Unpacks a <c>0xRRGGBBAA</c> value.</summary>
        public static PlayerColor FromRgba32(uint rgba)
        {
            return new PlayerColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        /// <inheritdoc />
        public bool Equals(PlayerColor other)
        {
            return R == other.R && G == other.G && B == other.B && A == other.A;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is PlayerColor other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (int)ToRgba32();
        }

        /// <summary><c>#RRGGBBAA</c> in upper-case hex.</summary>
        public override string ToString()
        {
            return "#" + ToRgba32().ToString("X8", CultureInfo.InvariantCulture);
        }

        /// <summary>Equality.</summary>
        public static bool operator ==(PlayerColor left, PlayerColor right)
        {
            return left.Equals(right);
        }

        /// <summary>Inequality.</summary>
        public static bool operator !=(PlayerColor left, PlayerColor right)
        {
            return !left.Equals(right);
        }
    }
}
