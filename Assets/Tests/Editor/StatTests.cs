using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Stats;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    public class StatTests
    {
        private const int HEALTH = 1;
        private const int SPEED = 2;

        private static readonly LockstepStatSource Ability = new LockstepStatSource(1, 7);
        private static readonly LockstepStatSource Research = new LockstepStatSource(2, 7);

        [Test]
        public void Calculate_AddsFlatThenAdditiveThenMultiplicative()
        {
            var modifiers = new NativeArray<LockstepStatModifier>(new[]
            {
                LockstepStatModifier.Multiplicative(HEALTH, FixedPoint.Half),
                LockstepStatModifier.Additive(HEALTH, FixedPoint.FromFraction(1, 4)),
                LockstepStatModifier.Flat(HEALTH, 20),
                LockstepStatModifier.Multiplicative(HEALTH, -FixedPoint.FromFraction(1, 4)),
                LockstepStatModifier.Additive(HEALTH, FixedPoint.FromFraction(1, 8)),
                LockstepStatModifier.Flat(SPEED, 1000),
            }, Allocator.Temp);
            using (modifiers)
            {
                // (100 + 20) * (1 + 1/4 + 1/8) * (1 + 1/2) * (1 - 1/4)
                Assert.AreEqual(FixedPoint.FromFraction(1485, 8), LockstepStats.Calculate(HEALTH, 100, modifiers));
                Assert.AreEqual((FixedPoint)1004, LockstepStats.Calculate(SPEED, 4, modifiers), "only the modifiers of the stat count");
                Assert.AreEqual((FixedPoint)5, LockstepStats.Calculate(3, 5, modifiers), "a stat without modifiers keeps its base");
            }
        }

        [Test]
        public void System_AppliesModifiersAndRemovesThemBySource()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(LockstepStatSystem))))
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateUnit(entityManager, (HEALTH, 100), (SPEED, 4));
                TestUtility.Step(simulation);

                var modifiers = entityManager.GetBuffer<LockstepStatModifier>(unit);
                modifiers.Add(LockstepStatModifier.Additive(SPEED, FixedPoint.Half, Ability));
                modifiers.Add(LockstepStatModifier.Flat(HEALTH, 30, Ability));
                modifiers.Add(LockstepStatModifier.Flat(HEALTH, 50, Research));
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)6, Value(entityManager, unit, SPEED));
                Assert.AreEqual((FixedPoint)180, Value(entityManager, unit, HEALTH));

                Assert.AreEqual(2, LockstepStats.RemoveModifiers(entityManager.GetBuffer<LockstepStatModifier>(unit), Ability));
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)4, Value(entityManager, unit, SPEED), "the ability ended");
                Assert.AreEqual((FixedPoint)150, Value(entityManager, unit, HEALTH), "the research stays");
            }
        }

        [Test]
        public void TimedModifier_EndsOnItsEndTick()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(LockstepStatSystem))))
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateUnit(entityManager, (SPEED, 4));
                TestUtility.Step(simulation);

                // Added before tick T for three ticks: it applies on T, T + 1 and T + 2.
                var endTick = simulation.Tick + 3;
                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.Multiplicative(SPEED, 1, default, endTick));
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
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(LockstepStatSystem))))
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateUnit(entityManager, (HEALTH, 100));
                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.Additive(HEALTH, FixedPoint.Half, Research));
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)150, Value(entityManager, unit, HEALTH));

                Assert.IsTrue(LockstepStats.TrySetBase(entityManager.GetBuffer<LockstepStat>(unit), HEALTH, 200));
                Assert.IsFalse(LockstepStats.TrySetBase(entityManager.GetBuffer<LockstepStat>(unit), SPEED, 5), "the unit has no speed");
                TestUtility.Step(simulation);
                Assert.AreEqual((FixedPoint)300, Value(entityManager, unit, HEALTH));
                Assert.IsTrue(LockstepStats.TryGetBase(entityManager.GetBuffer<LockstepStat>(unit, true), HEALTH, out var baseValue));
                Assert.AreEqual((FixedPoint)200, baseValue);
                Assert.IsFalse(LockstepStats.TryGetValue(entityManager.GetBuffer<LockstepStat>(unit, true), SPEED, out _));
            }
        }

        [Test]
        public void UnchangedStats_AreNotWritten()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(LockstepStatSystem))))
            {
                var entityManager = simulation.World.EntityManager;
                var unit = CreateUnit(entityManager, (HEALTH, 100));
                TestUtility.Step(simulation);
                var version = StatVersion(entityManager, unit);

                for (var i = 0; i < 5; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(version, StatVersion(entityManager, unit), "nothing changed, so nothing was written");

                entityManager.GetBuffer<LockstepStatModifier>(unit).Add(LockstepStatModifier.Flat(HEALTH, 1, default, simulation.Tick + 2));
                TestUtility.Step(simulation);
                var applied = StatVersion(entityManager, unit);
                Assert.AreNotEqual(version, applied, "the new modifier was applied");
                Assert.AreEqual((FixedPoint)101, Value(entityManager, unit, HEALTH));

                TestUtility.Step(simulation);
                Assert.AreEqual(applied, StatVersion(entityManager, unit), "a running modifier writes nothing");

                TestUtility.Step(simulation);
                Assert.AreNotEqual(applied, StatVersion(entityManager, unit), "the expired modifier was removed");
                Assert.AreEqual((FixedPoint)100, Value(entityManager, unit, HEALTH));
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
                AssertStatsFollowTheirModifiers(first);
                AssertStatsFollowTheirModifiers(late);
            }
        }

        private static LockstepSimulationOptions SessionOptions()
        {
            var options = TestUtility.Options(typeof(LockstepStatSystem), typeof(TestStatSystem));
            options.Initialize = world =>
            {
                var entityManager = world.EntityManager;
                for (var i = 0; i < 9; i++)
                {
                    var unit = CreateUnit(entityManager, (0, 10 + i), (1, 20 + i), (2, 30 + i));
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

        // Every value is what its base and modifiers give: the stat system kept up with every change.
        private static void AssertStatsFollowTheirModifiers(LockstepClient client)
        {
            var entityManager = client.Simulation.World.EntityManager;
            using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LockstepStat>(), ComponentType.ReadOnly<LockstepStatModifier>()))
            {
                var units = query.ToEntityArray(Allocator.Temp);
                var modified = 0;
                foreach (var unit in units)
                {
                    var stats = entityManager.GetBuffer<LockstepStat>(unit, true);
                    var modifiers = entityManager.GetBuffer<LockstepStatModifier>(unit, true).AsNativeArray();
                    for (var i = 0; i < stats.Length; i++)
                    {
                        Assert.AreEqual(LockstepStats.Calculate(stats[i].type, stats[i].baseValue, modifiers), stats[i].value);
                        modified += stats[i].value != stats[i].baseValue ? 1 : 0;
                    }
                }
                Assert.AreEqual(9, units.Length);
                Assert.Greater(modified, 0, "the modifiers changed some stats");
            }
        }

        private static Entity CreateUnit(EntityManager entityManager, params (int Type, FixedPoint Base)[] stats)
        {
            var unit = entityManager.CreateEntity(typeof(LockstepStat), typeof(LockstepStatModifier));
            var buffer = entityManager.GetBuffer<LockstepStat>(unit);
            foreach (var stat in stats)
            {
                buffer.Add(LockstepStat.Create(stat.Type, stat.Base));
            }
            return unit;
        }

        private static FixedPoint Value(EntityManager entityManager, Entity unit, int type)
        {
            Assert.IsTrue(LockstepStats.TryGetValue(entityManager.GetBuffer<LockstepStat>(unit, true), type, out var value), $"stat {type}");
            return value;
        }

        private static uint StatVersion(EntityManager entityManager, Entity unit)
        {
            var handle = entityManager.GetBufferTypeHandle<LockstepStat>(true);
            return entityManager.GetChunk(unit).GetChangeVersion(ref handle);
        }
    }
}
