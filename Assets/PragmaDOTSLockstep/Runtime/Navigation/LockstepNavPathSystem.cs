using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Plans the paths of agents that got a destination, and checks the paths of walking agents again whenever the grid
    /// changed: a path an obstacle now blocks is planned again, and so is a partial one, whose destination may have become
    /// reachable.
    /// </summary>
    /// <remarks>
    /// Agents are planned in parallel. A path depends only on the grid and on its own agent, so it is the same whichever
    /// thread computes it. Without a <see cref="LockstepNavGrid"/> paths go straight to the destination.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepNavSystemGroup))]
    [UpdateAfter(typeof(LockstepNavObstacleSystem))]
    [BurstCompile]
    public partial struct LockstepNavPathSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new PlanJob();
            if (SystemAPI.TryGetSingleton<LockstepNavGrid>(out var grid) &&
                grid.IsValid &&
                SystemAPI.TryGetSingletonBuffer<LockstepNavCell>(out var cells, true) &&
                cells.Length == grid.CellCount)
            {
                job.grid = grid;
                job.cells = cells.AsNativeArray();
                job.hasGrid = true;
            }
            else
            {
                job.cells = CollectionHelper.CreateNativeArray<LockstepNavCell>(0, state.WorldUpdateAllocator);
            }
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct PlanJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            public bool hasGrid;

            // Scratch memory of the current chunk, created by its first search.
            private LockstepPathfinder _pathfinder;

            public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
                if (_pathfinder.IsCreated)
                {
                    _pathfinder.Dispose();
                    _pathfinder = default;
                }
            }

            private void Execute(in LockstepTransform transform, ref LockstepNavAgent agent, DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                if (agent.status == LockstepNavStatus.Requested)
                {
                    Plan(transform.position, ref agent, waypoints);
                    return;
                }
                if (agent.status != LockstepNavStatus.Moving || !hasGrid || agent.gridVersion == grid.version)
                {
                    return;
                }
                if (!agent.isPathPartial && IsClear(transform.position, agent.waypointIndex, waypoints))
                {
                    agent.gridVersion = grid.version;
                    return;
                }
                Plan(transform.position, ref agent, waypoints);
            }

            private void Plan(FixedVector3 position, ref LockstepNavAgent agent, DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                var status = LockstepPathStatus.Complete;
                if (hasGrid)
                {
                    if (!_pathfinder.IsCreated)
                    {
                        _pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp);
                    }
                    status = _pathfinder.FindPath(grid, cells, position, agent.destination, waypoints);
                }
                else
                {
                    waypoints.Clear();
                    waypoints.Add(new LockstepNavWaypoint { position = agent.destination });
                }
                agent.waypointIndex = 0;
                agent.gridVersion = grid.version;
                agent.isPathPartial = status != LockstepPathStatus.Complete;
                agent.status = waypoints.Length > 0 ? LockstepNavStatus.Moving : LockstepNavStatus.Arrived;
            }

            // The rest of the path still runs through walkable cells only.
            private bool IsClear(FixedVector3 position, int index, DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                var from = position;
                for (var i = index; i < waypoints.Length; i++)
                {
                    var to = waypoints[i].position;
                    if (!LockstepNavigation.HasLineOfSight(grid, cells, from, to))
                    {
                        return false;
                    }
                    from = to;
                }
                return true;
            }
        }
    }
}
