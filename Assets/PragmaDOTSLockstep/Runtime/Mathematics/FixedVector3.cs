using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Pragma.Lockstep.Mathematics
{
    /// <summary>Deterministic 3D vector of <see cref="FixedPoint"/>.</summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    public struct FixedVector3 : IEquatable<FixedVector3>, IFormattable
    {
        public FixedPoint x;
        public FixedPoint y;
        public FixedPoint z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector3(FixedPoint x, FixedPoint y, FixedPoint z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector3(FixedPoint value)
        {
            x = value;
            y = value;
            z = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector3(FixedVector2 xy, FixedPoint z)
        {
            x = xy.x;
            y = xy.y;
            this.z = z;
        }

        public static FixedVector3 Zero => default;
        public static FixedVector3 One => new FixedVector3(FixedPoint.One);
        public static FixedVector3 Up => new FixedVector3(FixedPoint.Zero, FixedPoint.One, FixedPoint.Zero);
        public static FixedVector3 Down => new FixedVector3(FixedPoint.Zero, FixedPoint.MinusOne, FixedPoint.Zero);
        public static FixedVector3 Right => new FixedVector3(FixedPoint.One, FixedPoint.Zero, FixedPoint.Zero);
        public static FixedVector3 Left => new FixedVector3(FixedPoint.MinusOne, FixedPoint.Zero, FixedPoint.Zero);
        public static FixedVector3 Forward => new FixedVector3(FixedPoint.Zero, FixedPoint.Zero, FixedPoint.One);
        public static FixedVector3 Back => new FixedVector3(FixedPoint.Zero, FixedPoint.Zero, FixedPoint.MinusOne);

        public FixedPoint this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public FixedVector2 Xy => new FixedVector2(x, y);
        public FixedVector2 Xz => new FixedVector2(x, z);
        public FixedVector2 Yz => new FixedVector2(y, z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator FixedVector3(int3 value) => new FixedVector3(value.x, value.y, value.z);

        public static explicit operator FixedVector3(float3 value) => new FixedVector3((FixedPoint)value.x, (FixedPoint)value.y, (FixedPoint)value.z);

        public static explicit operator float3(FixedVector3 value) => new float3((float)value.x, (float)value.y, (float)value.z);

        public static explicit operator FixedVector3(UnityEngine.Vector3 value) => new FixedVector3((FixedPoint)value.x, (FixedPoint)value.y, (FixedPoint)value.z);

        public static explicit operator UnityEngine.Vector3(FixedVector3 value) => new UnityEngine.Vector3((float)value.x, (float)value.y, (float)value.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator +(FixedVector3 a, FixedVector3 b) => new FixedVector3(a.x + b.x, a.y + b.y, a.z + b.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator -(FixedVector3 a, FixedVector3 b) => new FixedVector3(a.x - b.x, a.y - b.y, a.z - b.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator -(FixedVector3 value) => new FixedVector3(-value.x, -value.y, -value.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator *(FixedVector3 a, FixedVector3 b) => new FixedVector3(a.x * b.x, a.y * b.y, a.z * b.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator *(FixedVector3 a, FixedPoint b) => new FixedVector3(a.x * b, a.y * b, a.z * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator *(FixedPoint a, FixedVector3 b) => new FixedVector3(a * b.x, a * b.y, a * b.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator *(FixedVector3 a, int b) => new FixedVector3(a.x * b, a.y * b, a.z * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator /(FixedVector3 a, FixedVector3 b) => new FixedVector3(a.x / b.x, a.y / b.y, a.z / b.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator /(FixedVector3 a, FixedPoint b) => new FixedVector3(a.x / b, a.y / b, a.z / b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator /(FixedVector3 a, int b) => new FixedVector3(a.x / b, a.y / b, a.z / b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedVector3 a, FixedVector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedVector3 a, FixedVector3 b) => !(a == b);

        public bool Equals(FixedVector3 other) => this == other;

        public override bool Equals(object obj) => obj is FixedVector3 other && this == other;

        public override int GetHashCode() => unchecked((x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedVector3({x.ToString(format, formatProvider)}, {y.ToString(format, formatProvider)}, {z.ToString(format, formatProvider)})";
        }
    }
}
