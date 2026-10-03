using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Lockstep.Tests
{
    public class SimulationTests
    {
        private LockstepSimulation _simulation;

        [SetUp]
        public void SetUp()
        {
            _simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(TestGameplaySystem)));
        }

        [TearDown]
        public void TearDown()
        {
            _simulation.Dispose();
        }

        private EntityManager EntityManager => _simulation.World.EntityManager;

        [Test]
        public void Time_AdvancesOneTickPerStep()
        {
            for (var i = 0; i < 45; i++)
            {
                TestUtility.Step(_simulation);
            }
            using (var query = EntityManager.CreateEntityQuery(typeof(LockstepTime)))
            {
                var time = query.GetSingleton<LockstepTime>();
                Assert.AreEqual(44, time.tick, "time holds the tick being simulated");
                Assert.AreEqual(FixedPoint.FromFraction(44, 30), time.elapsedTime);
                Assert.AreEqual(FixedPoint.FromFraction(1, 30), time.deltaTime);
            }
            Assert.AreEqual(45, _simulation.Tick);
        }

        [Test]
        public void Join_CreatesPlayerFlaggedForOneTick()
        {
            TestUtility.Step(_simulation, new FrameBuilder().Join(2, 7, 8).Build());
            var player = TestUtility.PlayerEntity(_simulation, 2);
            Assert.AreNotEqual(Entity.Null, player);
            Assert.IsTrue(EntityManager.IsComponentEnabled<LockstepPlayerJoined>(player));
            Assert.IsFalse(EntityManager.IsComponentEnabled<LockstepPlayerLeft>(player));
            var data = EntityManager.GetComponentData<LockstepPlayer>(player);
            Assert.AreEqual(2, data.slot);
            Assert.AreEqual(0, data.joinTick);
            Assert.AreEqual(2, data.joinData.Length);
            Assert.AreEqual(8, data.joinData[1]);
            Assert.IsTrue(EntityManager.HasComponent<TestPlayerState>(player), "game systems see the joined flag on the join tick");

            TestUtility.Step(_simulation);
            Assert.IsFalse(EntityManager.IsComponentEnabled<LockstepPlayerJoined>(player));
        }

        [Test]
        public void Leave_FlagsThePlayerThenRemovesIt()
        {
            TestUtility.Step(_simulation, new FrameBuilder().Join(0).Join(1).Build());
            var leaving = TestUtility.PlayerEntity(_simulation, 1);
            TestUtility.Step(_simulation, new FrameBuilder().Leave(1).Build());
            Assert.IsTrue(EntityManager.IsComponentEnabled<LockstepPlayerLeft>(leaving));
            Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(EntityManager));

            TestUtility.Step(_simulation);
            Assert.IsFalse(EntityManager.Exists(leaving));
            Assert.AreEqual(Entity.Null, TestUtility.PlayerEntity(_simulation, 1));
            Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(EntityManager));

            TestUtility.Step(_simulation, new FrameBuilder().Join(1).Build());
            Assert.AreNotEqual(Entity.Null, TestUtility.PlayerEntity(_simulation, 1), "the slot can be reused");
        }

        [Test]
        public void Input_PersistsUntilChangedAndKeepsPrevious()
        {
            TestUtility.Step(_simulation, new FrameBuilder().Join(0).Input(0, new TestInput { moveX = 3, buttons = 1 }).Build());
            TestUtility.Step(_simulation);
            TestUtility.Step(_simulation, new FrameBuilder().Input(0, new TestInput { moveX = -1 }).Build());

            var input = EntityManager.GetComponentData<LockstepPlayerInput>(TestUtility.PlayerEntity(_simulation, 0));
            Assert.AreEqual(-1, input.Get<TestInput>().moveX);
            Assert.AreEqual(3, input.GetPrevious<TestInput>().moveX);
            Assert.IsTrue(input.HasChanged());

            var state = TestUtility.PlayerState(_simulation, 0);
            Assert.AreEqual(FixedPoint.FromFraction(1, 30) * (3 + 3 - 1), state.position.x);
            Assert.AreEqual(1, state.jumps, "the button edge counts once");
        }

        [Test]
        public void Commands_AreVisibleForExactlyOneTick()
        {
            TestUtility.Step(_simulation, new FrameBuilder().Join(0).Command(0, new TestCommand { value = 5 }).Command(0, new TestCommand { value = 7 }).Build());
            var player = TestUtility.PlayerEntity(_simulation, 0);
            Assert.AreEqual(2, EntityManager.GetBuffer<LockstepCommand>(player).Length);
            Assert.AreEqual(12, TestUtility.PlayerState(_simulation, 0).commandSum);

            TestUtility.Step(_simulation);
            Assert.AreEqual(0, EntityManager.GetBuffer<LockstepCommand>(player).Length);
            Assert.AreEqual(12, TestUtility.PlayerState(_simulation, 0).commandSum);
        }

        [Test]
        public void SameFrames_GiveSameStateEveryTick()
        {
            using (var other = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(TestGameplaySystem))))
            {
                var random = new System.Random(4);
                for (var tick = 0; tick < 200; tick++)
                {
                    var builder = new FrameBuilder();
                    if (tick == 0)
                    {
                        builder.Join(0).Join(3);
                    }
                    if (tick == 50)
                    {
                        builder.Join(1);
                    }
                    if (tick == 120)
                    {
                        builder.Leave(3);
                    }
                    if (random.Next(3) == 0)
                    {
                        builder.Input(0, new TestInput { moveX = random.Next(-2, 3), buttons = random.Next(2) });
                    }
                    if (random.Next(10) == 0)
                    {
                        builder.Command(0, new TestCommand { value = random.Next(100) });
                    }
                    var frame = builder.Build();
                    TestUtility.Step(_simulation, frame);
                    TestUtility.Step(other, frame);
                    if (tick == 0)
                    {
                        ChecksumTests.AssertSameState(_simulation.World.EntityManager, other.World.EntityManager);
                    }
                    Assert.AreEqual(_simulation.ComputeChecksum(), other.ComputeChecksum(), $"tick {tick}");
                }
            }
        }

        [Test]
        public void DifferentInput_GivesDifferentState()
        {
            using (var other = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(TestGameplaySystem))))
            {
                TestUtility.Step(_simulation, new FrameBuilder().Join(0).Input(0, new TestInput { moveX = 1 }).Build());
                TestUtility.Step(other, new FrameBuilder().Join(0).Input(0, new TestInput { moveX = 2 }).Build());
                Assert.AreNotEqual(_simulation.ComputeChecksum(), other.ComputeChecksum());
            }
        }

        [Test]
        public void MalformedFrame_IsReportedAndSkipped()
        {
            LogAssert.Expect(LogType.Error, new Regex("Malformed frame"));
            TestUtility.Step(_simulation, new byte[] { 3, 0, (byte)LockstepFrameRecordFlags.Input, 1 });
            Assert.AreEqual(1, _simulation.Tick);
            TestUtility.Step(_simulation, new FrameBuilder().Join(0).Build());
            Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(EntityManager));
        }

        [Test]
        public void EntityIds_AreAssignedInOrderAndResolvable()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options()))
            {
                var entityManager = simulation.World.EntityManager;
                var first = entityManager.CreateEntity(typeof(LockstepEntityId));
                var second = entityManager.CreateEntity(typeof(LockstepEntityId));
                TestUtility.Step(simulation);
                Assert.AreEqual(1u, entityManager.GetComponentData<LockstepEntityId>(first).value);
                Assert.AreEqual(2u, entityManager.GetComponentData<LockstepEntityId>(second).value);

                entityManager.DestroyEntity(first);
                var third = entityManager.CreateEntity(typeof(LockstepEntityId));
                TestUtility.Step(simulation);
                Assert.AreEqual(3u, entityManager.GetComponentData<LockstepEntityId>(third).value, "ids are never reused");

                using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc
                       {
                           All = new[] { ComponentType.ReadOnly<LockstepEntityIdMap>() },
                           Options = EntityQueryOptions.IncludeSystems,
                       }))
                {
                    var map = query.GetSingleton<LockstepEntityIdMap>();
                    Assert.IsTrue(map.TryGetEntity(3, out var resolved));
                    Assert.AreEqual(third, resolved);
                    Assert.IsFalse(map.TryGetEntity(1, out _));
                }
            }
        }

        [Test]
        public void TransformHistory_CapturesTheStartOfEveryTick()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options()))
            {
                var entityManager = simulation.World.EntityManager;
                var entity = entityManager.CreateEntity(typeof(LockstepTransform), typeof(LockstepTransformPrevious));
                entityManager.SetComponentData(entity, LockstepTransform.FromPosition(new FixedVector3(1, 0, 0)));
                TestUtility.Step(simulation);
                var previous = entityManager.GetComponentData<LockstepTransformPrevious>(entity);
                Assert.IsTrue(previous.IsCapturedAt(0));
                Assert.AreEqual(new FixedVector3(1, 0, 0), previous.position);
            }
        }
    }
}
