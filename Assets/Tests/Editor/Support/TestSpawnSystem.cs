using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Spawns registry prefab 0 on tick 3, moves it along x every tick and destroys it on tick 30.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    public partial struct TestSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var tick = SystemAPI.GetSingleton<LockstepTime>().tick;
            if (tick == 3)
            {
                var prefab = SystemAPI.GetSingletonBuffer<LockstepPrefabElement>(true)[0].prefab;
                var spawned = state.EntityManager.Instantiate(prefab);
                state.EntityManager.AddComponent<TestSpawnedTag>(spawned);
                state.EntityManager.SetComponentData(spawned, LockstepTransform.FromPosition(new FixedVector3(10, 0, 0)));
            }

            foreach (var transform in SystemAPI.Query<RefRW<LockstepTransform>>().WithAll<TestSpawnedTag>())
            {
                transform.ValueRW.position.x += FixedPoint.One;
            }

            if (tick == 30)
            {
                var query = SystemAPI.QueryBuilder().WithAll<TestSpawnedTag>().Build();
                state.EntityManager.DestroyEntity(query);
            }
        }
    }
}
