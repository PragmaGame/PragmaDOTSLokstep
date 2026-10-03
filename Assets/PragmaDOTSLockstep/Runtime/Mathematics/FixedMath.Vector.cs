using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Pragma.Lockstep.Mathematics
{
    public static partial class FixedMath
    {
        // ---------- FixedVector2 ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Abs(FixedVector2 v) => new FixedVector2(Abs(v.x), Abs(v.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Min(FixedVector2 a, FixedVector2 b) => new FixedVector2(Min(a.x, b.x), Min(a.y, b.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Max(FixedVector2 a, FixedVector2 b) => new FixedVector2(Max(a.x, b.x), Max(a.y, b.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Clamp(FixedVector2 v, FixedVector2 a, FixedVector2 b) => Max(a, Min(b, v));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Lerp(FixedVector2 a, FixedVector2 b, FixedPoint t) => a + (b - a) * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Floor(FixedVector2 v) => new FixedVector2(Floor(v.x), Floor(v.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Round(FixedVector2 v) => new FixedVector2(Round(v.x), Round(v.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Dot(FixedVector2 a, FixedVector2 b) => a.x * b.x + a.y * b.y;

        /// <summary>The z component of the 3D cross product; positive when b is counter-clockwise from a.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Cross(FixedVector2 a, FixedVector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Squared length. Overflows for components above about 46000; prefer <see cref="Length(FixedVector2)"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint LengthSquared(FixedVector2 v) => Dot(v, v);

        /// <summary>Length computed on raw values with automatic scaling, so it does not overflow.</summary>
        public static FixedPoint Length(FixedVector2 v) => FixedPoint.FromRaw(LengthRaw(v.x.rawValue, v.y.rawValue, 0));

        public static FixedPoint Distance(FixedVector2 a, FixedVector2 b) => Length(b - a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint DistanceSquared(FixedVector2 a, FixedVector2 b) => LengthSquared(b - a);

        /// <summary>Unit vector; zero for a zero vector.</summary>
        public static FixedVector2 Normalize(FixedVector2 v)
        {
            var len = Length(v);
            return len.rawValue == 0 ? FixedVector2.Zero : new FixedVector2(v.x / len, v.y / len);
        }

        /// <summary>Unit vector, or <paramref name="defaultValue"/> when the vector is zero.</summary>
        public static FixedVector2 NormalizeSafe(FixedVector2 v, FixedVector2 defaultValue = default)
        {
            var len = Length(v);
            return len.rawValue == 0 ? defaultValue : new FixedVector2(v.x / len, v.y / len);
        }

        /// <summary>The vector clamped to a maximum length.</summary>
        public static FixedVector2 ClampLength(FixedVector2 v, FixedPoint maxLength)
        {
            var len = Length(v);
            return len > maxLength && len.rawValue != 0 ? v * (maxLength / len) : v;
        }

        public static FixedVector2 MoveTowards(FixedVector2 current, FixedVector2 target, FixedPoint maxDistance)
        {
            var delta = target - current;
            var len = Length(delta);
            if (len <= maxDistance || len.rawValue == 0)
            {
                return target;
            }
            return current + delta * (maxDistance / len);
        }

        /// <summary>Rotates a vector counter-clockwise by <paramref name="angle"/> radians.</summary>
        public static FixedVector2 Rotate(FixedVector2 v, FixedPoint angle)
        {
            var s = Sin(angle);
            var c = Cos(angle);
            return new FixedVector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>The vector rotated by 90 degrees counter-clockwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector2 Perpendicular(FixedVector2 v) => new FixedVector2(-v.y, v.x);

        public static FixedVector2 Reflect(FixedVector2 v, FixedVector2 normal) => v - normal * (2 * Dot(v, normal));

        /// <summary>Unit vector at <paramref name="angle"/> radians from the x axis.</summary>
        public static FixedVector2 Direction(FixedPoint angle) => new FixedVector2(Cos(angle), Sin(angle));

        /// <summary>Angle of the vector in radians, in (-π, π].</summary>
        public static FixedPoint Angle(FixedVector2 v) => Atan2(v.y, v.x);

        // ---------- FixedVector3 ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Abs(FixedVector3 v) => new FixedVector3(Abs(v.x), Abs(v.y), Abs(v.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Min(FixedVector3 a, FixedVector3 b) => new FixedVector3(Min(a.x, b.x), Min(a.y, b.y), Min(a.z, b.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Max(FixedVector3 a, FixedVector3 b) => new FixedVector3(Max(a.x, b.x), Max(a.y, b.y), Max(a.z, b.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Clamp(FixedVector3 v, FixedVector3 a, FixedVector3 b) => Max(a, Min(b, v));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Lerp(FixedVector3 a, FixedVector3 b, FixedPoint t) => a + (b - a) * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Floor(FixedVector3 v) => new FixedVector3(Floor(v.x), Floor(v.y), Floor(v.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Round(FixedVector3 v) => new FixedVector3(Round(v.x), Round(v.y), Round(v.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Dot(FixedVector3 a, FixedVector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Cross(FixedVector3 a, FixedVector3 b) => new FixedVector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        /// <summary>Squared length. Overflows for components above about 46000; prefer <see cref="Length(FixedVector3)"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint LengthSquared(FixedVector3 v) => Dot(v, v);

        /// <summary>Length computed on raw values with automatic scaling, so it does not overflow.</summary>
        public static FixedPoint Length(FixedVector3 v) => FixedPoint.FromRaw(LengthRaw(v.x.rawValue, v.y.rawValue, v.z.rawValue));

        public static FixedPoint Distance(FixedVector3 a, FixedVector3 b) => Length(b - a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint DistanceSquared(FixedVector3 a, FixedVector3 b) => LengthSquared(b - a);

        /// <summary>Unit vector; zero for a zero vector.</summary>
        public static FixedVector3 Normalize(FixedVector3 v)
        {
            var len = Length(v);
            return len.rawValue == 0 ? FixedVector3.Zero : new FixedVector3(v.x / len, v.y / len, v.z / len);
        }

        /// <summary>Unit vector, or <paramref name="defaultValue"/> when the vector is zero.</summary>
        public static FixedVector3 NormalizeSafe(FixedVector3 v, FixedVector3 defaultValue = default)
        {
            var len = Length(v);
            return len.rawValue == 0 ? defaultValue : new FixedVector3(v.x / len, v.y / len, v.z / len);
        }

        /// <summary>The vector clamped to a maximum length.</summary>
        public static FixedVector3 ClampLength(FixedVector3 v, FixedPoint maxLength)
        {
            var len = Length(v);
            return len > maxLength && len.rawValue != 0 ? v * (maxLength / len) : v;
        }

        public static FixedVector3 MoveTowards(FixedVector3 current, FixedVector3 target, FixedPoint maxDistance)
        {
            var delta = target - current;
            var len = Length(delta);
            if (len <= maxDistance || len.rawValue == 0)
            {
                return target;
            }
            return current + delta * (maxDistance / len);
        }

        public static FixedVector3 Reflect(FixedVector3 v, FixedVector3 normal) => v - normal * (2 * Dot(v, normal));

        /// <summary>Projection of <paramref name="v"/> onto <paramref name="onto"/>; zero when onto is zero.</summary>
        public static FixedVector3 Project(FixedVector3 v, FixedVector3 onto)
        {
            var denominator = Dot(onto, onto);
            return denominator.rawValue == 0 ? FixedVector3.Zero : onto * (Dot(v, onto) / denominator);
        }

        public static FixedVector3 ProjectOnPlane(FixedVector3 v, FixedVector3 planeNormal) => v - Project(v, planeNormal);

        /// <summary>Unsigned angle between two vectors in radians, in [0, π].</summary>
        public static FixedPoint Angle(FixedVector3 a, FixedVector3 b) => Atan2(Length(Cross(a, b)), Dot(a, b));

        // ---------- FixedQuaternion ----------

        /// <summary>Hamilton product: applies <paramref name="b"/> first, then <paramref name="a"/>.</summary>
        public static FixedQuaternion Mul(FixedQuaternion a, FixedQuaternion b)
        {
            return new FixedQuaternion(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        /// <summary>Rotates a vector by a unit quaternion.</summary>
        public static FixedVector3 Rotate(FixedQuaternion q, FixedVector3 v)
        {
            var u = q.Xyz;
            var t = Cross(u, v) * 2;
            return v + t * q.w + Cross(u, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedQuaternion Conjugate(FixedQuaternion q) => new FixedQuaternion(-q.x, -q.y, -q.z, q.w);

        /// <summary>Inverse rotation. Equal to <see cref="Conjugate"/> for unit quaternions.</summary>
        public static FixedQuaternion Inverse(FixedQuaternion q)
        {
            var lengthSq = Dot(q, q);
            if (lengthSq.rawValue == 0)
            {
                return FixedQuaternion.Identity;
            }
            var c = Conjugate(q);
            return new FixedQuaternion(c.x / lengthSq, c.y / lengthSq, c.z / lengthSq, c.w / lengthSq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Dot(FixedQuaternion a, FixedQuaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        public static FixedPoint Length(FixedQuaternion q)
        {
            // Components of a rotation are in [-1, 1]: raw squares fit easily, no scaling needed.
            var sum = (ulong)(q.x.rawValue * q.x.rawValue + q.y.rawValue * q.y.rawValue + q.z.rawValue * q.z.rawValue + q.w.rawValue * q.w.rawValue);
            return FixedPoint.FromRaw((long)SqrtRounded(sum));
        }

        public static FixedQuaternion Normalize(FixedQuaternion q)
        {
            var len = Length(q);
            return len.rawValue == 0 ? FixedQuaternion.Identity : new FixedQuaternion(q.x / len, q.y / len, q.z / len, q.w / len);
        }

        /// <summary>Unit quaternion, or identity when the input is zero.</summary>
        public static FixedQuaternion NormalizeSafe(FixedQuaternion q) => Normalize(q);

        /// <summary>Normalized linear interpolation along the shortest path. Cheap and good for small angles.</summary>
        public static FixedQuaternion Nlerp(FixedQuaternion a, FixedQuaternion b, FixedPoint t)
        {
            if (Dot(a, b).rawValue < 0)
            {
                b = new FixedQuaternion(-b.x, -b.y, -b.z, -b.w);
            }
            return Normalize(new FixedQuaternion(
                Lerp(a.x, b.x, t), Lerp(a.y, b.y, t), Lerp(a.z, b.z, t), Lerp(a.w, b.w, t)));
        }

        /// <summary>Spherical linear interpolation along the shortest path.</summary>
        public static FixedQuaternion Slerp(FixedQuaternion a, FixedQuaternion b, FixedPoint t)
        {
            var cosAngle = Dot(a, b);
            if (cosAngle.rawValue < 0)
            {
                cosAngle = -cosAngle;
                b = new FixedQuaternion(-b.x, -b.y, -b.z, -b.w);
            }
            // Close quaternions: the sine below would be tiny, nlerp is accurate there.
            if (cosAngle.rawValue > 65470)
            {
                return Nlerp(a, b, t);
            }
            var angle = Acos(cosAngle);
            var sinAngle = Sin(angle);
            var wa = Sin((FixedPoint.One - t) * angle) / sinAngle;
            var wb = Sin(t * angle) / sinAngle;
            return Normalize(new FixedQuaternion(
                a.x * wa + b.x * wb, a.y * wa + b.y * wb, a.z * wa + b.z * wb, a.w * wa + b.w * wb));
        }

        /// <summary>Angle in radians between two rotations, in [0, π].</summary>
        public static FixedPoint Angle(FixedQuaternion a, FixedQuaternion b)
        {
            var d = Abs(Dot(a, b));
            return Acos(Min(d, FixedPoint.One)) * 2;
        }

        /// <summary>Rotates from <paramref name="from"/> toward <paramref name="to"/> by at most <paramref name="maxRadians"/>.</summary>
        public static FixedQuaternion RotateTowards(FixedQuaternion from, FixedQuaternion to, FixedPoint maxRadians)
        {
            var total = Angle(from, to);
            if (total.rawValue == 0 || total <= maxRadians)
            {
                return to;
            }
            return Slerp(from, to, maxRadians / total);
        }

        public static FixedVector3 Forward(FixedQuaternion q) => Rotate(q, FixedVector3.Forward);

        public static FixedVector3 Up(FixedQuaternion q) => Rotate(q, FixedVector3.Up);

        public static FixedVector3 Right(FixedQuaternion q) => Rotate(q, FixedVector3.Right);

        // sqrt(x² + y² + z²) on raw values. Scaling keeps the squares below 2^62, so it never overflows.
        private static long LengthRaw(long x, long y, long z)
        {
            var ax = UnsignedAbs(x);
            var ay = UnsignedAbs(y);
            var az = UnsignedAbs(z);
            var largest = math.max(ax, math.max(ay, az));
            if (largest == 0)
            {
                return 0;
            }
            var shift = 0;
            var bits = 64 - math.lzcnt(largest);
            if (bits > 30)
            {
                shift = bits - 30;
                ax >>= shift;
                ay >>= shift;
                az >>= shift;
            }
            var length = (long)SqrtRounded(ax * ax + ay * ay + az * az);
            return shift == 0 ? length : length << shift;
        }
    }
}
