using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Keeps agents from standing inside each other: after they walked, every two agents closer than the sum of their
    /// <see cref="LockstepNavAgent.radius"/> are pushed apart by half of the overlap per tick, so a crowd spreads out over
    /// a few ticks instead of jumping. Two walking or two standing agents share the push equally; a walking agent takes
    /// three quarters of it from a standing one, so it slides past a unit that holds its place, yet still shoulders its
    /// way to a destination that unit stands on. Two agents walking opposite ways (by their last steps,
    /// <see cref="LockstepNavVelocity"/>) are only pushed sideways, each to its own side, never back: they step aside and
    /// pass, and in a passage too narrow to step aside in, they squeeze past each other instead of blocking it for good.
    /// An agent is never pushed onto a cell its body does not fit in, a pushed agent stays on the ground of the grid
    /// (<see cref="LockstepNavHeight"/>), and an agent with a zero radius neither pushes nor is pushed.
    /// </summary>
    /// <remarks>
    /// An agent's push is the sum of the pushes of its neighbours, each a function of the pair alone, summed in exact
    /// integer math: the result does not depend on the order agents are visited in. Agents that stand on the same point are
    /// told apart by their order in the query, which is the same on every client. Paths are not planned again: a pushed
    /// agent walks on to its next waypoint from where it was pushed to.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepNavSystemGroup))]
    [UpdateAfter(typeof(LockstepNavMoveSystem))]
    [BurstCompile]
    public partial struct LockstepNavSeparationSystem : ISystem
    {
        private EntityQuery _agentQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _agentQuery = SystemAPI.QueryBuilder().WithAll<LockstepNavAgent>().WithAllRW<LockstepTransform>().Build();
            state.RequireForUpdate(_agentQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new SeparateJob
            {
                transformLookup = SystemAPI.GetComponentLookup<LockstepTransform>(),
                velocityLookup = SystemAPI.GetComponentLookup<LockstepNavVelocity>(true),
            };
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
            if (job.hasGrid &&
                SystemAPI.TryGetSingletonBuffer<LockstepNavHeight>(out var heights, true) &&
                LockstepNavigation.HasHeights(grid, heights.AsNativeArray()))
            {
                job.heights = heights.AsNativeArray();
                job.hasHeights = true;
            }
            else
            {
                job.heights = CollectionHelper.CreateNativeArray<LockstepNavHeight>(0, state.WorldUpdateAllocator);
            }

            job.entities = _agentQuery.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var entitiesHandle);
            job.agents = _agentQuery.ToComponentDataListAsync<LockstepNavAgent>(state.WorldUpdateAllocator, state.Dependency, out var agentsHandle);
            job.transforms = _agentQuery.ToComponentDataListAsync<LockstepTransform>(state.WorldUpdateAllocator, state.Dependency, out var transformsHandle);
            state.Dependency = job.Schedule(JobHandle.CombineDependencies(entitiesHandle, agentsHandle, transformsHandle));
        }

        [BurstCompile]
        private struct SeparateJob : IJob
        {
            // Agents closer than this are taken to stand on the same point: the direction between them is noise.
            private const long MIN_DISTANCE_RAW = FixedPoint.ONE_RAW / 1024;
            // Two walkers whose offset is this close to their heading meet head on: sideways of it is noise.
            private const long MIN_SIDEWAYS_RAW = FixedPoint.ONE_RAW / 64;
            // Shares of the overlap, in eighths, that an agent of an overlapping pair moves per tick: half of the overlap
            // is gone per tick, split equally, or three to one when only one of the two walks.
            private const int EQUAL_SHARE = 2;
            private const int WALKER_SHARE = 3;
            private const int STANDER_SHARE = 1;
            private const int SHARE_DENOMINATOR = 8;

            [ReadOnly] public NativeList<Entity> entities;
            [ReadOnly] public NativeList<LockstepNavAgent> agents;
            [ReadOnly] public NativeList<LockstepTransform> transforms;
            public ComponentLookup<LockstepTransform> transformLookup;
            [ReadOnly] public ComponentLookup<LockstepNavVelocity> velocityLookup;
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            public bool hasGrid;
            [ReadOnly] public NativeArray<LockstepNavHeight> heights;
            public bool hasHeights;

            public void Execute()
            {
                var maxRadius = FixedPoint.Zero;
                for (var i = 0; i < agents.Length; i++)
                {
                    maxRadius = FixedMath.Max(maxRadius, agents[i].radius);
                }
                if (maxRadius.rawValue <= 0)
                {
                    return;
                }

                // What every agent walked last, zero for agents that do not record it.
                var velocities = new NativeArray<FixedVector2>(agents.Length, Allocator.Temp);
                // Neighbours of an agent are at most two radii away, so they are in its hash cell or the eight around it.
                var hash = new LockstepNavAgentHash(agents.Length, maxRadius * 2, Allocator.Temp);
                for (var i = 0; i < agents.Length; i++)
                {
                    if (agents[i].radius.rawValue > 0)
                    {
                        hash.Add(transforms[i].position.Xz, i);
                        if (velocityLookup.TryGetComponent(entities[i], out var velocity))
                        {
                            velocities[i] = velocity.value;
                        }
                    }
                }
                hash.Sort();

                for (var entry = 0; entry < hash.Count; entry++)
                {
                    var index = hash.GetIndex(entry);
                    var push = GetPush(index, hash, velocities);
                    if (push.x.rawValue == 0 && push.y.rawValue == 0)
                    {
                        continue;
                    }
                    var transform = transforms[index];
                    if (TryMove(transform.position, push, LockstepNavigation.GetClearance(grid, agents[index].radius), out var position))
                    {
                        transform.position = hasHeights ? LockstepNavigation.ToGround(grid, heights, position) : position;
                        transformLookup[entities[index]] = transform;
                    }
                }
            }

            // The sum of the pushes of the neighbours, at most the agent's radius per tick.
            private FixedVector2 GetPush(int index, in LockstepNavAgentHash hash, NativeArray<FixedVector2> velocities)
            {
                var position = transforms[index].position.Xz;
                var radius = agents[index].radius;
                var cellX = hash.GetCell(position.x);
                var cellZ = hash.GetCell(position.y);
                var push = FixedVector2.Zero;
                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var entry = hash.GetFirst(cellX + dx, cellZ + dz); hash.IsInCell(entry, cellX + dx, cellZ + dz); entry++)
                        {
                            var other = hash.GetIndex(entry);
                            if (other != index)
                            {
                                push += GetPairPush(index, position, radius, other, velocities);
                            }
                        }
                    }
                }
                return FixedMath.ClampLength(push, radius);
            }

            // How far the other agent pushes this one, away from it.
            private FixedVector2 GetPairPush(int index, FixedVector2 position, FixedPoint radius, int other, NativeArray<FixedVector2> velocities)
            {
                var offset = position - transforms[other].position.Xz;
                var limit = radius + agents[other].radius;
                if (FixedMath.Abs(offset.x) >= limit || FixedMath.Abs(offset.y) >= limit)
                {
                    return FixedVector2.Zero;
                }
                var distanceSquared = FixedMath.LengthSquared(offset);
                if (distanceSquared >= limit * limit)
                {
                    return FixedVector2.Zero;
                }
                var distance = FixedMath.Sqrt(distanceSquared);
                var direction = distance.rawValue > MIN_DISTANCE_RAW ? offset / distance : GetTieBreak(index, other);
                var isWalking = agents[index].IsMoving;
                var isOtherWalking = agents[other].IsMoving;
                if (isWalking && isOtherWalking && TryGetPassing(velocities[index], velocities[other], ref direction))
                {
                    return direction * ((limit - distance) * EQUAL_SHARE / SHARE_DENOMINATOR);
                }
                return direction * ((limit - distance) * GetShare(isWalking, isOtherWalking) / SHARE_DENOMINATOR);
            }

            // Two walkers going opposite ways pass each other: the push turns sideways to the agent's heading, towards the
            // side the other one is not on, or to its right when they meet head on.
            private static bool TryGetPassing(FixedVector2 velocity, FixedVector2 otherVelocity, ref FixedVector2 direction)
            {
                if (FixedMath.Dot(velocity, otherVelocity).rawValue >= 0)
                {
                    return false;
                }
                var heading = FixedMath.NormalizeSafe(velocity);
                if (heading.x.rawValue == 0 && heading.y.rawValue == 0)
                {
                    return false;
                }
                var sideways = direction - heading * FixedMath.Dot(direction, heading);
                var length = FixedMath.Length(sideways);
                // Right of the heading: positive turns go from X towards Z, to the left of an agent facing X.
                direction = length.rawValue > MIN_SIDEWAYS_RAW ? sideways / length : -FixedMath.Perpendicular(heading);
                return true;
            }

            private static int GetShare(bool isWalking, bool isOtherWalking)
            {
                if (isWalking == isOtherWalking)
                {
                    return EQUAL_SHARE;
                }
                return isWalking ? WALKER_SHARE : STANDER_SHARE;
            }

            // Agents on the same point split along a direction picked by the pair, opposite for its two agents.
            private static FixedVector2 GetTieBreak(int index, int other)
            {
                var first = math.min(index, other);
                var second = math.max(index, other);
                var direction = GetDirection((first * 31 + second) & 7);
                return index == first ? direction : -direction;
            }

            // One of eight directions 45 degrees apart.
            private static FixedVector2 GetDirection(int octant)
            {
                var diagonal = FixedPoint.FromFraction(70711, 100000);
                switch (octant)
                {
                    case 0: return new FixedVector2(FixedPoint.One, FixedPoint.Zero);
                    case 1: return new FixedVector2(diagonal, diagonal);
                    case 2: return new FixedVector2(FixedPoint.Zero, FixedPoint.One);
                    case 3: return new FixedVector2(-diagonal, diagonal);
                    case 4: return new FixedVector2(FixedPoint.MinusOne, FixedPoint.Zero);
                    case 5: return new FixedVector2(-diagonal, -diagonal);
                    case 6: return new FixedVector2(FixedPoint.Zero, FixedPoint.MinusOne);
                    default: return new FixedVector2(diagonal, -diagonal);
                }
            }

            // The pushed position, or only the part of it the body fits in (LockstepNavigation.TryStep). An agent that
            // already stands on a cell too narrow for it is pushed freely: the path system walks it out.
            private bool TryMove(FixedVector3 from, FixedVector2 push, int clearance, out FixedVector3 position)
            {
                if (hasGrid)
                {
                    return LockstepNavigation.TryStep(grid, cells, from, push, clearance, out position);
                }
                position = new FixedVector3(from.x + push.x, from.y, from.z + push.y);
                return true;
            }
        }
    }
}
