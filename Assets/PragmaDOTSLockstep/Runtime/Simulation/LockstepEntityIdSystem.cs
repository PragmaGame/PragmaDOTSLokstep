using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Assigns <see cref="LockstepEntityId"/>s to new entities and rebuilds <see cref="LockstepEntityIdMap"/>.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(LockstepEndSimulationEntityCommandBufferSystem))]
    [BurstCompile]
    public partial struct LockstepEntityIdSystem : ISystem
    {
        private NativeHashMap<uint, Entity> _map;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _map = new NativeHashMap<uint, Entity>(256, Allocator.Persistent);
            state.EntityManager.AddComponentData(state.SystemHandle, new LockstepEntityIdMap { map = _map });
            state.RequireForUpdate<LockstepEntityIdCounter>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _map.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var counter = ref SystemAPI.GetSingletonRW<LockstepEntityIdCounter>().ValueRW;
            _map.Clear();
            foreach (var (id, entity) in SystemAPI.Query<RefRW<LockstepEntityId>>().WithEntityAccess())
            {
                if (id.ValueRO.value == 0)
                {
                    id.ValueRW.value = ++counter.last;
                }
                _map[id.ValueRO.value] = entity;
            }
        }
    }
}
