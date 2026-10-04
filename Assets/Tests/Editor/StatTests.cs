using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Stats;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Pragma.Lockstep.Tests
{
    public class StatTests
    {
        private const int MAX_HEALTH = 1;
        private const int SPEED = 2;
        private const int HEALTH = 3;
        private const int MAX_ENERGY = 4;
        private const int ENERGY = 5;
        private const int GOLD = 6;

        private const int INFANTRY = 1;
        private const int VEHICLE = 2;

        private static readonly LockstepStatSource Ability = new LockstepStatSource(1, 7);
        private static readonly LockstepStatSource Research = new LockstepStatSource(2, 7);
        private static readonly LockstepStatSource Damage = new LockstepStatSource(3, 1);

        private enum ByteStat : byte
        {
            Value = 200,
        }

        private enum ShortStat : short
        {
            Value = 3000,
        }

        private enum IntStat
        {
            Value = 70000,
        }

        private enum LongStat : long
        {
            Value = 5,
        }

        [Test]
        public void Calculate_AddsFlatThenAdditiveThenMultiplicative()
        {
            var modifiers = new NativeArray<LockstepStatModifier>(new[]
            {
                LockstepStatModifier.MultiplicativePercent(MAX_HEALTH, FixedPoint.Half),
                LockstepStatModifier.AdditivePercent(MAX_HEALTH, FixedPoint.FromFraction(1, 4)),
                LockstepStatModifier.Flat(MAX_HEALTH, 20),
                LockstepStatModifier.MultiplicativePercent(MAX_HEALTH, -FixedPoint.FromFraction(1, 4)),
                LockstepStatModifier.AdditivePercent(MAX_HEALTH, FixedPoint.FromFraction(1, 8)),
                LockstepStatModifier.Flat(SPEED, 1000),
            }, Allocator.Temp);
            using (modifiers)
            {
                // (100 + 20) * (1 + 1/4 + 1/8) * (1 + 1/2) * (1 - 1/4)
                Assert.AreEqual(FixedPoint.FromFraction(1485, 8), LockstepStats.Calculate(MAX_HEALTH, 100, modifiers));
                Assert.AreEqual((FixedPoint)1004, LockstepStats.Calculate(SPEED, 4, modifiers), "only the modifiers of the stat count");
                Assert.AreEqual((FixedPoint)5, LockstepStats.Calculate(7, 5, modifiers), "a stat without modifiers keeps its base");
            }
        }

        [Test]
        public void Calculate_StopsThePercentageFactorsAtZero()
        {
            var modifiers = new NativeArray<LockstepStatModifier>(new[]
            {
                LockstepStatModifier.AdditivePercent(MAX_HEALTH, -FixedPoint.FromFraction(3, 4)),
                LockstepStatModifier.AdditivePercent(MAX_HEALTH, -FixedPoint.FromFraction(3, 4)),
                LockstepStatModifier.MultiplicativePercent(SPEED, -FixedPoint.FromFraction(3, 2)),
                LockstepStatModifier.MultiplicativePercent(SPEED, -FixedPoint.FromFraction(3, 2)),
                LockstepStatModifier.MultiplicativePercent(SPEED, FixedPoint.Half),
                LockstepStatModifier.AdditivePercent(MAX_ENERGY, -FixedPoint.FromFraction(3, 4)),
                LockstepStatModifier.AdditivePercent(MAX_ENERGY, -FixedPoint.FromFraction(3, 4)),
                LockstepStatModifier.AdditivePercent(MAX_ENERGY, FixedPoint.One),
                LockstepStatModifier.Flat(ENERGY, -15),
                LockstepStatModifier.AdditivePercent(ENERGY, FixedPoint.Half),
            }, Allocator.Temp);
            using (modifiers)
            {
                Assert.AreEqual(FixedPoint.Zero, LockstepStats.Calculate(MAX_HEALTH, 10, modifiers), "-150 % leaves nothing, not -50 %");
                Assert.AreEqual(FixedPoint.Zero, LockstepStats.Calculate(SPEED, 10, modifiers), "two factors below zero do not cancel out");
                // 10 * (1 - 3/4 - 3/4 + 1)
                Assert.AreEqual((FixedPoint)5, LockstepStats.Calculate(MAX_ENERGY, 10, modifiers), "the shares are summed before the floor");
                // (10 - 15) * (1 + 1/2)
                Assert.AreEqual(FixedPoint.FromFraction(-15, 2), LockstepStats.Calculate(ENERGY, 10, modifiers), "the flat part may go below zero");
            }
        }

        [Test]
        public void Calculate_AppliesOnlyTheStrongestNonStackingModifierOfAKind()
        {
            const LockstepStatStacking strongest = LockstepStatStacking.Strongest;
            var modifiers = new NativeArray<LockstepStatModifier>(new[]
            {
                LockstepStatModifier.AdditivePercent(SPEED, FixedPoint.FromFraction(1, 8), new LockstepStatSource(5, 1), stacking: strongest),
                LockstepStatModifier.AdditivePercent(SPEED, FixedPoint.Half, new LockstepStatSource(5, 2), stacking: strongest),
                // Weaker than the half, as its absolute value is smaller.
                LockstepStatModifier.AdditivePercent(SPEED, -FixedPoint.FromFraction(3, 8), new LockstepStatSource(5, 3), stacking: strongest),
                // Stacking modifiers of the same kind and modifiers of other kinds or types are not in the group.
                LockstepStatModifier.AdditivePercent(SPEED, FixedPoint.FromFraction(1, 4), new LockstepStatSource(5, 4)),
                LockstepStatModifier.AdditivePercent(SPEED, FixedPoint.FromFraction(1, 4), new LockstepStatSource(6, 1), stacking: strongest),
                LockstepStatModifier.MultiplicativePercent(SPEED, FixedPoint.Half, new LockstepStatSource(5, 5), stacking: strongest),
                // A tie: only the first one counts.
                LockstepStatModifier.Flat(SPEED, 10, new LockstepStatSource(7, 1), stacking: strongest),
                LockstepStatModifier.Flat(SPEED, 10, new LockstepStatSource(7, 2), stacking: strongest),
            }, Allocator.Temp);
            using (modifiers)
            {
                // (100 + 10) * (1 + 1/2 + 1/4 + 1/4) * (1 + 1/2)
                Assert.AreEqual((FixedPoint)330, LockstepStats.Calculate(SPEED, 100, modifiers));
            }
        }

        [Test]
        public void System_AppliesModifiersAndRemovesThemBySource()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateUnit(entityManager, (MAX_HEALTH, 100), (SPEED, 4));
                TestUtility.Step(simulation);

                var modifiers = entityManager.GetBuffer<LockstepStatModifier>(unit);
                modifiers.Add(LockstepStatModifier.AdditivePercent(SPEED, FixedPoint.Half, Ability));
                modifiers.Add(LockstepStatModifier.Flat(MAX_HEALTH, 30, Ability));
                modifiers.Add(LockstepStatModifier.Flat(MAX_HEALTH, 50, Research));
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)6, Value(entityManager, unit, SPEED));
                Assert.AreEqual((FixedPoint)180, Value(entityManager, unit, MAX_HEALTH));

                Assert.AreEqual(2, entityManager.GetBuffer<LockstepStatModifier>(unit).RemoveModifiers(Ability));
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)4, Value(entityManager, unit, SPEED), "the ability ended");
                Assert.AreEqual((FixedPoint)150, Value(entityManager, unit, MAX_HEALTH), "the research stays");
            }
        }

        [Test]
        public void TimedModifier_EndsOnItsEndTick()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateUnit(entityManager, (SPEED, 4));
                TestUtility.Step(simulation);

                // Added before tick T for three ticks: it applies on T, T + 1 and T + 2.
                var endTick = simulation.Tick + 3;
                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.MultiplicativePercent(SPEED, 1, default, endTick));
                for (var i = 0; i < 3; i++)
                {
                    TestUtility.Step(simulation);
                    Assert.AreEqual((FixedPoint)8, Value(entityManager, unit, SPEED), $"tick {simulation.Tick - 1}");
                }

                TestUtility.Step(simulation);
                Assert.AreEqual(endTick + 1, simulation.Tick);
                Assert.AreEqual((FixedPoint)4, Value(entityManager, unit, SPEED));
                Assert.AreEqual(0, entityManager.GetBuffer<LockstepStatModifier>(unit, true).Length, "the expired modifier is gone");
            }
        }

        [Test]
        public void BaseValue_ChangesUnderItsModifiers()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateEntity(entityManager, LockstepStat.Attribute(MAX_HEALTH, 100), LockstepStat.Resource(GOLD, 5));
                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.AdditivePercent(MAX_HEALTH, FixedPoint.Half, Research));
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)150, Value(entityManager, unit, MAX_HEALTH));

                var stats = entityManager.GetBuffer<LockstepStat>(unit);
                Assert.IsTrue(stats.TrySetBase(MAX_HEALTH, 200));
                Assert.IsFalse(stats.TrySetBase(SPEED, 5), "the unit has no speed");
                Assert.IsFalse(stats.TrySetBase(GOLD, 50), "a resource has no base value");
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)300, Value(entityManager, unit, MAX_HEALTH));
                Assert.AreEqual((FixedPoint)5, Value(entityManager, unit, GOLD));
                stats = entityManager.GetBuffer<LockstepStat>(unit, true);
                Assert.IsTrue(stats.TryGetBase(MAX_HEALTH, out var baseValue));
                Assert.AreEqual((FixedPoint)200, baseValue);
                Assert.IsFalse(stats.TryGetBase(GOLD, out _));
                Assert.IsFalse(stats.TryGetValue(SPEED, out _));
            }
        }

        [Test]
        public void UnchangedStats_AreNotWritten()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateUnit(entityManager, (MAX_HEALTH, 100));
                TestUtility.Step(simulation);
                var version = StatVersion(entityManager, unit);

                for (var i = 0; i < 5; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(version, StatVersion(entityManager, unit), "nothing changed, so nothing was written");

                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.Flat(MAX_HEALTH, 1, default, simulation.Tick + 2));
                TestUtility.Step(simulation);
                var applied = StatVersion(entityManager, unit);
                Assert.AreNotEqual(version, applied, "the new modifier was applied");
                Assert.AreEqual((FixedPoint)101, Value(entityManager, unit, MAX_HEALTH));

                TestUtility.Step(simulation);
                Assert.AreEqual(applied, StatVersion(entityManager, unit), "a running modifier writes nothing");

                TestUtility.Step(simulation);
                Assert.AreNotEqual(applied, StatVersion(entityManager, unit), "the expired modifier was removed");
                Assert.AreEqual((FixedPoint)100, Value(entityManager, unit, MAX_HEALTH));
            }
        }

        [Test]
        public void Resource_AddsUpTheChangesOfATickAndStaysWithinZeroAndItsCap()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateEntity(entityManager,
                    LockstepStat.Attribute(MAX_HEALTH, 100), LockstepStat.Resource(HEALTH, MAX_HEALTH, LockstepStatCapPolicy.KeepRatio));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 100, 100, "a capped resource starts full");

                AddChanges(entityManager, unit, (HEALTH, -30), (HEALTH, 10));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 80, 100);
                Assert.AreEqual(0, entityManager.GetBuffer<LockstepStatChange>(unit, true).Length, "the changes were used up");

                // Summed first: healing 50 at 80 of 100 is not lost to the cap before the damage of the same tick.
                AddChanges(entityManager, unit, (HEALTH, 50), (HEALTH, -90));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 40, 100);

                AddChanges(entityManager, unit, (HEALTH, -500));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 0, 100, "never below zero");

                AddChanges(entityManager, unit, (HEALTH, 1000));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 100, 100, "never above the cap");

                // Attributes take no changes, resources take no modifiers.
                AddChanges(entityManager, unit, (MAX_HEALTH, -50));
                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.Flat(HEALTH, -40));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 100, 100);
                Assert.AreEqual((FixedPoint)100, Value(entityManager, unit, MAX_HEALTH));
            }
        }

        [Test]
        public void Resource_KeepsItsShareOrItsAmountWhenItsCapChanges()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateEntity(entityManager,
                    LockstepStat.Attribute(MAX_HEALTH, 100), LockstepStat.Resource(HEALTH, MAX_HEALTH, LockstepStatCapPolicy.KeepRatio),
                    LockstepStat.Attribute(MAX_ENERGY, 100), LockstepStat.Resource(ENERGY, MAX_ENERGY, LockstepStatCapPolicy.Clamp));
                TestUtility.Step(simulation);
                AddChanges(entityManager, unit, (HEALTH, -20), (ENERGY, -20));
                TestUtility.Step(simulation);

                var modifiers = entityManager.GetBuffer<LockstepStatModifier>(unit);
                modifiers.Add(LockstepStatModifier.AdditivePercent(MAX_HEALTH, FixedPoint.Half, Ability));
                modifiers.Add(LockstepStatModifier.AdditivePercent(MAX_ENERGY, FixedPoint.Half, Ability));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 120, 150, "the share is kept");
                AssertResource(entityManager, unit, ENERGY, 80, 150, "the amount is kept");

                entityManager.GetBuffer<LockstepStatModifier>(unit).RemoveModifiers(Ability);
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 80, 100, "a bonus that ends neither heals nor wounds");
                AssertResource(entityManager, unit, ENERGY, 80, 100);

                modifiers = entityManager.GetBuffer<LockstepStatModifier>(unit);
                modifiers.Add(LockstepStatModifier.MultiplicativePercent(MAX_HEALTH, -FixedPoint.Half, Ability));
                modifiers.Add(LockstepStatModifier.MultiplicativePercent(MAX_ENERGY, -FixedPoint.Half, Ability));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 40, 50);
                AssertResource(entityManager, unit, ENERGY, 50, 50, "cut down to the lower cap");

                entityManager.GetBuffer<LockstepStatModifier>(unit).RemoveModifiers(Ability);
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, HEALTH, 80, 100);
                AssertResource(entityManager, unit, ENERGY, 50, 100, "what was cut stays lost");
            }
        }

        [Test]
        public void Resource_StartsFullUnderTheModifiersOfItsCap()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateEntity(entityManager,
                    LockstepStat.Attribute(MAX_HEALTH, 100), LockstepStat.Resource(HEALTH, MAX_HEALTH, LockstepStatCapPolicy.Clamp),
                    LockstepStat.Resource(ENERGY, MAX_ENERGY, LockstepStatCapPolicy.KeepRatio), LockstepStat.Resource(GOLD, 40));
                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.AdditivePercent(MAX_HEALTH, FixedPoint.Half, Research));
                AddChanges(entityManager, unit, (HEALTH, -10), (GOLD, 15));
                TestUtility.Step(simulation);

                AssertResource(entityManager, unit, HEALTH, 140, 150, "filled to the modified cap, then the changes of the tick");
                AssertResource(entityManager, unit, ENERGY, 0, 0, "without its cap attribute a resource is uncapped");
                AssertResource(entityManager, unit, GOLD, 55, 0, "an uncapped resource keeps any amount");

                AddChanges(entityManager, unit, (GOLD, -100));
                TestUtility.Step(simulation);
                AssertResource(entityManager, unit, GOLD, 0, 0);
            }
        }

        [Test]
        public void Grants_ReachTheTargetedReceiversAlsoTheLaterOnes()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var player = entityManager.CreateEntity(typeof(LockstepStatGrant));
                var infantry = CreateReceiver(entityManager, player, INFANTRY);
                var vehicle = CreateReceiver(entityManager, player, VEHICLE);
                var untargeted = CreateReceiver(entityManager, player);
                var grants = entityManager.GetBuffer<LockstepStatGrant>(player);
                grants.Add(LockstepStatGrant.Create(LockstepStatModifier.Flat(MAX_HEALTH, 10, Research)));
                grants.Add(LockstepStatGrant.Create(LockstepStatModifier.AdditivePercent(MAX_HEALTH, FixedPoint.Half, Research), INFANTRY));
                // For one tick: added before tick T, it ends on T + 1.
                grants.Add(LockstepStatGrant.Create(LockstepStatModifier.AdditivePercent(SPEED, 1, Ability, simulation.Tick + 1), VEHICLE));
                TestUtility.Step(simulation);

                Assert.AreEqual((FixedPoint)165, Value(entityManager, infantry, MAX_HEALTH));
                Assert.AreEqual((FixedPoint)110, Value(entityManager, vehicle, MAX_HEALTH));
                Assert.AreEqual((FixedPoint)110, Value(entityManager, untargeted, MAX_HEALTH), "a grant for any receiver");
                Assert.AreEqual((FixedPoint)8, Value(entityManager, vehicle, SPEED));
                Assert.AreEqual((FixedPoint)4, Value(entityManager, infantry, SPEED));
                AssertResource(entityManager, infantry, HEALTH, 165, 165, "a new receiver starts full under its grants");

                // A unit made later gets what its player has; the timed grant ends on its end tick.
                var reinforcement = CreateReceiver(entityManager, player, INFANTRY);
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)165, Value(entityManager, reinforcement, MAX_HEALTH));
                Assert.AreEqual((FixedPoint)4, Value(entityManager, vehicle, SPEED), "the timed grant ended");
                Assert.AreEqual(2, entityManager.GetBuffer<LockstepStatGrant>(player, true).Length);

                Assert.AreEqual(2, entityManager.GetBuffer<LockstepStatGrant>(player).RemoveGrants(Research));
                TestUtility.Step(simulation);
                foreach (var unit in new[] { infantry, vehicle, untargeted, reinforcement })
                {
                    Assert.AreEqual((FixedPoint)100, Value(entityManager, unit, MAX_HEALTH), "a removed grant leaves every receiver");
                    AssertResource(entityManager, unit, HEALTH, 100, 100);
                }
            }
        }

        [Test]
        public void Grants_StackWithTheReceiversOwnModifiers()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var squad = entityManager.CreateEntity(typeof(LockstepStatGrant));
                var member = CreateReceiver(entityManager, squad);
                var aura = new LockstepStatSource(9, 1);
                var otherAura = new LockstepStatSource(9, 2);
                entityManager.GetBuffer<LockstepStatModifier>(member).Add(
                    LockstepStatModifier.AdditivePercent(SPEED, FixedPoint.FromFraction(1, 4), aura, stacking: LockstepStatStacking.Strongest));
                entityManager.GetBuffer<LockstepStatGrant>(squad).Add(LockstepStatGrant.Create(
                    LockstepStatModifier.AdditivePercent(SPEED, FixedPoint.Half, otherAura, stacking: LockstepStatStacking.Strongest)));
                entityManager.GetBuffer<LockstepStatGrant>(squad).Add(LockstepStatGrant.Create(LockstepStatModifier.Flat(SPEED, 1, Research)));
                TestUtility.Step(simulation);

                // (4 + 1) * (1 + 1/2): the two auras of one kind count once, the stronger one.
                Assert.AreEqual(FixedPoint.FromFraction(15, 2), Value(entityManager, member, SPEED));
            }
        }

        [Test]
        public void Grants_LeaveWithAGrantorThatIsDestroyed()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var squad = entityManager.CreateEntity(typeof(LockstepStatGrant));
                var player = entityManager.CreateEntity(typeof(LockstepStatGrant));
                var member = CreateReceiver(entityManager, squad);
                entityManager.GetBuffer<LockstepStatGrantor>(member).Add(new LockstepStatGrantor(player));
                entityManager.GetBuffer<LockstepStatGrant>(squad).Add(LockstepStatGrant.Create(LockstepStatModifier.Flat(SPEED, 2, Ability)));
                entityManager.GetBuffer<LockstepStatGrant>(player).Add(LockstepStatGrant.Create(LockstepStatModifier.Flat(SPEED, 1, Research)));
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)7, Value(entityManager, member, SPEED));

                entityManager.DestroyEntity(squad);
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)5, Value(entityManager, member, SPEED), "what the squad gave is gone");
                var grantors = entityManager.GetBuffer<LockstepStatGrantor>(member, true);
                Assert.AreEqual(1, grantors.Length, "the destroyed grantor was dropped");
                Assert.AreEqual(player, grantors[0].entity);
            }
        }

        [Test]
        public void UnchangedGrants_LeaveTheirReceiversUnwritten()
        {
            using (var simulation = CreateSimulation())
            {
                var entityManager = simulation.World.EntityManager;
                var player = entityManager.CreateEntity(typeof(LockstepStatGrant));
                var unit = CreateReceiver(entityManager, player);
                entityManager.GetBuffer<LockstepStatGrant>(player).Add(LockstepStatGrant.Create(LockstepStatModifier.Flat(SPEED, 1, Research)));
                TestUtility.Step(simulation);
                var version = StatVersion(entityManager, unit);

                for (var i = 0; i < 5; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(version, StatVersion(entityManager, unit), "the grants did not change, so nothing was written");

                entityManager.GetBuffer<LockstepStatGrant>(player).Add(LockstepStatGrant.Create(LockstepStatModifier.Flat(SPEED, 1, Ability)));
                TestUtility.Step(simulation);
                Assert.AreNotEqual(version, StatVersion(entityManager, unit), "a new grant was applied");
                Assert.AreEqual((FixedPoint)6, Value(entityManager, unit, SPEED));
            }
        }

        [Test]
        public void Id_IsTheValueOfAnEnumOfAnySize()
        {
            Assert.AreEqual(200, LockstepStats.Id(ByteStat.Value));
            Assert.AreEqual(3000, LockstepStats.Id(ShortStat.Value));
            Assert.AreEqual(70000, LockstepStats.Id(IntStat.Value));
            Assert.AreEqual(5, LockstepStats.Id(LongStat.Value));

            using (var ids = new NativeArray<int>(4, Allocator.TempJob))
            {
                new IdJob { ids = ids }.Run();
                CollectionAssert.AreEqual(new[] { 200, 3000, 70000, 5 }, ids.ToArray(), "Burst gives the same ids");
            }

            using (var world = new World("Stats"))
            {
                var entity = CreateEntity(world.EntityManager, LockstepStat.Attribute(IntStat.Value, 3));
                Assert.IsTrue(world.EntityManager.GetBuffer<LockstepStat>(entity, true).TryGet(IntStat.Value, out var stat));
                Assert.AreEqual(70000, stat.type);
                Assert.AreEqual((FixedPoint)3, stat.value);
            }
        }

        [Test]
        public void Session_StatsMatchOnEveryClient_AlsoOnALateJoiner()
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 2;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 99;
            settings.MinPlayersToStart = 1;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 5;

            using (var session = new SessionHarness(settings, latency: 0.05, jitter: 0.04))
            {
                var first = session.AddClient(SessionOptions());
                session.Run(4);
                // Replays the match from tick 0, many ticks per frame: the stats must not depend on the pace.
                var late = session.AddClient(SessionOptions());
                session.Run(4);

                Assert.AreEqual(LockstepClientState.Running, late.State);
                Assert.Greater(late.JoinTick, 60);
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, late, 20);
                AssertStatsAreUpToDate(first);
                AssertStatsAreUpToDate(late);
            }
        }

        private static LockstepSimulation CreateSimulation()
        {
            return new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(LockstepStatSystem)));
        }

        private static LockstepSimulationOptions SessionOptions()
        {
            var options = TestUtility.Options(typeof(LockstepStatSystem), typeof(TestStatSystem));
            options.Initialize = world =>
            {
                var entityManager = world.EntityManager;
                var grantor = entityManager.CreateEntity(typeof(LockstepStatGrant));
                for (var i = 0; i < 9; i++)
                {
                    var unit = CreateEntity(entityManager,
                        LockstepStat.Attribute(TestStatSystem.FIRST_ATTRIBUTE, 10 + i),
                        LockstepStat.Attribute(TestStatSystem.FIRST_ATTRIBUTE + 1, 20 + i),
                        LockstepStat.Attribute(TestStatSystem.FIRST_ATTRIBUTE + 2, 30 + i),
                        LockstepStat.Resource(TestStatSystem.FIRST_RESOURCE, TestStatSystem.FIRST_ATTRIBUTE, LockstepStatCapPolicy.KeepRatio),
                        LockstepStat.Resource(TestStatSystem.FIRST_RESOURCE + 1, TestStatSystem.FIRST_ATTRIBUTE + 1, LockstepStatCapPolicy.Clamp),
                        LockstepStat.Resource(TestStatSystem.FIRST_RESOURCE + 2, 50));
                    entityManager.AddBuffer<LockstepStatGrantor>(unit).Add(new LockstepStatGrantor(grantor));
                    entityManager.AddBuffer<LockstepStatTarget>(unit).Add(new LockstepStatTarget(1 + i % TestStatSystem.TARGET_COUNT));
                    // Three archetypes: a tick usually changes one chunk and leaves the others to the change filter.
                    if (i % 3 == 1)
                    {
                        entityManager.AddComponent<TestSpawnedTag>(unit);
                    }
                    else if (i % 3 == 2)
                    {
                        entityManager.AddComponent<LockstepEntityId>(unit);
                    }
                }
            };
            return options;
        }

        // Every attribute is what its base, modifiers and grants give, and every resource is within its bounds: the stat
        // system kept up with every change.
        private static void AssertStatsAreUpToDate(LockstepClient client)
        {
            var entityManager = client.Simulation.World.EntityManager;
            using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LockstepStat>(), ComponentType.ReadOnly<LockstepStatGrantor>()))
            {
                var units = query.ToEntityArray(Allocator.Temp);
                var modified = 0;
                var damaged = 0;
                foreach (var unit in units)
                {
                    var stats = entityManager.GetBuffer<LockstepStat>(unit, true);
                    var applied = AppliedModifiers(entityManager, unit);
                    for (var i = 0; i < stats.Length; i++)
                    {
                        var stat = stats[i];
                        if (stat.IsAttribute)
                        {
                            Assert.AreEqual(LockstepStats.Calculate(stat.type, stat.baseValue, applied.AsArray()), stat.value);
                            modified += stat.value != stat.baseValue ? 1 : 0;
                            continue;
                        }
                        Assert.GreaterOrEqual(stat.value, FixedPoint.Zero);
                        if (stat.cap != LockstepStat.NONE)
                        {
                            Assert.IsTrue(stats.TryGetValue(stat.cap, out var cap));
                            Assert.AreEqual(FixedMath.Max(cap, FixedPoint.Zero), stat.max, "the resource follows its cap");
                            Assert.LessOrEqual(stat.value, stat.max);
                            damaged += stat.value < stat.max ? 1 : 0;
                        }
                    }
                    Assert.AreEqual(0, entityManager.GetBuffer<LockstepStatChange>(unit, true).Length);
                }
                Assert.AreEqual(9, units.Length);
                Assert.Greater(modified, 0, "the modifiers changed some attributes");
                Assert.Greater(damaged, 0, "the changes spent some resources");
            }
        }

        private static NativeList<LockstepStatModifier> AppliedModifiers(EntityManager entityManager, Entity unit)
        {
            var applied = new NativeList<LockstepStatModifier>(Allocator.Temp);
            applied.AddRange(entityManager.GetBuffer<LockstepStatModifier>(unit, true).AsNativeArray());
            var targets = entityManager.GetBuffer<LockstepStatTarget>(unit, true);
            foreach (var grantor in entityManager.GetBuffer<LockstepStatGrantor>(unit, true))
            {
                foreach (var grant in entityManager.GetBuffer<LockstepStatGrant>(grantor.entity, true))
                {
                    var isTargeted = grant.target == LockstepStatGrant.ANY;
                    foreach (var target in targets)
                    {
                        isTargeted = isTargeted || target.value == grant.target;
                    }
                    if (isTargeted)
                    {
                        applied.Add(grant.modifier);
                    }
                }
            }
            return applied;
        }

        private static Entity CreateEntity(EntityManager entityManager, params LockstepStat[] stats)
        {
            var entity = entityManager.CreateEntity(typeof(LockstepStat), typeof(LockstepStatModifier), typeof(LockstepStatChange));
            var buffer = entityManager.GetBuffer<LockstepStat>(entity);
            foreach (var stat in stats)
            {
                buffer.Add(stat);
            }
            return entity;
        }

        private static Entity CreateUnit(EntityManager entityManager, params (int Type, FixedPoint Base)[] attributes)
        {
            var unit = entityManager.CreateEntity(typeof(LockstepStat), typeof(LockstepStatModifier));
            var buffer = entityManager.GetBuffer<LockstepStat>(unit);
            foreach (var attribute in attributes)
            {
                buffer.Add(LockstepStat.Attribute(attribute.Type, attribute.Base));
            }
            return unit;
        }

        // A unit with health and speed that inherits from grantor and belongs to targets.
        private static Entity CreateReceiver(EntityManager entityManager, Entity grantor, params int[] targets)
        {
            var unit = CreateEntity(entityManager,
                LockstepStat.Attribute(MAX_HEALTH, 100), LockstepStat.Resource(HEALTH, MAX_HEALTH, LockstepStatCapPolicy.KeepRatio),
                LockstepStat.Attribute(SPEED, 4));
            entityManager.AddBuffer<LockstepStatGrantor>(unit).Add(new LockstepStatGrantor(grantor));
            var buffer = entityManager.AddBuffer<LockstepStatTarget>(unit);
            foreach (var target in targets)
            {
                buffer.Add(new LockstepStatTarget(target));
            }
            return unit;
        }

        private static void AddChanges(EntityManager entityManager, Entity unit, params (int Stat, int Amount)[] changes)
        {
            var buffer = entityManager.GetBuffer<LockstepStatChange>(unit);
            foreach (var change in changes)
            {
                buffer.Add(LockstepStatChange.Create(change.Stat, change.Amount, Damage));
            }
        }

        private static FixedPoint Value(EntityManager entityManager, Entity unit, int type)
        {
            Assert.IsTrue(entityManager.GetBuffer<LockstepStat>(unit, true).TryGetValue(type, out var value), $"stat {type}");
            return value;
        }

        private static void AssertResource(EntityManager entityManager, Entity unit, int type, int value, int max, string message = null)
        {
            Assert.IsTrue(entityManager.GetBuffer<LockstepStat>(unit, true).TryGet(type, out var stat), $"stat {type}");
            Assert.IsTrue(stat.IsResource);
            Assert.AreEqual((FixedPoint)value, stat.value, message);
            Assert.AreEqual((FixedPoint)max, stat.max, message);
        }

        private static uint StatVersion(EntityManager entityManager, Entity unit)
        {
            var handle = entityManager.GetBufferTypeHandle<LockstepStat>(true);
            return entityManager.GetChunk(unit).GetChangeVersion(ref handle);
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct IdJob : IJob
        {
            public NativeArray<int> ids;

            public void Execute()
            {
                ids[0] = LockstepStats.Id(ByteStat.Value);
                ids[1] = LockstepStats.Id(ShortStat.Value);
                ids[2] = LockstepStats.Id(IntStat.Value);
                ids[3] = LockstepStats.Id(LongStat.Value);
            }
        }
    }
}
