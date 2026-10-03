using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    internal static unsafe class TestUtility
    {
        public static LockstepSessionConfig Config(int tickRate = 30, int maxPlayers = 4, uint seed = 7, int checksumInterval = 10)
        {
            return new LockstepSessionConfig
            {
                TickRate = tickRate,
                MaxPlayers = maxPlayers,
                InputSize = UnsafeUtility.SizeOf<TestInput>(),
                Seed = seed,
                ChecksumInterval = checksumInterval,
            };
        }

        public static LockstepSimulationOptions Options(params Type[] systems)
        {
            return new LockstepSimulationOptions { AutoDiscoverSystems = false, AdditionalSystems = systems };
        }

        public static void Step(LockstepSimulation simulation, byte[] frame)
        {
            fixed (byte* data = frame)
            {
                simulation.Step(data, frame.Length);
            }
        }

        public static void Step(LockstepSimulation simulation) => Step(simulation, new FrameBuilder().Build());

        public static int Count<T>(EntityManager entityManager) where T : unmanaged, IComponentData
        {
            using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>()))
            {
                return query.CalculateEntityCount();
            }
        }

        public static Entity PlayerEntity(LockstepSimulation simulation, int slot)
        {
            var entityManager = simulation.World.EntityManager;
            using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LockstepPlayerSlot>()))
            {
                return entityManager.GetBuffer<LockstepPlayerSlot>(query.GetSingletonEntity())[slot].player;
            }
        }

        public static TestPlayerState PlayerState(LockstepSimulation simulation, int slot)
        {
            return simulation.World.EntityManager.GetComponentData<TestPlayerState>(PlayerEntity(simulation, slot));
        }

        /// <summary>Both clients hashed the same state on every tick they both checked, and on enough of them.</summary>
        public static void AssertSameChecksums(LockstepClient a, LockstepClient b, int minimumCommonTicks)
        {
            var byTick = new Dictionary<int, ulong>();
            foreach (var pair in a.LocalChecksums)
            {
                byTick[pair.Key] = pair.Value;
            }
            var common = 0;
            foreach (var pair in b.LocalChecksums)
            {
                if (!byTick.TryGetValue(pair.Key, out var hash))
                {
                    continue;
                }
                Assert.AreEqual(hash, pair.Value, $"state differs at tick {pair.Key}");
                common++;
            }
            Assert.GreaterOrEqual(common, minimumCommonTicks);
        }
    }
}
