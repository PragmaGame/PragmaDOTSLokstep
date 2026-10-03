using System.Runtime.CompilerServices;

namespace Pragma.Lockstep.Mathematics
{
    /// <summary>
    /// Deterministic math for <see cref="FixedPoint"/>, <see cref="FixedVector2"/>, <see cref="FixedVector3"/> and <see cref="FixedQuaternion"/>,
    /// shaped after <c>Mathf</c> and <c>Unity.Mathematics.math</c>.
    /// </summary>
    /// <remarks>
    /// Everything here is integer-only: transcendental functions use range reduction plus fixed-point polynomials
    /// evaluated with 30 fractional bits, so they are accurate to about one step of <see cref="FixedPoint"/> and identical on
    /// every platform. No lookup tables are needed, which keeps the code Burst-friendly.
    /// </remarks>
    public static partial class FixedMath
    {
        /// <summary>π, rounded to the nearest step.</summary>
        public static FixedPoint Pi => FixedPoint.FromRaw(205887);
        /// <summary>2π, rounded to the nearest step.</summary>
        public static FixedPoint TwoPi => FixedPoint.FromRaw(411775);
        /// <summary>π/2, rounded to the nearest step.</summary>
        public static FixedPoint HalfPi => FixedPoint.FromRaw(102944);
        public static FixedPoint E => FixedPoint.FromRaw(178145);
        public static FixedPoint Ln2 => FixedPoint.FromRaw(45426);
        public static FixedPoint Ln10 => FixedPoint.FromRaw(150902);
        public static FixedPoint Sqrt2 => FixedPoint.FromRaw(92682);
        /// <summary>Degrees per radian. Prefer <see cref="ToDegrees(FixedPoint)"/>, which is more precise.</summary>
        public static FixedPoint Rad2Deg => FixedPoint.FromRaw(3754936);
        /// <summary>Radians per degree. Prefer <see cref="ToRadians(FixedPoint)"/>, which is more precise.</summary>
        public static FixedPoint Deg2Rad => FixedPoint.FromRaw(1144);
        /// <summary>The smallest positive value, 1/65536.</summary>
        public static FixedPoint Epsilon => FixedPoint.FromRaw(1);

        // π/180 with 32 fractional bits, used by ToRadians().
        private const long TORADIANS_Q32 = 74961321;
        // 180/π with 16 fractional bits, exact enough for ToDegrees() because angles are small.
        private const long TODEGREES_Q16 = 3754936;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Abs(FixedPoint x) => FixedPoint.FromRaw(x.rawValue < 0 ? -x.rawValue : x.rawValue);

        /// <summary>-1, 0 or 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Sign(FixedPoint x) => FixedPoint.FromRaw(x.rawValue > 0 ? FixedPoint.ONE_RAW : x.rawValue < 0 ? -FixedPoint.ONE_RAW : 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Min(FixedPoint a, FixedPoint b) => a.rawValue < b.rawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Max(FixedPoint a, FixedPoint b) => a.rawValue > b.rawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Clamp(FixedPoint x, FixedPoint a, FixedPoint b) => Max(a, Min(b, x));

        /// <summary>Clamps to [0, 1].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Clamp01(FixedPoint x) => Clamp(x, FixedPoint.Zero, FixedPoint.One);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Lerp(FixedPoint a, FixedPoint b, FixedPoint t) => a + (b - a) * t;

        /// <summary>The inverse of <see cref="Lerp(FixedPoint,FixedPoint,FixedPoint)"/>: where <paramref name="x"/> lies between a and b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint InverseLerp(FixedPoint a, FixedPoint b, FixedPoint x) => (x - a) / (b - a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Remap(FixedPoint srcStart, FixedPoint srcEnd, FixedPoint dstStart, FixedPoint dstEnd, FixedPoint x) => Lerp(dstStart, dstEnd, InverseLerp(srcStart, srcEnd, x));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Floor(FixedPoint x) => FixedPoint.FromRaw(x.rawValue & ~FixedPoint.FRACTION_MASK);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Ceil(FixedPoint x) => FixedPoint.FromRaw((x.rawValue + FixedPoint.FRACTION_MASK) & ~FixedPoint.FRACTION_MASK);

        /// <summary>Rounds to the nearest integer; halves round up (toward positive infinity).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Round(FixedPoint x) => FixedPoint.FromRaw((x.rawValue + FixedPoint.HALF_RAW) & ~FixedPoint.FRACTION_MASK);

        /// <summary>Rounds toward zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Truncate(FixedPoint x) => FixedPoint.FromRaw(FixedPoint.TruncateRaw(x.rawValue) << FixedPoint.FRACTIONAL_BITS);

        /// <summary><c>x - floor(x)</c>, always in [0, 1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Frac(FixedPoint x) => FixedPoint.FromRaw(x.rawValue & FixedPoint.FRACTION_MASK);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(FixedPoint x) => (int)(x.rawValue >> FixedPoint.FRACTIONAL_BITS);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilToInt(FixedPoint x) => (int)((x.rawValue + FixedPoint.FRACTION_MASK) >> FixedPoint.FRACTIONAL_BITS);

        /// <summary>Rounds to the nearest integer; halves round up (toward positive infinity).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundToInt(FixedPoint x) => (int)((x.rawValue + FixedPoint.HALF_RAW) >> FixedPoint.FRACTIONAL_BITS);

