using System;
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
    /// way to a destination that unit stands on. An agent is never pushed onto a blocked cell of the grid, and an agent
    /// with a zero radius neither pushes nor is pushed.
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

            job.entities = _agentQuery.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var entitiesHandle);
            job.agents = _agentQuery.ToComponentDataListAsync<LockstepNavAgent>(state.WorldUpdateAllocator, state.Dependency, out var agentsHandle);
            job.transforms = _agentQuery.ToComponentDataListAsync<LockstepTransform>(state.WorldUpdateAllocator, state.Dependency, out var transformsHandle);
            state.Dependency = job.Schedule(JobHandle.CombineDependencies(entitiesHandle, agentsHandle, transformsHandle));
        }

        /// <summary>An agent in the spatial hash: its hash cell, then its index in the query.</summary>
        private struct HashedAgent : IComparable<HashedAgent>
        {
            public long cell;
            public int index;

            public int CompareTo(HashedAgent other)
            {
                return cell != other.cell ? cell.CompareTo(other.cell) : index.CompareTo(other.index);
            }
        }

        [BurstCompile]
        private struct SeparateJob : IJob
        {
            // Agents closer than this are taken to stand on the same point: the direction between them is noise.
            private const long MIN_DISTANCE_RAW = FixedPoint.ONE_RAW / 1024;
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
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            public bool hasGrid;

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

                // Neighbours of an agent are at most two radii away, so they are in its hash cell or the eight around it.
                var cellSize = maxRadius.rawValue * 2;
                var hashed = new NativeList<HashedAgent>(agents.Length, Allocator.Temp);
                for (var i = 0; i < agents.Length; i++)
                {
                    if (agents[i].radius.rawValue > 0)
                    {
                        var position = transforms[i].position;
                        hashed.Add(new HashedAgent { cell = Key(FloorDivide(position.x.rawValue, cellSize), FloorDivide(position.z.rawValue, cellSize)), index = i });
                    }
                }
                hashed.Sort();

                for (var h = 0; h < hashed.Length; h++)
                {
                    var index = hashed[h].index;
                    var push = GetPush(index, hashed, cellSize);
                    if (push.x.rawValue == 0 && push.y.rawValue == 0)
                    {
                        continue;
                    }
                    var transform = transforms[index];
                    if (TryMove(transform.position, push, out var position))
                    {
                        transform.position = position;
                        transformLookup[entities[index]] = transform;
                    }
                }
            }

            // The sum of the pushes of the neighbours, at most the agent's radius per tick.
            private FixedVector2 GetPush(int index, NativeList<HashedAgent> hashed, long cellSize)
            {
                var position = transforms[index].position.Xz;
                var radius = agents[index].radius;
                var cellX = FloorDivide(position.x.rawValue, cellSize);
                var cellZ = FloorDivide(position.y.rawValue, cellSize);
                var push = FixedVector2.Zero;
                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var key = Key(cellX + dx, cellZ + dz);
                        for (var h = LowerBound(hashed, key); h < hashed.Length && hashed[h].cell == key; h++)
                        {
                            var other = hashed[h].index;
                            if (other != index)
                            {
                                push += GetPairPush(index, position, radius, other);
                            }
                        }
                    }
                }
                return FixedMath.ClampLength(push, radius);
            }

            // How far the other agent pushes this one, away from it.
            private FixedVector2 GetPairPush(int index, FixedVector2 position, FixedPoint radius, int other)
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
                return direction * ((limit - distance) * GetShare(agents[index].IsMoving, agents[other].IsMoving) / SHARE_DENOMINATOR);
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

            // The pushed position, or only its walkable part: the whole push, else along X, else along Z. An agent that
            // already stands on a blocked cell is pushed freely: the path system walks it out.
            private bool TryMove(FixedVector3 from, FixedVector2 push, out FixedVector3 position)
            {
                position = new FixedVector3(from.x + push.x, from.y, from.z + push.y);
                if (!hasGrid || !LockstepNavigation.IsWalkable(grid, cells, from) || LockstepNavigation.IsWalkable(grid, cells, position))
                {
                    return true;
                }
                position = new FixedVector3(from.x + push.x, from.y, from.z);
                if (push.x.rawValue != 0 && LockstepNavigation.IsWalkable(grid, cells, position))
                {
                    return true;
                }
                position = new FixedVector3(from.x, from.y, from.z + push.y);
                return push.y.rawValue != 0 && LockstepNavigation.IsWalkable(grid, cells, position);
            }

            private static long Key(long x, long z) => (x << 32) ^ (z & 0xFFFFFFFFL);

            private static int LowerBound(NativeList<HashedAgent> hashed, long key)
            {
                var low = 0;
                var high = hashed.Length;
                while (low < high)
                {
                    var middle = (low + high) >> 1;
                    if (hashed[middle].cell < key)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }
                return low;
            }

            private static long FloorDivide(long value, long divisor)
            {
                var quotient = value / divisor;
                if (value % divisor != 0 && value < 0)
                {
                    quotient--;
                }
                return quotient;
            }
        }
    }
}
