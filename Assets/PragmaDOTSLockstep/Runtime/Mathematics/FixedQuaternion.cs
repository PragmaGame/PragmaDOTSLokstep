using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Pragma.Lockstep.Mathematics
{
    /// <summary>Deterministic rotation quaternion of <see cref="FixedPoint"/>, laid out like <c>Unity.Mathematics.quaternion</c>.</summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    public struct FixedQuaternion : IEquatable<FixedQuaternion>, IFormattable
    {
        public FixedPoint x;
        public FixedPoint y;
        public FixedPoint z;
        public FixedPoint w;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedQuaternion(FixedPoint x, FixedPoint y, FixedPoint z, FixedPoint w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static FixedQuaternion Identity => new FixedQuaternion(FixedPoint.Zero, FixedPoint.Zero, FixedPoint.Zero, FixedPoint.One);

        public FixedVector3 Xyz => new FixedVector3(x, y, z);

        /// <summary>Rotation of <paramref name="angle"/> radians around a normalized <paramref name="axis"/>.</summary>
        public static FixedQuaternion AxisAngle(FixedVector3 axis, FixedPoint angle)
        {
            var half = FixedPoint.FromRaw(angle.rawValue >> 1);
            var s = FixedMath.Sin(half);
            var c = FixedMath.Cos(half);
            return new FixedQuaternion(axis.x * s, axis.y * s, axis.z * s, c);
        }

        public static FixedQuaternion RotateX(FixedPoint angle)
        {
            var half = FixedPoint.FromRaw(angle.rawValue >> 1);
            return new FixedQuaternion(FixedMath.Sin(half), FixedPoint.Zero, FixedPoint.Zero, FixedMath.Cos(half));
        }

        public static FixedQuaternion RotateY(FixedPoint angle)
        {
            var half = FixedPoint.FromRaw(angle.rawValue >> 1);
            return new FixedQuaternion(FixedPoint.Zero, FixedMath.Sin(half), FixedPoint.Zero, FixedMath.Cos(half));
        }

        public static FixedQuaternion RotateZ(FixedPoint angle)
        {
            var half = FixedPoint.FromRaw(angle.rawValue >> 1);
            return new FixedQuaternion(FixedPoint.Zero, FixedPoint.Zero, FixedMath.Sin(half), FixedMath.Cos(half));
        }

        /// <summary>
        /// Euler angles in radians applied in Unity's order: around z, then x, then y
        /// (the same as <c>quaternion.EulerZXY</c> and <c>Transform.eulerAngles</c>).
        /// </summary>
        public static FixedQuaternion Euler(FixedVector3 radians)
        {
            return FixedMath.Mul(RotateY(radians.y), FixedMath.Mul(RotateX(radians.x), RotateZ(radians.z)));
        }

        /// <summary>Rotation looking along <paramref name="forward"/> with the given <paramref name="up"/> hint.</summary>
        /// <remarks>Both vectors are normalized internally. Returns identity for a zero forward vector.</remarks>
        public static FixedQuaternion LookRotation(FixedVector3 forward, FixedVector3 up)
        {
            var f = FixedMath.NormalizeSafe(forward);
            if (f == FixedVector3.Zero)
            {
                return Identity;
            }
            var r = FixedMath.NormalizeSafe(FixedMath.Cross(up, f));
            if (r == FixedVector3.Zero)
            {
                // up is parallel to forward: pick any perpendicular axis.
                r = FixedMath.NormalizeSafe(FixedMath.Cross(FixedMath.Abs(f.y) < FixedPoint.FromRaw(62259) ? FixedVector3.Up : FixedVector3.Right, f));
            }
            var u = FixedMath.Cross(f, r);
            return FromBasis(r, u, f);
        }

        /// <summary>Rotation from an orthonormal basis given as the rotated x, y and z axes.</summary>
        public static FixedQuaternion FromBasis(FixedVector3 right, FixedVector3 up, FixedVector3 forward)
        {
            // Matrix columns are the basis vectors; m[row][column].
            FixedPoint m00 = right.x, m10 = right.y, m20 = right.z;
            FixedPoint m01 = up.x, m11 = up.y, m21 = up.z;
            FixedPoint m02 = forward.x, m12 = forward.y, m22 = forward.z;

            var trace = m00 + m11 + m22;
            FixedQuaternion q;
            if (trace > FixedPoint.Zero)
            {
                var s = FixedMath.Sqrt(trace + FixedPoint.One) * 2;
                q = new FixedQuaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, s / 4);
            }
            else if (m00 > m11 && m00 > m22)
            {
                var s = FixedMath.Sqrt(FixedPoint.One + m00 - m11 - m22) * 2;
                q = new FixedQuaternion(s / 4, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                var s = FixedMath.Sqrt(FixedPoint.One + m11 - m00 - m22) * 2;
                q = new FixedQuaternion((m01 + m10) / s, s / 4, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                var s = FixedMath.Sqrt(FixedPoint.One + m22 - m00 - m11) * 2;
                q = new FixedQuaternion((m02 + m20) / s, (m12 + m21) / s, s / 4, (m10 - m01) / s);
            }
            return FixedMath.NormalizeSafe(q);
        }

        public static explicit operator quaternion(FixedQuaternion value) => new quaternion((float)value.x, (float)value.y, (float)value.z, (float)value.w);

        public static explicit operator FixedQuaternion(quaternion value) => new FixedQuaternion((FixedPoint)value.value.x, (FixedPoint)value.value.y, (FixedPoint)value.value.z, (FixedPoint)value.value.w);

        public static explicit operator UnityEngine.Quaternion(FixedQuaternion value) => new UnityEngine.Quaternion((float)value.x, (float)value.y, (float)value.z, (float)value.w);

        public static explicit operator FixedQuaternion(UnityEngine.Quaternion value) => new FixedQuaternion((FixedPoint)value.x, (FixedPoint)value.y, (FixedPoint)value.z, (FixedPoint)value.w);

        /// <summary>Hamilton product: applies <paramref name="b"/> first, then <paramref name="a"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQuaternion operator *(FixedQuaternion a, FixedQuaternion b) => FixedMath.Mul(a, b);

        /// <summary>Rotates a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator *(FixedQuaternion q, FixedVector3 v) => FixedMath.Rotate(q, v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedQuaternion a, FixedQuaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedQuaternion a, FixedQuaternion b) => !(a == b);

        public bool Equals(FixedQuaternion other) => this == other;

        public override bool Equals(object obj) => obj is FixedQuaternion other && this == other;

        public override int GetHashCode() => unchecked(((x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode()) * 397 ^ w.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedQuaternion({x.ToString(format, formatProvider)}, {y.ToString(format, formatProvider)}, {z.ToString(format, formatProvider)}, {w.ToString(format, formatProvider)})";
        }
    }
}