        /// <summary>Remainder with the sign of the dividend, like C# <c>%</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Remainder(FixedPoint x, FixedPoint y) => x % y;

        /// <summary>Remainder that is always in [0, |y|), useful for wrapping.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Mod(FixedPoint x, FixedPoint y)
        {
            var r = x % y;
            return r.rawValue < 0 ? r + Abs(y) : r;
        }

        /// <summary>Wraps <paramref name="x"/> into [min, max).</summary>
        public static FixedPoint Wrap(FixedPoint x, FixedPoint min, FixedPoint max) => min + Mod(x - min, max - min);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Select(FixedPoint falseValue, FixedPoint trueValue, bool test) => test ? trueValue : falseValue;

        /// <summary>0 when <paramref name="x"/> &lt; <paramref name="threshold"/>, otherwise 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Step(FixedPoint threshold, FixedPoint x) => x.rawValue >= threshold.rawValue ? FixedPoint.One : FixedPoint.Zero;

        public static FixedPoint SmoothStep(FixedPoint a, FixedPoint b, FixedPoint x)
        {
            var t = Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
        public static FixedPoint MoveTowards(FixedPoint current, FixedPoint target, FixedPoint maxDelta)
        {
            var delta = target - current;
            if (Abs(delta).rawValue <= maxDelta.rawValue)
            {
                return target;
            }
            return current + Sign(delta) * maxDelta;
        }

        /// <summary>Degrees to radians, computed with 32 extra fractional bits.</summary>
        public static FixedPoint ToRadians(FixedPoint degrees) => FixedPoint.FromRaw(MulShiftRound(degrees.rawValue, TORADIANS_Q32, 32));

        /// <summary>Radians to degrees.</summary>
        public static FixedPoint ToDegrees(FixedPoint radians) => FixedPoint.FromRaw(MulShiftRound(radians.rawValue, TODEGREES_Q16, 16));

        /// <summary>Shortest signed difference between two angles in radians, in [-π, π).</summary>
        public static FixedPoint DeltaAngle(FixedPoint from, FixedPoint to) => Wrap(to - from, -Pi, Pi);

        /// <summary>
        /// <c>(a * b) &gt;&gt; shift</c> with a 128-bit intermediate and rounding to nearest, for 0 &lt; shift &lt; 64.
        /// Slower than the plain operators; used where the 64-bit product could overflow.
        /// </summary>
        public static long MulShiftRound(long a, long b, int shift)
        {
            var negative = (a < 0) ^ (b < 0);
            var ua = UnsignedAbs(a);
            var ub = UnsignedAbs(b);

            var aLo = ua & 0xFFFFFFFFUL;
            var aHi = ua >> 32;
            var bLo = ub & 0xFFFFFFFFUL;
            var bHi = ub >> 32;

            var loLo = aLo * bLo;
            var loHi = aLo * bHi;
            var hiLo = aHi * bLo;
            var hiHi = aHi * bHi;

            var middle = (loLo >> 32) + (loHi & 0xFFFFFFFFUL) + (hiLo & 0xFFFFFFFFUL);
            var lo = (middle << 32) | (loLo & 0xFFFFFFFFUL);
            var hi = hiHi + (loHi >> 32) + (hiLo >> 32) + (middle >> 32);

            // Add half of the last kept bit for round-to-nearest.
            var half = 1UL << (shift - 1);
            var newLo = lo + half;
            if (newLo < lo)
            {
                hi++;
            }
            lo = newLo;

            var resultHi = hi >> shift;
            var result = (lo >> shift) | (hi << (64 - shift));
            if (resultHi != 0 || result > FixedPoint.MAX_RAW)
            {
                return negative ? FixedPoint.MIN_RAW : FixedPoint.MAX_RAW;
            }
            return negative ? -(long)result : (long)result;
        }

        /// <summary>
        /// The raw quotient of two raw values: <c>(a &lt;&lt; 16) / b</c>, truncated toward zero, without overflow.
        /// Division by zero saturates to the sign of <paramref name="a"/>.
        /// </summary>
        public static long DivideRaw(long a, long b)
        {
            if (b == 0)
            {
                return SaturateSign(a);
            }

            const long fastLimit = 1L << (63 - FixedPoint.FRACTIONAL_BITS);
            if (a < fastLimit && a > -fastLimit)
            {
                return (a << FixedPoint.FRACTIONAL_BITS) / b;
            }

            // |a| is too large to shift: long division, one fractional bit at a time.
            var negative = (a < 0) ^ (b < 0);
            var ua = UnsignedAbs(a);
            var ub = UnsignedAbs(b);
            var quotient = ua / ub;
            var remainder = ua % ub;
            if (quotient > (ulong)(FixedPoint.MAX_RAW >> FixedPoint.FRACTIONAL_BITS))
            {
                return negative ? FixedPoint.MIN_RAW : FixedPoint.MAX_RAW;
            }
            for (var i = 0; i < FixedPoint.FRACTIONAL_BITS; i++)
            {
                // remainder < ub <= 2^63, so the shift cannot overflow.
                remainder <<= 1;
                quotient <<= 1;
                if (remainder >= ub)
                {
                    remainder -= ub;
                    quotient |= 1;
                }
            }
            return negative ? -(long)quotient : (long)quotient;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long SaturateSign(long raw) => raw > 0 ? FixedPoint.MAX_RAW : raw < 0 ? FixedPoint.MIN_RAW : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong UnsignedAbs(long value) => value < 0 ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
    }
}
