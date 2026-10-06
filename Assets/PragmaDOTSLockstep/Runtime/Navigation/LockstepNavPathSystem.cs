using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Plans the paths of agents that got a destination, and checks the paths of walking agents again whenever the grid
    /// changed: a path an obstacle now blocks is planned again, and so is a partial one, whose destination may have become
    /// reachable. An agent standing still on a cell that became blocked walks to the nearest walkable cell.
    /// </summary>
    /// <remarks>
    /// <para>Agents sent together share one search: the paths planned on a tick are grouped by the cell of their
    /// <see cref="LockstepNavAgent.groupGoal"/> and the clearance of their body, every group of two or more searches once
    /// from its goal back to all of its agents (<see cref="LockstepPathfinder.SearchGroup"/>), and each agent follows its
    /// own chain of cells to its own destination. An agent the chain does not lead to its destination, and an agent alone,
    /// plans its own path. An order that moves an army costs one search, not one per soldier.</para>
    /// <para>A path keeps the agent's body out of cells too narrow for it (<see cref="LockstepNavigation.GetClearance"/>).
    /// With avoidance (<see cref="LockstepNavAvoidance"/>), a path goes around crowds of standing agents when the way
    /// around is shorter than <see cref="LockstepNavAvoidance.crowdCost"/> for every agent in the way.</para>
    /// <para>Groups are searched in parallel, then every agent's path is made in parallel. A path depends only on the grid,
    /// the standing agents, its own agent and the agents of its group, so it is the same whichever thread computes it;
    /// groups and their agents are in the order of the query, which is the same on every client. The scratch memory of
    /// the searches, one pathfinder per job thread, lives as long as the system and holds no state between ticks. Without
    /// a <see cref="LockstepNavGrid"/> paths go straight to the destination.</para>
    /// </remarks>
    [UpdateInGroup(typeof(LockstepNavSystemGroup))]
    [UpdateAfter(typeof(LockstepNavObstacleSystem))]
    [BurstCompile]
    public partial struct LockstepNavPathSystem : ISystem
    {
        private NativeArray<LockstepPathfinder> _pathfinders;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _pathfinders = new NativeArray<LockstepPathfinder>(JobsUtility.ThreadIndexCount, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            state.CompleteDependency();
            for (var i = 0; i < _pathfinders.Length; i++)
            {
                _pathfinders[i].Dispose();
            }
            _pathfinders.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var allocator = state.WorldUpdateAllocator;
            var map = new LockstepNavPathMap();
            var hasGrid = false;
            if (SystemAPI.TryGetSingleton<LockstepNavGrid>(out var grid) &&
                grid.IsValid &&
                SystemAPI.TryGetSingletonBuffer<LockstepNavCell>(out var cells, true) &&
                cells.Length == grid.CellCount)
            {
                map = new LockstepNavPathMap(grid, cells.AsNativeArray());
                hasGrid = true;
            }
            else
            {
                map.grid = grid;
                map.cells = CollectionHelper.CreateNativeArray<LockstepNavCell>(0, allocator);
            }

            // Standing agents cost a path only with avoidance.
            if (hasGrid && SystemAPI.TryGetSingleton<LockstepNavAvoidance>(out var avoidance) && avoidance.crowdCost.rawValue > 0)
            {
                map.crowdCost = math.max(FixedMath.RoundToInt(avoidance.crowdCost * LockstepPathfinder.STRAIGHT_COST), 1);
            }
            map.crowd = CollectionHelper.CreateNativeArray<byte>(map.crowdCost > 0 ? grid.CellCount : 0, allocator);

            var requests = new NativeList<PathRequest>(allocator);
            var members = new NativeList<int>(allocator);
            var groups = new NativeList<PathGroup>(allocator);
            var spans = new NativeList<int2>(allocator);
            var chains = new NativeList<int>(allocator);

            state.Dependency = new CheckJob { grid = grid, cells = map.cells, hasGrid = hasGrid }.ScheduleParallel(state.Dependency);
            state.Dependency = new CollectJob
            {
                grid = grid,
                cells = map.cells,
                hasGrid = hasGrid,
                crowd = map.crowd,
                isCrowded = map.crowdCost > 0,
                requests = requests,
            }.Schedule(state.Dependency);
            state.Dependency = new GroupJob { requests = requests, members = members, groups = groups, spans = spans }.Schedule(state.Dependency);
            state.Dependency = NativeStream.ScheduleConstruct(out var stream, groups, state.Dependency, Allocator.TempJob);
            state.Dependency = new SearchJob
            {
                map = map,
                requests = requests.AsDeferredJobArray(),
                members = members.AsDeferredJobArray(),
                groups = groups.AsDeferredJobArray(),
                pathfinders = _pathfinders,
                writer = stream.AsWriter(),
            }.Schedule(groups, 1, state.Dependency);
            state.Dependency = new ChainJob { reader = stream.AsReader(), spans = spans, chains = chains }.Schedule(state.Dependency);
            state.Dependency = stream.Dispose(state.Dependency);
            state.Dependency = new PlanJob
            {
                map = map,
                hasGrid = hasGrid,
                requests = requests.AsDeferredJobArray(),
                spans = spans.AsDeferredJobArray(),
                chains = chains.AsDeferredJobArray(),
                pathfinders = _pathfinders,
                agents = SystemAPI.GetComponentLookup<LockstepNavAgent>(),
                waypoints = SystemAPI.GetBufferLookup<LockstepNavWaypoint>(),
            }.Schedule(requests, 1, state.Dependency);
        }

        /// <summary>The pathfinder of the job thread, created by its first search.</summary>
        private static LockstepPathfinder GetPathfinder(NativeArray<LockstepPathfinder> pathfinders, int thread, int cellCount)
        {
            var pathfinder = pathfinders[thread];
            if (!pathfinder.IsCreated)
            {
                pathfinder = new LockstepPathfinder(cellCount, Allocator.Persistent);
                pathfinders[thread] = pathfinder;
            }
            return pathfinder;
        }

        /// <summary>An agent that plans its path on this tick.</summary>
        private struct PathRequest
        {
            public Entity entity;
            public FixedVector3 position;
            public int clearance;
            // Cells of the start and of the group's goal, both passable for the agent; -1 when it cannot be grouped.
            public int start;
            public int goal;
        }

        /// <summary>Agents that share a search: a run of <see cref="count"/> requests in the sorted members.</summary>
        private struct PathGroup
        {
            public int first;
            public int count;
        }

        // The walking agents look at their paths again when the grid changed: a path an obstacle now blocks, or a partial
        // one, is planned again; a standing agent on a cell its body no longer fits in walks to the nearest one it fits in.
        [BurstCompile]
        private partial struct CheckJob : IJobEntity
        {
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            public bool hasGrid;

            private void Execute(in LockstepTransform transform, ref LockstepNavAgent agent, in DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                if (agent.status == LockstepNavStatus.Requested || !hasGrid || agent.gridVersion == grid.version)
                {
                    return;
                }
                var clearance = LockstepNavigation.GetClearance(grid, agent.radius);
                var position = transform.position;
                if (agent.status != LockstepNavStatus.Moving)
                {
                    agent.gridVersion = grid.version;
                    var cell = grid.WorldToCell(position);
                    if (grid.Contains(cell) &&
                        !LockstepNavigation.IsPassable(grid, cells, cell, clearance) &&
                        LockstepNavigation.TryFindNearestPassable(grid, cells, cell, clearance, out var nearest))
                    {
                        var center = grid.GetCellCenter(nearest);
                        agent.SetDestination(new FixedVector3(center.x, position.y, center.y));
                    }
                    return;
                }
                if (!agent.isPathPartial && IsClear(position, agent.waypointIndex, waypoints, clearance))
                {
                    agent.gridVersion = grid.version;
                    return;
                }
                agent.Replan();
            }

            // The rest of the path still runs through cells the body fits in.
            private bool IsClear(FixedVector3 position, int index, in DynamicBuffer<LockstepNavWaypoint> waypoints, int clearance)
            {
                var from = position.Xz;
                for (var i = index; i < waypoints.Length; i++)
                {
                    var to = waypoints[i].position.Xz;
                    if (!LockstepNavigation.HasLineOfSight(grid, cells, from, to, clearance))
                    {
                        return false;
                    }
                    from = to;
                }
                return true;
            }
        }

        // Lists the agents that plan on this tick, in query order, and counts the standing agents in every cell.
        [BurstCompile]
        private partial struct CollectJob : IJobEntity
        {
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            public bool hasGrid;
            public NativeArray<byte> crowd;
            public bool isCrowded;
            public NativeList<PathRequest> requests;

            private void Execute(Entity entity, in LockstepTransform transform, in LockstepNavAgent agent)
            {
                var position = transform.position;
                if (isCrowded && agent.radius.rawValue > 0 && !agent.IsMoving)
                {
                    AddToCrowd(position.Xz, agent.radius + grid.agentRadius);
                }
                if (agent.status != LockstepNavStatus.Requested)
                {
                    return;
                }

                var request = new PathRequest { entity = entity, position = position, clearance = 1, start = -1, goal = -1 };
                if (hasGrid)
                {
                    var map = new LockstepNavPathMap(grid, cells, LockstepNavigation.GetClearance(grid, agent.radius));
                    request.clearance = map.clearance;
                    if (LockstepPathfinder.TryGetStartCell(map, position, out var start) &&
                        LockstepPathfinder.TryGetEndCell(map, agent.groupGoal, out var goal, out _))
                    {
                        request.start = grid.GetIndex(start);
                        request.goal = grid.GetIndex(goal);
                    }
                }
                requests.Add(request);
            }

            // A standing agent crowds every cell whose centre its body, grown by the agent radius of the grid, covers: a
            // path around it keeps a walker of that radius clear of it.
            private void AddToCrowd(FixedVector2 position, FixedPoint reach)
            {
                var min = grid.ClampToGrid(grid.WorldToCell(position - new FixedVector2(reach, reach)));
                var max = grid.ClampToGrid(grid.WorldToCell(position + new FixedVector2(reach, reach)));
                var reachSquared = reach * reach;
                for (var y = min.y; y <= max.y; y++)
                {
                    for (var x = min.x; x <= max.x; x++)
                    {
                        var cell = new int2(x, y);
                        if (FixedMath.DistanceSquared(grid.GetCellCenter(cell), position) > reachSquared)
                        {
                            continue;
                        }
                        var index = grid.GetIndex(cell);
                        crowd[index] = (byte)math.min(crowd[index] + 1, byte.MaxValue);
                    }
                }
            }
        }

        // Sorts the requests by goal and clearance (then query order) and cuts the runs of two or more into groups.
        [BurstCompile]
        private struct GroupJob : IJob
        {
            [ReadOnly] public NativeList<PathRequest> requests;
            public NativeList<int> members;
            public NativeList<PathGroup> groups;
            public NativeList<int2> spans;

            public void Execute()
            {
                spans.Resize(requests.Length, NativeArrayOptions.ClearMemory);
                for (var i = 0; i < requests.Length; i++)
                {
                    if (requests[i].goal >= 0)
                    {
                        members.Add(i);
                    }
                }
                members.Sort(new MemberOrder { requests = requests.AsArray() });

                for (var first = 0; first < members.Length;)
                {
                    var head = requests[members[first]];
                    var count = 1;
                    while (first + count < members.Length && IsSameGroup(head, requests[members[first + count]]))
                    {
                        count++;
                    }
                    if (count > 1)
                    {
                        groups.Add(new PathGroup { first = first, count = count });
                    }
                    first += count;
                }
            }

            private static bool IsSameGroup(in PathRequest a, in PathRequest b)
            {
                return a.goal == b.goal && a.clearance == b.clearance;
            }
        }

        private struct MemberOrder : System.Collections.Generic.IComparer<int>
        {
            [ReadOnly] public NativeArray<PathRequest> requests;

            public int Compare(int a, int b)
            {
                var first = requests[a];
                var second = requests[b];
                if (first.goal != second.goal)
                {
                    return first.goal.CompareTo(second.goal);
                }
                if (first.clearance != second.clearance)
                {
                    return first.clearance.CompareTo(second.clearance);
                }
                return a.CompareTo(b);
            }
        }

        // One search per group, from its goal back to all of its agents; writes every reached agent's chain of cells.
        [BurstCompile]
        private struct SearchJob : IJobParallelForDefer
        {
            [ReadOnly] public LockstepNavPathMap map;
            [ReadOnly] public NativeArray<PathRequest> requests;
            [ReadOnly] public NativeArray<int> members;
            [ReadOnly] public NativeArray<PathGroup> groups;
            [NativeDisableParallelForRestriction] public NativeArray<LockstepPathfinder> pathfinders;
            public NativeStream.Writer writer;
            [NativeSetThreadIndex] private int _thread;

            public void Execute(int index)
            {
                var group = groups[index];
                var own = map;
                own.clearance = requests[members[group.first]].clearance;
                var starts = new NativeArray<int>(group.count, Allocator.Temp);
                for (var i = 0; i < group.count; i++)
                {
                    starts[i] = requests[members[group.first + i]].start;
                }

                var pathfinder = GetPathfinder(pathfinders, _thread, map.grid.CellCount);
                pathfinder.SearchGroup(own, requests[members[group.first]].goal, starts);

                var chain = new NativeList<int>(Allocator.Temp);
                writer.BeginForEachIndex(index);
                for (var i = 0; i < group.count; i++)
                {
                    pathfinder.GetChain(starts[i], chain);
                    if (chain.Length == 0)
                    {
                        continue;
                    }
                    writer.Write(members[group.first + i]);
                    writer.Write(chain.Length);
                    for (var c = 0; c < chain.Length; c++)
                    {
                        writer.Write(chain[c]);
                    }
                }
                writer.EndForEachIndex();
            }
        }

        // Lays the chains of all groups out in one list, with the span of every request's chain in it.
        [BurstCompile]
        private struct ChainJob : IJob
        {
            public NativeStream.Reader reader;
            public NativeList<int2> spans;
            public NativeList<int> chains;

            public void Execute()
            {
                for (var i = 0; i < reader.ForEachCount; i++)
                {
                    reader.BeginForEachIndex(i);
                    while (reader.RemainingItemCount > 0)
                    {
                        var request = reader.Read<int>();
                        var length = reader.Read<int>();
                        spans[request] = new int2(chains.Length, length);
                        for (var c = 0; c < length; c++)
                        {
                            chains.Add(reader.Read<int>());
                        }
                    }
                    reader.EndForEachIndex();
                }
            }
        }

        // Every request's path: along its group's chain, or a search of its own.
        [BurstCompile]
        private struct PlanJob : IJobParallelForDefer
        {
            [ReadOnly] public LockstepNavPathMap map;
            public bool hasGrid;
            [ReadOnly] public NativeArray<PathRequest> requests;
            [ReadOnly] public NativeArray<int2> spans;
            [ReadOnly] public NativeArray<int> chains;
            [NativeDisableParallelForRestriction] public NativeArray<LockstepPathfinder> pathfinders;
            [NativeDisableParallelForRestriction] public ComponentLookup<LockstepNavAgent> agents;
            [NativeDisableParallelForRestriction] public BufferLookup<LockstepNavWaypoint> waypoints;
            [NativeSetThreadIndex] private int _thread;

            public void Execute(int index)
            {
                var request = requests[index];
                var agent = agents[request.entity];
                var path = waypoints[request.entity];
                var status = LockstepPathStatus.Complete;
                if (hasGrid)
                {
                    var own = map;
                    own.clearance = request.clearance;
                    var pathfinder = GetPathfinder(pathfinders, _thread, map.grid.CellCount);
                    var span = spans[index];
                    status = span.y > 0
                        ? pathfinder.FollowChain(own, request.position, agent.destination, chains.GetSubArray(span.x, span.y), path)
                        : LockstepPathStatus.Failed;
                    if (status == LockstepPathStatus.Failed)
                    {
                        status = pathfinder.FindPath(own, request.position, agent.destination, path);
                    }
                }
                else
                {
                    path.Clear();
                    path.Add(new LockstepNavWaypoint { position = agent.destination });
                }
                agent.waypointIndex = 0;
                agent.gridVersion = map.grid.version;
                agent.isPathPartial = status != LockstepPathStatus.Complete;
                agent.status = path.Length > 0 ? LockstepNavStatus.Moving : LockstepNavStatus.Arrived;
                agents[request.entity] = agent;
            }
        }
    }
}
