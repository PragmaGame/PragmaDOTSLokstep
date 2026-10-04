using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// Keeps <see cref="LockstepStat.value"/> up to date. Removes the modifiers and grants whose end tick has come, then
    /// recalculates the attributes of the entities whose base values, modifiers, grantors or targets changed, or whose
    /// grantors' grants did; fits capped resources to their caps and applies the tick's <see cref="LockstepStatChange"/>s.
    /// Change stats in systems that update before this one and read them after it, and both happen on the same tick.
    /// </summary>
    /// <remarks>
    /// What to recalculate is decided per chunk by change versions: stats nothing touched are neither recalculated nor
    /// written, so systems that follow a stat with a change filter run only when it may have changed. A recalculation
    /// gives the same values however often it runs, so the state never depends on the versions themselves. Chunks are
    /// processed in parallel, each writing only its own entities.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [BurstCompile]
    public partial struct LockstepStatSystem : ISystem
    {
        private EntityQuery _statQuery;
        private EntityQuery _grantQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
            _statQuery = SystemAPI.QueryBuilder().WithAllRW<LockstepStat>().Build();
            _grantQuery = SystemAPI.QueryBuilder().WithAllRW<LockstepStatGrant>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_statQuery.IsEmptyIgnoreFilter && _grantQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            var tick = SystemAPI.GetSingleton<LockstepTime>().tick;
            // Grants expire first: the stat update reads them and counts their removal as a change.
            state.Dependency = new ExpireGrantsJob
            {
                tick = tick,
                grantHandle = SystemAPI.GetBufferTypeHandle<LockstepStatGrant>(),
            }.ScheduleParallel(_grantQuery, state.Dependency);

            state.Dependency = new UpdateJob
            {
                tick = tick,
                lastSystemVersion = state.LastSystemVersion,
                statHandle = SystemAPI.GetBufferTypeHandle<LockstepStat>(),
                modifierHandle = SystemAPI.GetBufferTypeHandle<LockstepStatModifier>(),
                changeHandle = SystemAPI.GetBufferTypeHandle<LockstepStatChange>(),
                grantorHandle = SystemAPI.GetBufferTypeHandle<LockstepStatGrantor>(),
                targetHandle = SystemAPI.GetBufferTypeHandle<LockstepStatTarget>(true),
                grantHandle = SystemAPI.GetBufferTypeHandle<LockstepStatGrant>(true),
                grants = SystemAPI.GetBufferLookup<LockstepStatGrant>(true),
                storage = SystemAPI.GetEntityStorageInfoLookup(),
            }.ScheduleParallel(_statQuery, state.Dependency);
        }

        [BurstCompile]
        private struct ExpireGrantsJob : IJobChunk
        {
            public int tick;
            public BufferTypeHandle<LockstepStatGrant> grantHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // Read-only first: taking a buffer for writing marks the whole chunk changed.
                if (!HasExpired(chunk.GetBufferAccessorRO(ref grantHandle)))
                {
                    return;
                }
                var grants = chunk.GetBufferAccessorRW(ref grantHandle);
                for (var i = 0; i < grants.Length; i++)
                {
                    var entityGrants = grants[i];
                    var count = 0;
                    for (var j = 0; j < entityGrants.Length; j++)
                    {
                        var grant = entityGrants[j];
                        if (!grant.modifier.IsExpired(tick))
                        {
                            entityGrants[count++] = grant;
                        }
                    }
                    entityGrants.ResizeUninitialized(count);
                }
            }

            private bool HasExpired(BufferAccessor<LockstepStatGrant> grants)
            {
                for (var i = 0; i < grants.Length; i++)
                {
                    var entityGrants = grants[i];
                    for (var j = 0; j < entityGrants.Length; j++)
                    {
                        if (entityGrants[j].modifier.IsExpired(tick))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        [BurstCompile]
        private struct UpdateJob : IJobChunk
        {
            public int tick;
            public uint lastSystemVersion;
            public BufferTypeHandle<LockstepStat> statHandle;
            public BufferTypeHandle<LockstepStatModifier> modifierHandle;
            public BufferTypeHandle<LockstepStatChange> changeHandle;
            public BufferTypeHandle<LockstepStatGrantor> grantorHandle;
            [ReadOnly] public BufferTypeHandle<LockstepStatTarget> targetHandle;
            // Only for the change versions of the grantors' chunks.
            [ReadOnly] public BufferTypeHandle<LockstepStatGrant> grantHandle;
            [ReadOnly] public BufferLookup<LockstepStatGrant> grants;
            [ReadOnly] public EntityStorageInfoLookup storage;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var hasModifiers = chunk.Has(ref modifierHandle);
                var hasChanges = chunk.Has(ref changeHandle);
                var hasGrantors = chunk.Has(ref grantorHandle);
                var hasTargets = chunk.Has(ref targetHandle);

                // Read-only first: taking a buffer for writing marks the whole chunk changed.
                var hasExpired = hasModifiers && HasExpired(chunk.GetBufferAccessorRO(ref modifierHandle));
                var hasPendingChanges = hasChanges && HasAny(chunk.GetBufferAccessorRO(ref changeHandle));
                var hasLostGrantor = false;
                var hasGrantChanged = hasGrantors && DidGrantsChange(chunk.GetBufferAccessorRO(ref grantorHandle), out hasLostGrantor);
                if (!hasExpired && !hasPendingChanges && !hasGrantChanged && !chunk.DidChange(ref statHandle, lastSystemVersion) &&
                    !(hasModifiers && chunk.DidChange(ref modifierHandle, lastSystemVersion)) &&
                    !(hasGrantors && chunk.DidChange(ref grantorHandle, lastSystemVersion)) &&
                    !(hasTargets && chunk.DidChange(ref targetHandle, lastSystemVersion)))
                {
                    return;
                }

                var stats = chunk.GetBufferAccessorRW(ref statHandle);
                var modifiers = hasModifiers
                    ? hasExpired ? chunk.GetBufferAccessorRW(ref modifierHandle) : chunk.GetBufferAccessorRO(ref modifierHandle)
                    : default;
                var changes = hasPendingChanges ? chunk.GetBufferAccessorRW(ref changeHandle) : default;
                var grantors = hasGrantors
                    ? hasLostGrantor ? chunk.GetBufferAccessorRW(ref grantorHandle) : chunk.GetBufferAccessorRO(ref grantorHandle)
                    : default;
                var targets = hasTargets ? chunk.GetBufferAccessorRO(ref targetHandle) : default;
                var applied = new NativeList<LockstepStatModifier>(Allocator.Temp);
                for (var i = 0; i < chunk.Count; i++)
                {
                    applied.Clear();
                    if (hasModifiers)
                    {
                        var entityModifiers = modifiers[i];
                        if (hasExpired)
                        {
                            RemoveExpired(entityModifiers, tick);
                        }
                        applied.AddRange(entityModifiers.AsNativeArray());
                    }
                    if (hasGrantors)
                    {
                        var entityGrantors = grantors[i];
                        if (hasLostGrantor)
                        {
                            RemoveLost(entityGrantors);
                        }
                        AddGrants(applied, entityGrantors, hasTargets ? targets[i] : default, hasTargets);
                    }

                    var entityStats = stats[i];
                    UpdateAttributes(entityStats, applied.AsArray());
                    if (hasPendingChanges)
                    {
                        var entityChanges = changes[i];
                        UpdateResources(entityStats, entityChanges.AsNativeArray());
                        entityChanges.Clear();
                    }
                    else
                    {
                        UpdateResources(entityStats, default);
                    }
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

            private static bool HasAny(BufferAccessor<LockstepStatChange> changes)
            {
                for (var i = 0; i < changes.Length; i++)
                {
                    if (changes[i].Length > 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            // A grantor that is gone, or whose chunk had its grants written since the last update (expired ones included).
            private bool DidGrantsChange(BufferAccessor<LockstepStatGrantor> grantors, out bool hasLostGrantor)
            {
                hasLostGrantor = false;
                var isChanged = false;
                for (var i = 0; i < grantors.Length; i++)
                {
                    var entityGrantors = grantors[i];
                    for (var j = 0; j < entityGrantors.Length; j++)
                    {
                        var grantor = entityGrantors[j].entity;
                        if (!storage.Exists(grantor))
                        {
                            hasLostGrantor = true;
                            return true;
                        }
                        isChanged = isChanged || storage[grantor].Chunk.DidChange(ref grantHandle, lastSystemVersion);
                    }
                }
                return isChanged;
            }

            private void RemoveLost(DynamicBuffer<LockstepStatGrantor> grantors)
            {
                var count = 0;
                for (var i = 0; i < grantors.Length; i++)
                {
                    var grantor = grantors[i];
                    if (storage.Exists(grantor.entity))
                    {
                        grantors[count++] = grantor;
                    }
                }
                grantors.ResizeUninitialized(count);
            }

            private void AddGrants(NativeList<LockstepStatModifier> applied, DynamicBuffer<LockstepStatGrantor> grantors,
                                   DynamicBuffer<LockstepStatTarget> targets, bool hasTargets)
            {
                for (var i = 0; i < grantors.Length; i++)
                {
                    if (!grants.TryGetBuffer(grantors[i].entity, out var grantorGrants))
                    {
                        continue;
                    }
                    for (var j = 0; j < grantorGrants.Length; j++)
                    {
                        var grant = grantorGrants[j];
                        if (grant.target == LockstepStatGrant.ANY || (hasTargets && HasTarget(targets, grant.target)))
                        {
                            applied.Add(grant.modifier);
                        }
                    }
                }
            }

            private static bool HasTarget(DynamicBuffer<LockstepStatTarget> targets, int target)
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    if (targets[i].value == target)
                    {
                        return true;
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

            private static void UpdateAttributes(DynamicBuffer<LockstepStat> stats, NativeArray<LockstepStatModifier> modifiers)
            {
                for (var i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    if (stat.IsAttribute)
                    {
                        stat.value = LockstepStats.Calculate(stat.type, stat.baseValue, modifiers);
                        stats[i] = stat;
                    }
                }
            }

            // After the attributes, so a resource follows the cap it has on this tick.
            private static void UpdateResources(DynamicBuffer<LockstepStat> stats, NativeArray<LockstepStatChange> changes)
            {
                for (var i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    if (!stat.IsResource)
                    {
                        continue;
                    }
                    var isCapped = TryGetCap(stats, stat.cap, out var cap);
                    if (isCapped)
                    {
                        stat.value = Fit(stat, cap);
                        stat.max = cap;
                    }
                    if (changes.IsCreated)
                    {
                        stat.value += Sum(changes, stat.type);
                    }
                    stat.value = FixedMath.Max(stat.value, FixedPoint.Zero);
                    if (isCapped)
                    {
                        stat.value = FixedMath.Min(stat.value, cap);
                    }
                    stats[i] = stat;
                }
            }

            private static bool TryGetCap(DynamicBuffer<LockstepStat> stats, int type, out FixedPoint cap)
            {
                cap = FixedPoint.Zero;
                if (type == LockstepStat.NONE || !stats.TryGet(type, out var attribute) || !attribute.IsAttribute)
                {
                    return false;
                }
                cap = FixedMath.Max(attribute.value, FixedPoint.Zero);
                return true;
            }

            // The amount for a new cap. A resource whose cap was zero, as a new one, counts as full.
            private static FixedPoint Fit(in LockstepStat stat, FixedPoint cap)
            {
                if (stat.max <= FixedPoint.Zero)
                {
                    return cap;
                }
                if (stat.capPolicy == LockstepStatCapPolicy.KeepRatio && cap != stat.max)
                {
                    return stat.value * cap / stat.max;
                }
                return stat.value;
            }

            private static FixedPoint Sum(NativeArray<LockstepStatChange> changes, int type)
            {
                var sum = FixedPoint.Zero;
                for (var i = 0; i < changes.Length; i++)
                {
                    if (changes[i].stat == type)
                    {
                        sum += changes[i].amount;
                    }
                }
                return sum;
            }
        }
    }
}
