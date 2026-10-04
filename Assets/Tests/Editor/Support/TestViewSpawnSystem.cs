using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Views;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// A timeline for the view tests. Tick 3: an entity with key A at x = 10 (moving +1 every tick) and the
    /// <see cref="TestViewElement"/>s [1]. Tick 8: its <see cref="TestViewHidden"/> turns on, tick 12: off again. Ticks 10
    /// and 20: its data becomes 1, then 2. Tick 15: its elements become [1, 2]. Tick 25: its key becomes B. Tick 30: it is
    /// destroyed. Tick 33: a second key A entity with data 2 and the elements [3]. Tick 40: it is destroyed.
    /// </summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    public partial struct TestViewSpawnSystem : ISystem
    {
        public const string KEY_A = "TestA";
        public const string KEY_B = "TestB";

        public void OnUpdate(ref SystemState state)
        {
            var tick = SystemAPI.GetSingleton<LockstepTime>().tick;
            var query = SystemAPI.QueryBuilder().WithAll<TestViewData>().Build();
            if (tick == 3 || tick == 33)
            {
                var entity = state.EntityManager.CreateEntity(typeof(EntityViewKey), typeof(LockstepTransform), typeof(LockstepTransformPrevious),
                    typeof(TestViewData), typeof(TestViewHidden), typeof(TestViewElement));
                state.EntityManager.SetComponentData(entity, new EntityViewKey(KEY_A));
                state.EntityManager.SetComponentData(entity, LockstepTransform.FromPosition(new FixedVector3(10, 0, 0)));
                state.EntityManager.SetComponentData(entity, new TestViewData { value = tick == 3 ? 0 : 2 });
                state.EntityManager.SetComponentEnabled<TestViewHidden>(entity, false);
                state.EntityManager.GetBuffer<TestViewElement>(entity).Add(new TestViewElement { value = tick == 3 ? 1 : 3 });
            }

            // Read-only access to TestViewData, so only the writes below change its version.
            foreach (var transform in SystemAPI.Query<RefRW<LockstepTransform>>().WithAll<TestViewData>())
            {
                transform.ValueRW.position.x += FixedPoint.One;
            }

            if (tick == 8 || tick == 12)
            {
                state.EntityManager.SetComponentEnabled<TestViewHidden>(query.GetSingletonEntity(), tick == 8);
            }
            if (tick == 10 || tick == 20)
            {
                var entity = query.GetSingletonEntity();
                state.EntityManager.SetComponentData(entity, new TestViewData { value = tick == 10 ? 1 : 2 });
            }
            if (tick == 15)
            {
                state.EntityManager.GetBuffer<TestViewElement>(query.GetSingletonEntity()).Add(new TestViewElement { value = 2 });
            }
            if (tick == 25)
            {
                state.EntityManager.SetComponentData(query.GetSingletonEntity(), new EntityViewKey(KEY_B));
            }
            if (tick == 30 || tick == 40)
            {
                state.EntityManager.DestroyEntity(query);
            }
        }
    }
}
