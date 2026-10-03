using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Examples
{
    /// <summary>Spawns an avatar on the tick a player joins and removes it on the tick the player leaves.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [BurstCompile]
    public partial struct ArenaPlayerLifecycleSystem : ISystem
    {
        private EntityArchetype _avatarArchetype;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _avatarArchetype = state.EntityManager.CreateArchetype(stackalloc ComponentType[]
            {
                ComponentType.ReadWrite<LockstepTransform>(),
                ComponentType.ReadWrite<LockstepTransformPrevious>(),
                ComponentType.ReadWrite<ArenaAvatar>(),
                ComponentType.ReadWrite<LockstepEntityId>(),
            });
            state.RequireForUpdate<LockstepRandom>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var joined = new NativeList<int>(Allocator.Temp);
            foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>())
            {
                joined.Add(player.ValueRO.slot);
            }

            var left = new NativeList<int>(Allocator.Temp);
            foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerLeft>())
            {
                left.Add(player.ValueRO.slot);
            }

            if (left.Length > 0)
            {
                var removed = new NativeList<Entity>(Allocator.Temp);
                foreach (var (avatar, entity) in SystemAPI.Query<RefRO<ArenaAvatar>>().WithEntityAccess())
                {
                    for (var i = 0; i < left.Length; i++)
                    {
                        if (left[i] == avatar.ValueRO.slot)
                        {
                            removed.Add(entity);
                        }
                    }
                }
                state.EntityManager.DestroyEntity(removed.AsArray());
            }

            ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;
            var range = ArenaRules.HalfSize - 2;
            for (var i = 0; i < joined.Length; i++)
            {
                var spawn = random.NextVector2(new FixedVector2(-range), new FixedVector2(range));
                var avatar = state.EntityManager.CreateEntity(_avatarArchetype);
                state.EntityManager.SetComponentData(avatar, LockstepTransform.FromPosition(new FixedVector3(spawn.x, FixedPoint.Zero, spawn.y)));
                state.EntityManager.SetComponentData(avatar, new ArenaAvatar
                {
                    slot = joined[i],
                    facing = FixedVector2.Up,
                    colorIndex = joined[i] % ArenaRules.COLOR_COUNT,
                });
            }
        }
    }
}
