using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Pragma.Lockstep.Mathematics
{
    /// <summary>Deterministic 2D vector of <see cref="FixedPoint"/>.</summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    public struct FixedVector2 : IEquatable<FixedVector2>, IFormattable
    {
        public FixedPoint x;
        public FixedPoint y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector2(FixedPoint x, FixedPoint y)
        {
            this.x = x;
            this.y = y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector2(FixedPoint value)
        {
            x = value;
            y = value;
        }

        public static FixedVector2 Zero => default;
        public static FixedVector2 One => new FixedVector2(FixedPoint.One);
        public static FixedVector2 Up => new FixedVector2(FixedPoint.Zero, FixedPoint.One);
        public static FixedVector2 Down => new FixedVector2(FixedPoint.Zero, FixedPoint.MinusOne);
        public static FixedVector2 Right => new FixedVector2(FixedPoint.One, FixedPoint.Zero);
        public static FixedVector2 Left => new FixedVector2(FixedPoint.MinusOne, FixedPoint.Zero);

        public FixedPoint this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public FixedVector2 Yx => new FixedVector2(y, x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator FixedVector2(int2 value) => new FixedVector2(value.x, value.y);

        public static explicit operator FixedVector2(float2 value) => new FixedVector2((FixedPoint)value.x, (FixedPoint)value.y);

        public static explicit operator float2(FixedVector2 value) => new float2((float)value.x, (float)value.y);

        public static explicit operator FixedVector2(UnityEngine.Vector2 value) => new FixedVector2((FixedPoint)value.x, (FixedPoint)value.y);

        public static explicit operator UnityEngine.Vector2(FixedVector2 value) => new UnityEngine.Vector2((float)value.x, (float)value.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.x + b.x, a.y + b.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.x - b.x, a.y - b.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator -(FixedVector2 value) => new FixedVector2(-value.x, -value.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator *(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.x * b.x, a.y * b.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator *(FixedVector2 a, FixedPoint b) => new FixedVector2(a.x * b, a.y * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator *(FixedPoint a, FixedVector2 b) => new FixedVector2(a * b.x, a * b.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator *(FixedVector2 a, int b) => new FixedVector2(a.x * b, a.y * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator /(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.x / b.x, a.y / b.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator /(FixedVector2 a, FixedPoint b) => new FixedVector2(a.x / b, a.y / b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 operator /(FixedVector2 a, int b) => new FixedVector2(a.x / b, a.y / b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedVector2 a, FixedVector2 b) => a.x == b.x && a.y == b.y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedVector2 a, FixedVector2 b) => !(a == b);

        public bool Equals(FixedVector2 other) => this == other;

        public override bool Equals(object obj) => obj is FixedVector2 other && this == other;

        public override int GetHashCode() => unchecked(x.GetHashCode() * 397 ^ y.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedVector2({x.ToString(format, formatProvider)}, {y.ToString(format, formatProvider)})";
        }
    }
}
