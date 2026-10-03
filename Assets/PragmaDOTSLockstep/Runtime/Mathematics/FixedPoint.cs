using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pragma.Lockstep.Mathematics
{
    /// <summary>
    /// Deterministic signed fixed-point number: a 64-bit raw value with 16 fractional bits (Q47.16).
    /// </summary>
    /// <remarks>
    /// <para>Every operation is integer-only, so results are bit-identical on every platform and in Mono,
    /// IL2CPP and Burst. Use it for all simulation state instead of <c>float</c>.</para>
    /// <para>The step is 1/65536 (about 0.000015) and the range is about ±1.4e14. Multiplication keeps a
    /// 64-bit intermediate, so keep the magnitude of a product below about 2e9.</para>
    /// <para>Conversions from <c>float</c> and <c>double</c> are explicit on purpose: the conversion itself is
    /// deterministic, but the float it reads usually is not. Convert authoring data once, at bake time.</para>
    /// </remarks>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    public struct FixedPoint : IEquatable<FixedPoint>, IComparable<FixedPoint>, IComparable, IFormattable
    {
        public const int FRACTIONAL_BITS = 16;
        public const long ONE_RAW = 1L << FRACTIONAL_BITS;
        public const long HALF_RAW = ONE_RAW >> 1;
        public const long FRACTION_MASK = ONE_RAW - 1;
        public const long MAX_RAW = long.MaxValue;
        // Symmetric with MAX_RAW so that negation and abs never overflow.
        public const long MIN_RAW = -long.MaxValue;

        /// <summary>The raw two's complement value; <see cref="ONE_RAW"/> is 1.0.</summary>
        public long rawValue;

        public static FixedPoint Zero => default;
        public static FixedPoint One => FromRaw(ONE_RAW);
        public static FixedPoint Half => FromRaw(HALF_RAW);
        public static FixedPoint Two => FromRaw(ONE_RAW << 1);
        public static FixedPoint MinusOne => FromRaw(-ONE_RAW);
        /// <summary>The smallest positive value, 1/65536.</summary>
        public static FixedPoint Epsilon => FromRaw(1);
        public static FixedPoint MaxValue => FromRaw(MAX_RAW);
        public static FixedPoint MinValue => FromRaw(MIN_RAW);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint FromRaw(long rawValue)
        {
            FixedPoint result;
            result.rawValue = rawValue;
            return result;
        }

        /// <summary>Exact <paramref name="numerator"/> / <paramref name="denominator"/>, truncated toward zero.</summary>
        /// <remarks>The deterministic way to write constants: <c>FixedPoint.FromFraction(3, 10)</c> is 0.3.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint FromFraction(long numerator, long denominator)
        {
            return FromRaw(FixedMath.DivideRaw(numerator, denominator));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator FixedPoint(int value) => FromRaw((long)value << FRACTIONAL_BITS);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator FixedPoint(long value) => FromRaw(value << FRACTIONAL_BITS);

        public static explicit operator FixedPoint(float value) => FromDouble(value);

        public static explicit operator FixedPoint(double value) => FromDouble(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator float(FixedPoint value) => (float)(value.rawValue * (1.0 / ONE_RAW));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator double(FixedPoint value) => value.rawValue * (1.0 / ONE_RAW);

        /// <summary>Truncates toward zero, like a C# float to int cast.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator int(FixedPoint value) => (int)TruncateRaw(value.rawValue);

        /// <summary>Truncates toward zero, like a C# float to long cast.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator long(FixedPoint value) => TruncateRaw(value.rawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator +(FixedPoint value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator -(FixedPoint value) => FromRaw(-value.rawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator +(FixedPoint a, FixedPoint b) => FromRaw(a.rawValue + b.rawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator -(FixedPoint a, FixedPoint b) => FromRaw(a.rawValue - b.rawValue);

        /// <summary>Product rounded to the nearest step (half up).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator *(FixedPoint a, FixedPoint b) => FromRaw((a.rawValue * b.rawValue + HALF_RAW) >> FRACTIONAL_BITS);

        /// <summary>Exact product with an integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator *(FixedPoint a, int b) => FromRaw(a.rawValue * b);

        /// <summary>Exact product with an integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator *(int a, FixedPoint b) => FromRaw(a * b.rawValue);

        /// <summary>Quotient truncated toward zero. Division by zero saturates instead of throwing.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator /(FixedPoint a, FixedPoint b) => FromRaw(FixedMath.DivideRaw(a.rawValue, b.rawValue));

        /// <summary>Quotient by an integer, truncated toward zero. Division by zero saturates instead of throwing.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator /(FixedPoint a, int b)
        {
            if (b == 0)
            {
                return FromRaw(FixedMath.SaturateSign(a.rawValue));
            }
            if (b == -1)
            {
                return FromRaw(-a.rawValue);
            }
            return FromRaw(a.rawValue / b);
        }

        /// <summary>Remainder with the sign of the dividend, like C# <c>%</c>. A zero divisor returns zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator %(FixedPoint a, FixedPoint b)
        {
            if (b.rawValue == 0 || b.rawValue == -1)
            {
                return Zero;
            }
            return FromRaw(a.rawValue % b.rawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator ++(FixedPoint value) => FromRaw(value.rawValue + ONE_RAW);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint operator --(FixedPoint value) => FromRaw(value.rawValue - ONE_RAW);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedPoint a, FixedPoint b) => a.rawValue == b.rawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedPoint a, FixedPoint b) => a.rawValue != b.rawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FixedPoint a, FixedPoint b) => a.rawValue < b.rawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FixedPoint a, FixedPoint b) => a.rawValue > b.rawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FixedPoint a, FixedPoint b) => a.rawValue <= b.rawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FixedPoint a, FixedPoint b) => a.rawValue >= b.rawValue;

        public bool Equals(FixedPoint other) => rawValue == other.rawValue;

        public override bool Equals(object obj) => obj is FixedPoint other && rawValue == other.rawValue;

        public override int GetHashCode() => rawValue.GetHashCode();

        public int CompareTo(FixedPoint other) => rawValue.CompareTo(other.rawValue);

        public int CompareTo(object obj)
        {
            if (obj is FixedPoint other)
            {
                return rawValue.CompareTo(other.rawValue);
            }
            throw new ArgumentException($"Object must be of type {nameof(FixedPoint)}.", nameof(obj));
        }

        /// <summary>Formats through <c>double</c>. For display only; never feed the text back into the simulation.</summary>
        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format) => ToString(format, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return ((double)this).ToString(format ?? "0.######", formatProvider ?? CultureInfo.InvariantCulture);
        }

        /// <summary>Parses a decimal number such as <c>-12.375</c> with integer arithmetic only.</summary>
        /// <exception cref="FormatException">The text is not a plain decimal number or is out of range.</exception>
        public static FixedPoint Parse(string text)
        {
            if (!TryParse(text, out var result))
            {
                throw new FormatException($"'{text}' is not a valid {nameof(FixedPoint)} value.");
            }
            return result;
        }

        /// <summary>
        /// Parses a decimal number such as <c>-12.375</c> with integer arithmetic only, so the result is the same
        /// everywhere. Accepts '.' or ',' as the separator; no exponent, no thousands separators.
        /// </summary>
        public static bool TryParse(string text, out FixedPoint result)
        {
            result = Zero;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var start = 0;
            var end = text.Length;
            while (start < end && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
            if (start == end)
            {
                return false;
            }

            var negative = false;
            if (text[start] == '-' || text[start] == '+')
            {
                negative = text[start] == '-';
                start++;
            }

            const long maxInteger = MAX_RAW >> FRACTIONAL_BITS;
            const long maxDenominator = 1_000_000_000_000L;
            long integer = 0;
            var anyDigit = false;
            var i = start;
            for (; i < end && text[i] >= '0' && text[i] <= '9'; i++)
            {
                integer = integer * 10 + (text[i] - '0');
                if (integer > maxInteger)
                {
                    return false;
                }
                anyDigit = true;
            }

            long fractionNumerator = 0;
            long fractionDenominator = 1;
            if (i < end && (text[i] == '.' || text[i] == ','))
            {
                for (i++; i < end && text[i] >= '0' && text[i] <= '9'; i++)
                {
                    // Digits past the 12th cannot change a 16-bit fraction, they are ignored.
                    if (fractionDenominator < maxDenominator)
                    {
                        fractionNumerator = fractionNumerator * 10 + (text[i] - '0');
                        fractionDenominator *= 10;
                    }
                    anyDigit = true;
                }
            }

            if (i != end || !anyDigit)
            {
                return false;
            }

            // Round the fraction to the nearest step.
            var fraction = (fractionNumerator * ONE_RAW * 2 + fractionDenominator) / (fractionDenominator * 2);
            var raw = (integer << FRACTIONAL_BITS) + fraction;
            result = FromRaw(negative ? -raw : raw);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long TruncateRaw(long raw)
        {
            return raw >= 0 ? raw >> FRACTIONAL_BITS : -(-raw >> FRACTIONAL_BITS);
        }

        private static FixedPoint FromDouble(double value)
        {
            if (double.IsNaN(value))
            {
                return Zero;
            }
            var scaled = value * ONE_RAW;
            // long.MaxValue is not representable as a double; 2^63 is the first value that does not fit.
            if (scaled >= 9.2233720368547758E18)
            {
                return MaxValue;
            }
            if (scaled <= -9.2233720368547758E18)
            {
                return MinValue;
            }
            return FromRaw((long)(scaled >= 0 ? scaled + 0.5 : scaled - 0.5));
        }
    }
}
