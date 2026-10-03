using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Lockstep.Mathematics;

namespace Pragma.Lockstep.Tests
{
    public class RandomTests
    {
        [Test]
        public void SameSeed_GivesSameSequence()
        {
            var a = new FixedRandom(42);
            var b = new FixedRandom(42);
            for (var i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void DifferentSeedsAndIndices_GiveDifferentSequences()
        {
            Assert.AreNotEqual(new FixedRandom(1).NextUInt(), new FixedRandom(2).NextUInt());
            Assert.AreNotEqual(FixedRandom.CreateFromIndex(5, 0).NextUInt(), FixedRandom.CreateFromIndex(5, 1).NextUInt());
        }

        [Test]
        public void MatchesReferencePcg32()
        {
            // Reference PCG32 (XSH-RR) written independently of the implementation under test.
            const ulong multiplier = 6364136223846793005UL;
            var random = new FixedRandom(123, 77);
            ulong state = 0;
            var increment = (77UL << 1) | 1UL;
            uint Next()
            {
                var old = state;
                state = unchecked(old * multiplier + increment);
                var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
                var rotation = (int)(old >> 59);
                return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
            }
            Next();
            state += FixedRandom.SplitMix64(123);
            Next();

            for (var i = 0; i < 100; i++)
            {
                Assert.AreEqual(Next(), random.NextUInt());
            }
        }

        [Test]
        public void BoundedValues_StayInRangeAndCoverIt()
        {
            var random = new FixedRandom(99);
            var seen = new HashSet<int>();
            for (var i = 0; i < 10000; i++)
            {
                var value = random.NextInt(-3, 4);
                Assert.That(value, Is.InRange(-3, 3));
                seen.Add(value);

                var f = random.NextFixedPoint();
                Assert.That(f.rawValue, Is.InRange(0, FixedPoint.ONE_RAW - 1));

                var ranged = random.NextFixedPoint((FixedPoint)(-5), (FixedPoint)5);
                Assert.That(ranged.rawValue, Is.InRange(-5 * FixedPoint.ONE_RAW, 5 * FixedPoint.ONE_RAW - 1));

                var angle = random.NextAngle();
                Assert.That(angle.rawValue, Is.InRange(0, FixedMath.TwoPi.rawValue - 1));
            }
            Assert.AreEqual(7, seen.Count);
            Assert.AreEqual(3, random.NextInt(3, 3), "an empty range returns min");
            Assert.AreEqual(0, random.NextInt(0));
        }

        [Test]
        public void Directions_AreUnitLength()
        {
            var random = new FixedRandom(8);
            for (var i = 0; i < 200; i++)
            {
                Assert.AreEqual(1.0, (double)FixedMath.Length(random.NextDirection2()), 1e-3);
                Assert.AreEqual(1.0, (double)FixedMath.Length(random.NextDirection3()), 1e-3);
                Assert.LessOrEqual((double)FixedMath.Length(random.NextInsideUnitCircle()), 1.0 + 1e-3);
                Assert.AreEqual(1.0, (double)FixedMath.Length(random.NextRotation()), 1e-3);
            }
        }
    }
}
