using System;
using System.Runtime.CompilerServices;

namespace Pragma.Lockstep.Mathematics
{
    /// <summary>
    /// Deterministic random number generator (PCG32, XSH-RR variant) with 128 bits of state.
    /// </summary>
    /// <remarks>
    /// It is a plain value type: keep it inside simulation state (a component or singleton) so it is hashed, recorded
    /// and identical on every client. Copying the struct copies the sequence, so always write it back after use.
    /// </remarks>
    [Serializable]
    public struct FixedRandom : IEquatable<FixedRandom>
    {
        private const ulong MULTIPLIER = 6364136223846793005UL;
        private const ulong DEFAULT_SEQUENCE = 0xDA3E39CB94B95BDBUL;

        public ulong state;
        // Always odd.
        public ulong increment;

        /// <summary>Creates a generator. Different <paramref name="sequence"/> values give independent streams.</summary>
        public FixedRandom(ulong seed, ulong sequence = DEFAULT_SEQUENCE)
        {
            state = 0;
            increment = (sequence << 1) | 1UL;
            NextUInt();
            state += SplitMix64(seed);
            NextUInt();
        }

        /// <summary>
        /// A generator derived from a seed and an index, for example an entity's own stream:
        /// <c>FixedRandom.CreateFromIndex(sessionSeed, lockstepEntityId.value)</c>. Never use <c>Entity.Index</c>: it differs
        /// between clients.
        /// </summary>
        public static FixedRandom CreateFromIndex(ulong seed, uint index)
        {
            return new FixedRandom(SplitMix64(seed ^ ((ulong)index * 0x9E3779B97F4A7C15UL)), index);
        }

        /// <summary>Uniform 32-bit value.</summary>
        public uint NextUInt()
        {
            var old = state;
            state = unchecked(old * MULTIPLIER + increment);
            var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            var rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
        }

        /// <summary>Uniform value in [0, <paramref name="max"/>), without modulo bias. Zero when max is zero.</summary>
        public uint NextUInt(uint max)
        {
            if (max == 0)
            {
                return 0;
            }
            // Lemire's multiply-shift with rejection.
            var m = (ulong)NextUInt() * max;
            var low = (uint)m;
            if (low < max)
            {
                var threshold = (0u - max) % max;
                while (low < threshold)
                {
                    m = (ulong)NextUInt() * max;
                    low = (uint)m;
                }
            }
            return (uint)(m >> 32);
        }

        /// <summary>Uniform value in [<paramref name="min"/>, <paramref name="max"/>). Returns min when the range is empty.</summary>
        public uint NextUInt(uint min, uint max) => max <= min ? min : min + NextUInt(max - min);

        /// <summary>Uniform value in [0, <paramref name="max"/>). Returns 0 when max is not positive.</summary>
        public int NextInt(int max) => max <= 0 ? 0 : (int)NextUInt((uint)max);

        /// <summary>Uniform value in [<paramref name="min"/>, <paramref name="max"/>). Returns min when the range is empty.</summary>
        public int NextInt(int min, int max)
        {
            if (max <= min)
            {
                return min;
            }
            return (int)((long)min + NextUInt((uint)((long)max - min)));
        }

        public bool NextBool() => (NextUInt() & 1u) != 0;

        /// <summary>True with the given probability in [0, 1].</summary>
        public bool NextChance(FixedPoint probability) => NextFixedPoint().rawValue < probability.rawValue;

        /// <summary>Uniform value in [0, 1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedPoint NextFixedPoint() => FixedPoint.FromRaw(NextUInt() >> 16);

        /// <summary>Uniform value in [<paramref name="min"/>, <paramref name="max"/>).</summary>
        public FixedPoint NextFixedPoint(FixedPoint min, FixedPoint max)
        {
            if (max.rawValue <= min.rawValue)
            {
                return min;
            }
            var range = (ulong)(max.rawValue - min.rawValue);
            // Ranges below 65536 units: multiply-shift, the product stays below 2^64.
            if (range <= uint.MaxValue)
            {
                return FixedPoint.FromRaw(min.rawValue + (long)(((ulong)NextUInt() * range) >> 32));
            }
            var r = ((ulong)NextUInt() << 32) | NextUInt();
            return FixedPoint.FromRaw(min.rawValue + (long)(r % range));
        }

        public FixedVector2 NextVector2(FixedVector2 min, FixedVector2 max) => new FixedVector2(NextFixedPoint(min.x, max.x), NextFixedPoint(min.y, max.y));

        public FixedVector3 NextVector3(FixedVector3 min, FixedVector3 max) => new FixedVector3(NextFixedPoint(min.x, max.x), NextFixedPoint(min.y, max.y), NextFixedPoint(min.z, max.z));

        /// <summary>Uniform angle in [0, 2π).</summary>
        public FixedPoint NextAngle() => FixedPoint.FromRaw((long)NextUInt((uint)FixedMath.TwoPi.rawValue));

        /// <summary>Uniformly distributed unit vector.</summary>
        public FixedVector2 NextDirection2() => FixedMath.Direction(NextAngle());

        /// <summary>Uniformly distributed unit vector on the sphere.</summary>
        public FixedVector3 NextDirection3()
        {
            var z = NextFixedPoint(FixedPoint.MinusOne, FixedPoint.One);
            var r = FixedMath.Sqrt(FixedPoint.One - z * z);
            var d = FixedMath.Direction(NextAngle());
            return new FixedVector3(d.x * r, d.y * r, z);
        }

        /// <summary>Uniformly distributed point inside the unit circle.</summary>
        public FixedVector2 NextInsideUnitCircle() => NextDirection2() * FixedMath.Sqrt(NextFixedPoint());

        /// <summary>Uniformly distributed rotation.</summary>
        public FixedQuaternion NextRotation()
        {
            // Shoemake's method.
            var u1 = NextFixedPoint();
            var a = NextAngle();
            var b = NextAngle();
            var s1 = FixedMath.Sqrt(FixedPoint.One - u1);
            var s2 = FixedMath.Sqrt(u1);
            return FixedMath.Normalize(new FixedQuaternion(s1 * FixedMath.Sin(a), s1 * FixedMath.Cos(a), s2 * FixedMath.Sin(b), s2 * FixedMath.Cos(b)));
        }

        public bool Equals(FixedRandom other) => state == other.state && increment == other.increment;

        public override bool Equals(object obj) => obj is FixedRandom other && Equals(other);

        public override int GetHashCode() => unchecked(state.GetHashCode() * 397 ^ increment.GetHashCode());

        /// <summary>A strong 64-bit mixer, used to turn arbitrary seeds into well distributed state.</summary>
        public static ulong SplitMix64(ulong value)
        {
            unchecked
            {
                value += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }
}
