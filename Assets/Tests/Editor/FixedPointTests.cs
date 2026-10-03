using System;
using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Pragma.Lockstep.Tests
{
    public class FixedPointTests
    {
        private const double STEP = 1.0 / FixedPoint.ONE_RAW;

        [Test]
        public void Arithmetic_IsExactForRepresentableValues()
        {
            var a = FixedPoint.FromFraction(3, 2);
            var b = FixedPoint.FromFraction(9, 4);

            Assert.AreEqual(FixedPoint.FromFraction(15, 4), a + b);
            Assert.AreEqual(FixedPoint.FromFraction(-3, 4), a - b);
            Assert.AreEqual(FixedPoint.FromFraction(27, 8), a * b);
            Assert.AreEqual(FixedPoint.FromFraction(2, 3), a / b);
            Assert.AreEqual(FixedPoint.FromFraction(9, 2), a * 3);
            Assert.AreEqual(FixedPoint.FromFraction(3, 4), a / 2);
            Assert.AreEqual(FixedPoint.FromFraction(3, 4), FixedPoint.FromFraction(15, 4) % FixedPoint.FromFraction(3, 2));
        }

        [Test]
        public void IntegerConversions_FollowCSharpSemantics()
        {
            Assert.AreEqual(7, (int)(FixedPoint)7);
            Assert.AreEqual(-1, (int)FixedPoint.FromFraction(-3, 2), "casts truncate toward zero");
            Assert.AreEqual(-2, FixedMath.FloorToInt(FixedPoint.FromFraction(-3, 2)));
            Assert.AreEqual(-1, FixedMath.CeilToInt(FixedPoint.FromFraction(-3, 2)));
            Assert.AreEqual(-1, FixedMath.RoundToInt(FixedPoint.FromFraction(-3, 2)), "halves round up");
            Assert.AreEqual(FixedPoint.FromFraction(1, 2), FixedMath.Frac(FixedPoint.FromFraction(-3, 2)));
            Assert.AreEqual((FixedPoint)(-1), FixedMath.Truncate(FixedPoint.FromFraction(-3, 2)));
        }

        [Test]
        public void Multiplication_RoundsToNearestStep()
        {
            Assert.AreEqual(2, (FixedPoint.FromRaw(3) * FixedPoint.Half).rawValue);
            Assert.AreEqual(-1, (FixedPoint.FromRaw(-3) * FixedPoint.Half).rawValue);
            Assert.AreEqual(1, (FixedPoint.FromRaw(1) * FixedPoint.One).rawValue);
        }

        [Test]
        public void Division_ByZero_Saturates()
        {
            Assert.AreEqual(FixedPoint.MaxValue, FixedPoint.One / FixedPoint.Zero);
            Assert.AreEqual(FixedPoint.MinValue, -FixedPoint.One / FixedPoint.Zero);
            Assert.AreEqual(FixedPoint.Zero, FixedPoint.Zero / FixedPoint.Zero);
            Assert.AreEqual(FixedPoint.MaxValue, FixedPoint.One / 0);
            Assert.AreEqual(FixedPoint.Zero, FixedPoint.One % FixedPoint.Zero);
        }

        [Test]
        public void Division_OfLargeValues_StaysExact()
        {
            var big = (FixedPoint)3_000_000_000L;
            Assert.AreEqual((FixedPoint)1_000_000_000L, big / (FixedPoint)3);
            Assert.AreEqual(FixedPoint.FromRaw(big.rawValue / 7), big / (FixedPoint)7);
            Assert.AreEqual((FixedPoint)(-1_500_000_000L), -big / (FixedPoint)2);
        }

        [Test]
        public void MulShiftRound_MatchesDecimalReference()
        {
            var random = new System.Random(5);
            for (var i = 0; i < 5000; i++)
            {
                var a = NextLong(random, 1L << 50);
                var b = NextLong(random, 1L << 40);
                var shift = random.Next(16, 48);
                var exact = (decimal)a * b / (decimal)Math.Pow(2, shift);
                var expected = decimal.Round(exact, MidpointRounding.AwayFromZero);
                if (Math.Abs(expected) > long.MaxValue)
                {
                    continue;
                }
                Assert.AreEqual((long)expected, FixedMath.MulShiftRound(a, b, shift), $"{a} * {b} >> {shift}");
            }
        }

        [Test]
        public void Parse_IsExactAndCultureIndependent()
        {
            Assert.AreEqual(FixedPoint.FromFraction(-12375, 1000), FixedPoint.Parse("-12.375"));
            Assert.AreEqual(FixedPoint.FromRaw(6554), FixedPoint.Parse("0.1"));
            Assert.AreEqual((FixedPoint)42, FixedPoint.Parse(" 42 "));
            Assert.AreEqual(FixedPoint.Parse("1.5"), FixedPoint.Parse("1,5"));
            Assert.AreEqual(FixedPoint.FromFraction(1, 2), FixedPoint.Parse(".5"));
            Assert.IsFalse(FixedPoint.TryParse("1e5", out _));
            Assert.IsFalse(FixedPoint.TryParse("abc", out _));
            Assert.IsFalse(FixedPoint.TryParse("", out _));
            Assert.IsFalse(FixedPoint.TryParse("-", out _));
        }

        [Test]
        public void FloatConversions_RoundToNearestStep()
        {
            Assert.AreEqual(FixedPoint.FromRaw(6554), (FixedPoint)0.1f);
            Assert.AreEqual(FixedPoint.FromRaw(-6554), (FixedPoint)(-0.1));
            Assert.AreEqual(1.5f, (float)FixedPoint.FromFraction(3, 2));
            Assert.AreEqual(FixedPoint.MaxValue, (FixedPoint)1e30);
            Assert.AreEqual(FixedPoint.Zero, (FixedPoint)double.NaN);
        }

        [Test]
        public void Sqrt_IsWithinOneStep()
        {
            Assert.AreEqual((FixedPoint)2, FixedMath.Sqrt(4));
            Assert.AreEqual(FixedPoint.FromFraction(3, 2), FixedMath.Sqrt(FixedPoint.FromFraction(9, 4)));
            Assert.AreEqual(FixedPoint.Zero, FixedMath.Sqrt(-4));

            for (double v = STEP; v < 2e9; v *= 1.37)
            {
                var x = (FixedPoint)v;
                var expected = Math.Sqrt((double)x);
                Assert.AreEqual(expected, (double)FixedMath.Sqrt(x), STEP, $"sqrt({v})");
            }
        }

        [Test]
        public void SinCos_AreAccurate()
        {
            for (var raw = -20L * FixedPoint.ONE_RAW; raw <= 20L * FixedPoint.ONE_RAW; raw += 1013)
            {
                var x = FixedPoint.FromRaw(raw);
                Assert.AreEqual(Math.Sin((double)x), (double)FixedMath.Sin(x), 2 * STEP, $"sin({(double)x})");
                Assert.AreEqual(Math.Cos((double)x), (double)FixedMath.Cos(x), 2 * STEP, $"cos({(double)x})");
                var cos = Math.Cos((double)x);
                if (Math.Abs(cos) > 0.1)
                {
                    var expected = Math.Tan((double)x);
                    Assert.AreEqual(expected, (double)FixedMath.Tan(x), Math.Max(3 * STEP, Math.Abs(expected) * 2e-4), $"tan({(double)x})");
                }
            }
        }

        [Test]
        public void SinCos_AreSymmetric()
        {
            for (var raw = 0L; raw <= 10L * FixedPoint.ONE_RAW; raw += 997)
            {
                var x = FixedPoint.FromRaw(raw);
                Assert.AreEqual(-FixedMath.Sin(x).rawValue, FixedMath.Sin(-x).rawValue, 1);
                Assert.AreEqual(FixedMath.Cos(x).rawValue, FixedMath.Cos(-x).rawValue, 1);
            }
        }

        [Test]
        public void InverseTrigonometry_IsAccurate()
        {
            for (var raw = -100L * FixedPoint.ONE_RAW; raw <= 100L * FixedPoint.ONE_RAW; raw += 7919)
            {
                var x = FixedPoint.FromRaw(raw);
                Assert.AreEqual(Math.Atan((double)x), (double)FixedMath.Atan(x), 2 * STEP, $"atan({(double)x})");
            }

            for (var y = -5.0; y <= 5.0; y += 0.37)
            {
                for (var x = -5.0; x <= 5.0; x += 0.41)
                {
                    var fy = (FixedPoint)y;
                    var fx = (FixedPoint)x;
                    Assert.AreEqual(Math.Atan2((double)fy, (double)fx), (double)FixedMath.Atan2(fy, fx), 2 * STEP, $"atan2({y}, {x})");
                }
            }
            Assert.AreEqual(FixedPoint.Zero, FixedMath.Atan2(FixedPoint.Zero, FixedPoint.Zero));
            Assert.AreEqual((double)FixedMath.Pi, (double)FixedMath.Atan2(FixedPoint.Zero, -FixedPoint.One), STEP);

            for (var raw = -FixedPoint.ONE_RAW; raw <= FixedPoint.ONE_RAW; raw += 61)
            {
                var x = FixedPoint.FromRaw(raw);
                Assert.AreEqual(Math.Asin((double)x), (double)FixedMath.Asin(x), 3 * STEP, $"asin({(double)x})");
                Assert.AreEqual(Math.Acos((double)x), (double)FixedMath.Acos(x), 3 * STEP, $"acos({(double)x})");
            }
        }

        [Test]
        public void ExpLogPow_AreAccurate()
        {
            for (var v = -10.0; v <= 20.0; v += 0.0731)
            {
                var x = (FixedPoint)v;
                var expected = Math.Exp((double)x);
                Assert.AreEqual(expected, (double)FixedMath.Exp(x), Math.Max(3 * STEP, expected * 1e-4), $"exp({v})");
                var expected2 = Math.Pow(2, (double)x);
                Assert.AreEqual(expected2, (double)FixedMath.Exp2(x), Math.Max(3 * STEP, expected2 * 1e-4), $"exp2({v})");
            }

            for (var v = 0.001; v < 1e8; v *= 1.29)
            {
                var x = (FixedPoint)v;
                Assert.AreEqual(Math.Log((double)x), (double)FixedMath.Log(x), 3 * STEP, $"log({v})");
                Assert.AreEqual(Math.Log((double)x, 2), (double)FixedMath.Log2(x), 3 * STEP, $"log2({v})");
                Assert.AreEqual(Math.Log10((double)x), (double)FixedMath.Log10(x), 3 * STEP, $"log10({v})");
            }

            Assert.AreEqual((FixedPoint)1024, FixedMath.Pow((FixedPoint)2, 10));
            Assert.AreEqual(FixedPoint.FromFraction(1, 8), FixedMath.Pow((FixedPoint)2, -3));
            Assert.AreEqual((FixedPoint)(-27), FixedMath.Pow((FixedPoint)(-3), (FixedPoint)3));
            var p = Math.Pow(2.5, 3.5);
            Assert.AreEqual(p, (double)FixedMath.Pow(FixedPoint.FromFraction(5, 2), FixedPoint.FromFraction(7, 2)), p * 2e-4);
            Assert.AreEqual(FixedPoint.MinValue, FixedMath.Log(FixedPoint.Zero));
        }

        [Test]
        public void VectorLength_DoesNotOverflow()
        {
            Assert.AreEqual((FixedPoint)5, FixedMath.Length(new FixedVector3(3, 4, 0)));
            Assert.AreEqual((FixedPoint)5, FixedMath.Length(new FixedVector2(-3, 4)));
            var big = new FixedVector3((FixedPoint)1_000_000, (FixedPoint)1_000_000, (FixedPoint)1_000_000);
            Assert.AreEqual(Math.Sqrt(3) * 1e6, (double)FixedMath.Length(big), 1);

            var random = new FixedRandom(9);
            for (var i = 0; i < 500; i++)
            {
                var v = random.NextVector3(new FixedVector3(-1000), new FixedVector3(1000));
                var n = FixedMath.Normalize(v);
                Assert.AreEqual(1.0, (double)FixedMath.Length(n), 4 * STEP);
            }
            Assert.AreEqual(FixedVector3.Zero, FixedMath.Normalize(FixedVector3.Zero));
            Assert.AreEqual(FixedVector3.Up, FixedMath.NormalizeSafe(FixedVector3.Zero, FixedVector3.Up));
        }

        [Test]
        public void Quaternions_MatchUnityMathematics()
        {
            var random = new FixedRandom(17);
            for (var i = 0; i < 200; i++)
            {
                var euler = random.NextVector3(new FixedVector3(-4), new FixedVector3(4));
                var vector = random.NextVector3(new FixedVector3(-10), new FixedVector3(10));
                var q = FixedQuaternion.Euler(euler);
                var reference = quaternion.EulerZXY((float3)euler);
                var expected = math.mul(reference, (float3)vector);
                var actual = (float3)FixedMath.Rotate(q, vector);
                Assert.Less(math.distance(expected, actual), 2e-3f, $"rotate {euler} {vector}");
            }

            var forward = FixedMath.Normalize(new FixedVector3(1, 2, 3));
            var look = FixedQuaternion.LookRotation(forward, FixedVector3.Up);
            Assert.Less((double)FixedMath.Distance(forward, FixedMath.Forward(look)), 2e-4);

            var a = FixedQuaternion.RotateY(FixedMath.HalfPi);
            var b = FixedQuaternion.RotateY(FixedMath.Pi);
            Assert.Less((double)FixedMath.Angle(a, FixedMath.Slerp(a, b, FixedPoint.Zero)), 1e-3);
            Assert.Less((double)FixedMath.Angle(b, FixedMath.Slerp(a, b, FixedPoint.One)), 1e-3);
            Assert.AreEqual(Math.PI / 4, (double)FixedMath.Angle(a, FixedMath.Slerp(a, b, FixedPoint.Half)), 1e-3);
            Assert.Less((double)FixedMath.Distance(new FixedVector3(1, 2, 3), FixedMath.Rotate(FixedMath.Inverse(a), FixedMath.Rotate(a, new FixedVector3(1, 2, 3)))), 1e-3);
        }

        [Test]
        public void AngleHelpers_Work()
        {
            Assert.AreEqual(Math.PI, (double)FixedMath.ToRadians(180), STEP);
            Assert.AreEqual(90.0, (double)FixedMath.ToDegrees(FixedMath.HalfPi), 2e-3);
            Assert.AreEqual(-Math.PI / 2, (double)FixedMath.DeltaAngle(FixedPoint.Zero, FixedMath.Pi + FixedMath.HalfPi), 2 * STEP);
            Assert.AreEqual((FixedPoint)2, FixedMath.MoveTowards(FixedPoint.Zero, (FixedPoint)10, (FixedPoint)2));
            Assert.AreEqual((FixedPoint)10, FixedMath.MoveTowards((FixedPoint)9, (FixedPoint)10, (FixedPoint)2));
            Assert.AreEqual(FixedPoint.FromFraction(1, 2), FixedMath.Wrap(FixedPoint.FromFraction(5, 2), FixedPoint.Zero, (FixedPoint)2));
            Assert.AreEqual(FixedPoint.FromFraction(3, 2), FixedMath.Wrap(FixedPoint.FromFraction(-1, 2), FixedPoint.Zero, (FixedPoint)2));
        }

        [Test]
        public void Burst_ProducesBitIdenticalResults()
        {
            using (var inputs = CreateGoldenInputs(Allocator.TempJob))
            using (var managed = new NativeArray<long>(inputs.Length * MathBatchJob.OUTPUTS_PER_INPUT, Allocator.TempJob))
            using (var bursted = new NativeArray<long>(inputs.Length * MathBatchJob.OUTPUTS_PER_INPUT, Allocator.TempJob))
            {
                new MathBatchJob { inputs = inputs, outputs = managed }.Execute();
                new MathBatchJob { inputs = inputs, outputs = bursted }.Run();
                for (var i = 0; i < managed.Length; i++)
                {
                    Assert.AreEqual(managed[i], bursted[i], $"output {i % MathBatchJob.OUTPUTS_PER_INPUT} of input {inputs[i / MathBatchJob.OUTPUTS_PER_INPUT]}");
                }
            }
        }

        // Pins the exact results of the math library. A change here changes every simulation built on it: only update
        // the constant deliberately, and expect old replays to stop matching.
        [Test]
        public void GoldenResults_DoNotChange()
        {
            using (var inputs = CreateGoldenInputs(Allocator.TempJob))
            using (var outputs = new NativeArray<long>(inputs.Length * MathBatchJob.OUTPUTS_PER_INPUT, Allocator.TempJob))
            {
                new MathBatchJob { inputs = inputs, outputs = outputs }.Execute();
                ulong hash = 14695981039346656037UL;
                for (var i = 0; i < outputs.Length; i++)
                {
                    hash ^= (ulong)outputs[i];
                    hash *= 1099511628211UL;
                }
                Assert.AreEqual(GOLDEN_HASH, hash, $"Math results changed; new hash 0x{hash:X16}.");
            }
        }

        private const ulong GOLDEN_HASH = 0x7F3C51DAD5EB01A0UL;

        private static NativeArray<long> CreateGoldenInputs(Allocator allocator)
        {
            var random = new FixedRandom(1234);
            var inputs = new NativeArray<long>(512, allocator);
            for (var i = 0; i < inputs.Length; i++)
            {
                // Mix small, medium and large magnitudes, both signs.
                var magnitude = (i % 4) switch
                {
                    0 => (FixedPoint)1,
                    1 => (FixedPoint)10,
                    2 => (FixedPoint)1000,
                    _ => (FixedPoint)100000,
                };
                inputs[i] = random.NextFixedPoint(-magnitude, magnitude).rawValue;
            }
            return inputs;
        }

        private static long NextLong(System.Random random, long range)
        {
            var bytes = new byte[8];
            random.NextBytes(bytes);
            var value = BitConverter.ToInt64(bytes, 0) % range;
            return value;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct MathBatchJob : IJob
        {
            public const int OUTPUTS_PER_INPUT = 14;

            [ReadOnly] public NativeArray<long> inputs;
            public NativeArray<long> outputs;

            public void Execute()
            {
                for (var i = 0; i < inputs.Length; i++)
                {
                    var x = FixedPoint.FromRaw(inputs[i]);
                    var y = FixedPoint.FromRaw(inputs[(i + 1) % inputs.Length]);
                    var o = i * OUTPUTS_PER_INPUT;
                    outputs[o + 0] = FixedMath.Sin(x).rawValue;
                    outputs[o + 1] = FixedMath.Cos(x).rawValue;
                    outputs[o + 2] = FixedMath.Tan(x).rawValue;
                    outputs[o + 3] = FixedMath.Atan(x).rawValue;
                    outputs[o + 4] = FixedMath.Atan2(y, x).rawValue;
                    outputs[o + 5] = FixedMath.Sqrt(FixedMath.Abs(x)).rawValue;
                    outputs[o + 6] = FixedMath.Exp(x % 20).rawValue;
                    outputs[o + 7] = FixedMath.Log(FixedMath.Abs(x) + FixedPoint.Epsilon).rawValue;
                    outputs[o + 8] = FixedMath.Asin(FixedMath.Frac(x)).rawValue;
                    outputs[o + 9] = (x * y).rawValue;
                    outputs[o + 10] = (x / y).rawValue;
                    outputs[o + 11] = FixedMath.Length(new FixedVector3(x, y, x - y)).rawValue;
                    outputs[o + 12] = FixedMath.Rotate(FixedQuaternion.Euler(new FixedVector3(x, y, FixedPoint.Half)), new FixedVector3(1, 2, 3)).x.rawValue;
                    outputs[o + 13] = FixedMath.Pow(FixedMath.Abs(x % 4) + FixedPoint.One, FixedMath.Frac(y) * 3).rawValue;
                }
            }
        }
    }
}
