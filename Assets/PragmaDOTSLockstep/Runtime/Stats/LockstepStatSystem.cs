using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// Keeps <see cref="LockstepStat.value"/> up to date: removes the modifiers whose end tick has come and recalculates the
    /// stats of the entities whose base values or modifiers changed. Change base values and modifiers in systems that update
    /// before this one and read the values after it, and both happen on the same tick.
    /// </summary>
    /// <remarks>
    /// What to recalculate is decided per chunk by the change versions of the two buffers: stats nothing touched are neither
    /// recalculated nor written, so systems that follow a stat with a change filter run only when it may have changed. A
    /// recalculation gives the same values however often it runs, so the state never depends on the versions themselves.
    /// Chunks are processed in parallel, each writing only its own entities.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [BurstCompile]
    public partial struct LockstepStatSystem : ISystem
    {
        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = SystemAPI.QueryBuilder().WithAllRW<LockstepStat, LockstepStatModifier>().Build();
            state.RequireForUpdate(_query);
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new UpdateJob
            {
                tick = SystemAPI.GetSingleton<LockstepTime>().tick,
                lastSystemVersion = state.LastSystemVersion,
                statHandle = SystemAPI.GetBufferTypeHandle<LockstepStat>(),
                modifierHandle = SystemAPI.GetBufferTypeHandle<LockstepStatModifier>(),
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        private struct UpdateJob : IJobChunk
        {
            public int tick;
            public uint lastSystemVersion;
            public BufferTypeHandle<LockstepStat> statHandle;
            public BufferTypeHandle<LockstepStatModifier> modifierHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // Read-only first: taking a buffer for writing marks the whole chunk changed.
                var hasExpired = HasExpired(chunk.GetBufferAccessorRO(ref modifierHandle));
                if (!hasExpired && !chunk.DidChange(ref statHandle, lastSystemVersion) && !chunk.DidChange(ref modifierHandle, lastSystemVersion))
                {
                    return;
                }

                var stats = chunk.GetBufferAccessorRW(ref statHandle);
                var modifiers = hasExpired ? chunk.GetBufferAccessorRW(ref modifierHandle) : chunk.GetBufferAccessorRO(ref modifierHandle);
                for (var i = 0; i < chunk.Count; i++)
                {
                    var entityModifiers = modifiers[i];
                    if (hasExpired)
                    {
                        RemoveExpired(entityModifiers, tick);
                    }
                    Recalculate(stats[i], entityModifiers.AsNativeArray());
                }
            }

            private bool HasExpired(BufferAccessor<LockstepStatModifier> modifiers)
            {
                for (var i = 0; i < modifiers.Length; i++)
                {
                    var entityModifiers = modifiers[i];
                    for (var j = 0; j < entityModifiers.Length; j++)
                    {
                        if (entityModifiers[j].IsExpired(tick))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            private static void RemoveExpired(DynamicBuffer<LockstepStatModifier> modifiers, int tick)
            {
                var count = 0;
                for (var i = 0; i < modifiers.Length; i++)
                {
                    var modifier = modifiers[i];
                    if (!modifier.IsExpired(tick))
                    {
                        modifiers[count++] = modifier;
                    }
                }
                modifiers.ResizeUninitialized(count);
            }

            private static void Recalculate(DynamicBuffer<LockstepStat> stats, NativeArray<LockstepStatModifier> modifiers)
            {
                for (var i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    stat.value = LockstepStats.Calculate(stat.type, stat.baseValue, modifiers);
                    stats[i] = stat;
                }
            }
        }
    }
}
