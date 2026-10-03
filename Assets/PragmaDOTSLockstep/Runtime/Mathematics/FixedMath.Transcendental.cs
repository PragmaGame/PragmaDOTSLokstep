using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Pragma.Lockstep.Mathematics
{
    public static partial class FixedMath
    {
        // Internal fixed-point formats: Q30 for polynomial evaluation, Q32 for angle range reduction.
        private const int Q30 = 30;
        private const long ONE_Q30 = 1L << Q30;
        private const long PI_Q30 = 3373259426;
        private const long PIHALF_Q30 = 1686629713;
        private const long PI_6_Q30 = 562209904;
        private const long SQRT3_Q30 = 1859775393;
        private const long TAN15_Q30 = 287708255;
        private const long LN2_Q30 = 744261118;
        private const long LOG2E_Q30 = 1549082005;
        private const long INV_LN10_Q30 = 466320149;
        private const long TAU_Q32 = 26986075409;
        private const long PIHALF_Q32 = 6746518852;
        // Shifting a raw Q16 angle to Q32 is safe below this magnitude (about 2^31 radians).
        private const long ANGLE_SHIFT_LIMIT = 1L << (63 - 16);

        /// <summary>Square root, rounded to the nearest step. Negative input returns zero.</summary>
        public static FixedPoint Sqrt(FixedPoint x)
        {
            var raw = x.rawValue;
            if (raw <= 0)
            {
                return FixedPoint.Zero;
            }
            var value = (ulong)raw;
            // sqrt(raw * 2^16) is the raw result; the shift is safe below 2^47.
            if (value < 1UL << 47)
            {
                return FixedPoint.FromRaw((long)SqrtRounded(value << FixedPoint.FRACTIONAL_BITS));
            }
            // Values above 2^31 lose the 8 lowest bits, which is far below their own precision needs.
            return FixedPoint.FromRaw((long)SqrtRounded(value) << (FixedPoint.FRACTIONAL_BITS / 2));
        }

        /// <summary>1 / sqrt(x). Zero or negative input saturates.</summary>
        public static FixedPoint Rsqrt(FixedPoint x) => FixedPoint.One / Sqrt(x);

        public static FixedPoint Sin(FixedPoint x) => FixedPoint.FromRaw(RoundQ30ToRaw(SinQ30FromQ32(ReduceAngleQ32(x.rawValue))));

        public static FixedPoint Cos(FixedPoint x) => FixedPoint.FromRaw(RoundQ30ToRaw(SinQ30FromQ32(ReduceAngleQ32(x.rawValue, PIHALF_Q32))));

        public static void SinCos(FixedPoint x, out FixedPoint s, out FixedPoint c)
        {
            s = Sin(x);
            c = Cos(x);
        }

        /// <summary>Tangent. Saturates where the cosine is zero.</summary>
        public static FixedPoint Tan(FixedPoint x)
        {
            var s = SinQ30FromQ32(ReduceAngleQ32(x.rawValue));
            var c = SinQ30FromQ32(ReduceAngleQ32(x.rawValue, PIHALF_Q32));
            if (c == 0)
            {
                return FixedPoint.FromRaw(SaturateSign(s));
            }
            // Both are Q30 with |value| <= 1, so the shifted numerator stays below 2^47.
            return FixedPoint.FromRaw(DivideRounded(s << FixedPoint.FRACTIONAL_BITS, c));
        }

        public static FixedPoint Atan(FixedPoint x)
        {
            var negative = x.rawValue < 0;
            var a = (long)UnsignedAbsClamped(x.rawValue);
            long angle;
            if (a <= FixedPoint.ONE_RAW)
            {
                angle = AtanUnitQ30(a << (Q30 - FixedPoint.FRACTIONAL_BITS));
            }
            else
            {
                angle = PIHALF_Q30 - AtanUnitQ30((1L << (Q30 + FixedPoint.FRACTIONAL_BITS)) / a);
            }
            var raw = RoundQ30ToRaw(angle);
            return FixedPoint.FromRaw(negative ? -raw : raw);
        }

        /// <summary>Angle of the vector (x, y) in (-π, π]. atan2(0, 0) is zero.</summary>
        public static FixedPoint Atan2(FixedPoint y, FixedPoint x)
        {
            var raw = RoundQ30ToRaw(Atan2MagnitudeQ30(y.rawValue, x.rawValue));
            return FixedPoint.FromRaw(y.rawValue < 0 ? -raw : raw);
        }

        /// <summary>Arcsine; the input is clamped to [-1, 1].</summary>
        public static FixedPoint Asin(FixedPoint x)
        {
            var s = ClampUnitToQ30(x.rawValue);
            var c = (long)SqrtRounded((ulong)(ONE_Q30 * ONE_Q30 - s * s));
            var raw = RoundQ30ToRaw(Atan2MagnitudeQ30(s, c));
            return FixedPoint.FromRaw(s < 0 ? -raw : raw);
        }

        /// <summary>Arccosine; the input is clamped to [-1, 1].</summary>
        public static FixedPoint Acos(FixedPoint x)
        {
            var c = ClampUnitToQ30(x.rawValue);
            var s = (long)SqrtRounded((ulong)(ONE_Q30 * ONE_Q30 - c * c));
            return FixedPoint.FromRaw(RoundQ30ToRaw(Atan2MagnitudeQ30(s, c)));
        }

        /// <summary>2^x. Saturates above about 2^47 and returns zero below 2^-17.</summary>
        public static FixedPoint Exp2(FixedPoint x)
        {
            var raw = x.rawValue;
            if (raw >= 47L << FixedPoint.FRACTIONAL_BITS)
            {
                return FixedPoint.MaxValue;
            }
            if (raw < -(17L << FixedPoint.FRACTIONAL_BITS))
            {
                return FixedPoint.Zero;
            }

            var integer = (int)(raw >> FixedPoint.FRACTIONAL_BITS);
            var fraction = raw & FixedPoint.FRACTION_MASK;
            // 2^f = e^(f * ln2), with f * ln2 in [0, 0.7).
            var y = ((fraction << (Q30 - FixedPoint.FRACTIONAL_BITS)) * LN2_Q30) >> Q30;
            var mantissa = ExpUnitQ30(y);

            var shift = Q30 - FixedPoint.FRACTIONAL_BITS - integer;
            if (shift > 0)
            {
                return FixedPoint.FromRaw((mantissa + (1L << (shift - 1))) >> shift);
            }
            var result = mantissa << -shift;
            return result < 0 ? FixedPoint.MaxValue : FixedPoint.FromRaw(result);
        }

        /// <summary>e^x. Saturates above about 32.6 and returns zero below about -11.8.</summary>
        public static FixedPoint Exp(FixedPoint x)
        {
            var raw = x.rawValue;
            if (raw >= 33L << FixedPoint.FRACTIONAL_BITS)
            {
                return FixedPoint.MaxValue;
            }
            if (raw <= -(13L << FixedPoint.FRACTIONAL_BITS))
            {
                return FixedPoint.Zero;
            }
            return Exp2(FixedPoint.FromRaw(MulShiftRound(raw, LOG2E_Q30, Q30)));
        }

        /// <summary>Base-2 logarithm. Zero or negative input returns <see cref="FixedPoint.MinValue"/>.</summary>
        public static FixedPoint Log2(FixedPoint x)
        {
            if (x.rawValue <= 0)
            {
                return FixedPoint.MinValue;
            }
            var integer = MantissaQ30(x.rawValue, out var mantissa);
            var log2Mantissa = (LnMantissaQ30(mantissa) * LOG2E_Q30) >> Q30;
            return FixedPoint.FromRaw(((long)integer << FixedPoint.FRACTIONAL_BITS) + RoundQ30ToRaw(log2Mantissa));
        }

        /// <summary>Natural logarithm. Zero or negative input returns <see cref="FixedPoint.MinValue"/>.</summary>
        public static FixedPoint Log(FixedPoint x)
        {
            if (x.rawValue <= 0)
            {
                return FixedPoint.MinValue;
            }
            var integer = MantissaQ30(x.rawValue, out var mantissa);
            var ln = integer * LN2_Q30 + LnMantissaQ30(mantissa);
            return FixedPoint.FromRaw(RoundQ30ToRawSigned(ln));
        }

        /// <summary>Base-10 logarithm. Zero or negative input returns <see cref="FixedPoint.MinValue"/>.</summary>
        public static FixedPoint Log10(FixedPoint x)
        {
            if (x.rawValue <= 0)
            {
                return FixedPoint.MinValue;
            }
            var integer = MantissaQ30(x.rawValue, out var mantissa);
            var ln = integer * LN2_Q30 + LnMantissaQ30(mantissa);
            return FixedPoint.FromRaw(RoundQ30ToRawSigned(MulShiftRound(ln, INV_LN10_Q30, Q30)));
        }

        /// <summary>
        /// <paramref name="x"/> raised to <paramref name="y"/>. A negative base is only defined for integer
        /// exponents; for any other exponent it returns zero, as does a zero base.
        /// </summary>
        public static FixedPoint Pow(FixedPoint x, FixedPoint y)
        {
            if (y.rawValue == 0)
            {
                return FixedPoint.One;
            }
            if (x.rawValue == 0)
            {
                return FixedPoint.Zero;
            }
            if ((y.rawValue & FixedPoint.FRACTION_MASK) == 0 && y.rawValue >= int.MinValue * FixedPoint.ONE_RAW && y.rawValue <= int.MaxValue * FixedPoint.ONE_RAW)
            {
                return Pow(x, (int)(y.rawValue >> FixedPoint.FRACTIONAL_BITS));
            }
            if (x.rawValue < 0)
            {
                return FixedPoint.Zero;
            }
            var integer = MantissaQ30(x.rawValue, out var mantissa);
            var log2X = ((long)integer << Q30) + ((LnMantissaQ30(mantissa) * LOG2E_Q30) >> Q30);
            return Exp2(FixedPoint.FromRaw(RoundQ30ToRawSigned(MulShiftRound(log2X, y.rawValue, FixedPoint.FRACTIONAL_BITS))));
        }

        /// <summary><paramref name="x"/> raised to an integer power, by repeated squaring.</summary>
        public static FixedPoint Pow(FixedPoint x, int n)
        {
            if (n == int.MinValue)
            {
                return FixedPoint.One / (Pow(x, int.MaxValue) * x);
            }
            if (n < 0)
            {
                return FixedPoint.One / Pow(x, -n);
            }
            var result = FixedPoint.One;
            var power = x;
            while (n != 0)
            {
                if ((n & 1) != 0)
                {
                    result *= power;
                }
                n >>= 1;
                if (n != 0)
                {
                    power *= power;
                }
            }
            return result;
        }

        /// <summary>Integer square root rounded to the nearest integer.</summary>
        public static ulong SqrtRounded(ulong n)
        {
            if (n == 0)
            {
                return 0;
            }
            var bit = 1UL << ((63 - math.lzcnt(n)) & ~1);
            ulong result = 0;
            while (bit != 0)
            {
                if (n >= result + bit)
                {
                    n -= result + bit;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }
                bit >>= 2;
            }
            // n now holds the remainder; round up when it exceeds result (i.e. N >= (result + 0.5)^2).
            return n > result ? result + 1 : result;
        }

        // Reduces a raw Q16 angle (plus an optional Q32 offset) into [0, 2π) with 32 fractional bits.
        private static long ReduceAngleQ32(long raw, long offsetQ32 = 0)
        {
            if (raw >= ANGLE_SHIFT_LIMIT || raw <= -ANGLE_SHIFT_LIMIT)
            {
                // Absurdly large angles: pre-reduce by whole turns in Q16 so the shift below is safe.
                raw %= 411775L * 1024;
            }
            var r = ((raw << 16) % TAU_Q32 + offsetQ32) % TAU_Q32;
            return r < 0 ? r + TAU_Q32 : r;
        }

        // sin(r) for r in [0, 2π) given in Q32; result in Q30.
        private static long SinQ30FromQ32(long rQ32)
        {
            var quadrant = (int)(rQ32 / PIHALF_Q32);
            if (quadrant > 3)
            {
                quadrant = 3;
            }
            var t = (rQ32 - quadrant * PIHALF_Q32) >> 2;
            if (t > PIHALF_Q30)
            {
                t = PIHALF_Q30;
            }
            switch (quadrant)
            {
                case 0: return SinUnitQ30(t);
                case 1: return SinUnitQ30(PIHALF_Q30 - t);
                case 2: return -SinUnitQ30(t);
                default: return -SinUnitQ30(PIHALF_Q30 - t);
            }
        }

        // sin(t) for t in [0, π/2] in Q30: Taylor series up to t^11 (error below 6e-8).
        private static long SinUnitQ30(long t)
        {
            var u = (t * t) >> Q30;
            long p = -27;                       // -1/11!
            p = ((p * u) >> Q30) + 2959;        //  1/9!
            p = ((p * u) >> Q30) - 213044;      // -1/7!
            p = ((p * u) >> Q30) + 8947849;     //  1/5!
            p = ((p * u) >> Q30) - 178956971;   // -1/3!
            p = ((p * u) >> Q30) + ONE_Q30;     //  1
            return (t * p) >> Q30;
        }

        // atan(z) for z in [0, 1] in Q30; result in Q30 radians.
        private static long AtanUnitQ30(long z)
        {
            var offset = 0L;
            if (z > TAN15_Q30)
            {
                // atan(z) = π/6 + atan((z√3 - 1) / (z + √3)) maps [tan 15°, 1] onto [-tan 15°, tan 15°].
                var numerator = ((z * SQRT3_Q30) >> Q30) - ONE_Q30;
                var denominator = z + SQRT3_Q30;
                z = (numerator << Q30) / denominator;
                offset = PI_6_Q30;
            }
            // Taylor series up to z^11 for |z| <= tan 15° (error below 3e-9).
            var v = (z * z) >> Q30;
            long p = -97612893;                 // -1/11
            p = ((p * v) >> Q30) + 119304647;   //  1/9
            p = ((p * v) >> Q30) - 153391689;   // -1/7
            p = ((p * v) >> Q30) + 214748365;   //  1/5
            p = ((p * v) >> Q30) - 357913941;   // -1/3
            p = ((p * v) >> Q30) + ONE_Q30;     //  1
            return ((z * p) >> Q30) + offset;
        }

        // |atan2(y, x)| in Q30 for raw values of any (common) scale; the caller applies the sign of y.
        private static long Atan2MagnitudeQ30(long y, long x)
        {
            var ax = UnsignedAbsClamped(x);
            var ay = UnsignedAbsClamped(y);
            if (ax == 0 && ay == 0)
            {
                return 0;
            }

            long angle;
            if (ax >= ay)
            {
                ScaleBelow33Bits(ref ax, ref ay);
                angle = AtanUnitQ30((long)((ay << Q30) / ax));
            }
            else
            {
                ScaleBelow33Bits(ref ay, ref ax);
                angle = PIHALF_Q30 - AtanUnitQ30((long)((ax << Q30) / ay));
            }
            return x < 0 ? PI_Q30 - angle : angle;
        }

        // Shifts both values right until the larger one fits in 33 bits, so (smaller << 30) cannot overflow.
        private static void ScaleBelow33Bits(ref ulong larger, ref ulong smaller)
        {
            var bits = 64 - math.lzcnt(larger);
            if (bits > 33)
            {
                larger >>= bits - 33;
                smaller >>= bits - 33;
            }
        }

        // e^y for y in [0, ln 2) in Q30: Taylor series up to y^8 (error below 2e-6 relative).
        private static long ExpUnitQ30(long y)
        {
            long p = 26631;                     // 1/8!
            p = ((p * y) >> Q30) + 213044;      // 1/7!
            p = ((p * y) >> Q30) + 1491308;     // 1/6!
            p = ((p * y) >> Q30) + 8947849;     // 1/5!
            p = ((p * y) >> Q30) + 44739243;    // 1/4!
            p = ((p * y) >> Q30) + 178956971;   // 1/3!
            p = ((p * y) >> Q30) + 536870912;   // 1/2!
            p = ((p * y) >> Q30) + ONE_Q30;     // 1/1!
            p = ((p * y) >> Q30) + ONE_Q30;     // 1/0!
            return p;
        }

        // Splits a positive raw value into 2^integer * mantissa, with the mantissa in [1, 2) as Q30.
        private static int MantissaQ30(long raw, out long mantissa)
        {
            var msb = 63 - math.lzcnt((ulong)raw);
            mantissa = msb >= Q30 ? raw >> (msb - Q30) : raw << (Q30 - msb);
            return msb - FixedPoint.FRACTIONAL_BITS;
        }

        // ln(m) for m in [1, 2) in Q30, via ln(m) = 2 atanh((m - 1) / (m + 1)) (error below 1e-7).
        private static long LnMantissaQ30(long mantissa)
        {
            var t = ((mantissa - ONE_Q30) << Q30) / (mantissa + ONE_Q30);
            var v = (t * t) >> Q30;
            long p = 97612893;                  // 1/11
            p = ((p * v) >> Q30) + 119304647;   // 1/9
            p = ((p * v) >> Q30) + 153391689;   // 1/7
            p = ((p * v) >> Q30) + 214748365;   // 1/5
            p = ((p * v) >> Q30) + 357913941;   // 1/3
            p = ((p * v) >> Q30) + ONE_Q30;     // 1
            return (2 * t * p) >> Q30;
        }

        private static long ClampUnitToQ30(long raw)
        {
            if (raw >= FixedPoint.ONE_RAW)
            {
                return ONE_Q30;
            }
            if (raw <= -FixedPoint.ONE_RAW)
            {
                return -ONE_Q30;
            }
            return raw << (Q30 - FixedPoint.FRACTIONAL_BITS);
        }

        // Rounds a non-negative or negative Q30 value to raw Q16, rounding the magnitude so results are symmetric.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long RoundQ30ToRaw(long q30)
        {
            const int shift = Q30 - FixedPoint.FRACTIONAL_BITS;
            const long half = 1L << (shift - 1);
            return q30 >= 0 ? (q30 + half) >> shift : -((-q30 + half) >> shift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long RoundQ30ToRawSigned(long q30) => RoundQ30ToRaw(q30);

        private static long DivideRounded(long numerator, long denominator)
        {
            var negative = (numerator < 0) ^ (denominator < 0);
            var n = UnsignedAbs(numerator);
            var d = UnsignedAbs(denominator);
            var q = (long)((n + d / 2) / d);
            return negative ? -q : q;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong UnsignedAbsClamped(long value) => value < 0 ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
    }
}
