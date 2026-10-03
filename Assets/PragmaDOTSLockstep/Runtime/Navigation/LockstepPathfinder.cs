using System;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Finds paths on a <see cref="LockstepNavGrid"/>: A* over the eight neighbours of a cell, never across a blocked
    /// corner, then string pulling, so a path is a few straight segments rather than a staircase of cells.
    /// </summary>
    /// <remarks>
    /// <para>Integer costs (10 straight, 14 diagonal), an octile heuristic, a heap ordered by cost, heuristic and cell index,
    /// and exact line-of-sight tests: the same grid and query give the same path on every platform, in Burst and in
    /// Mono.</para>
    /// <para>A destination in a blocked cell moves to the nearest walkable cell. A start in a blocked cell (an obstacle
    /// placed on top of the agent) moves to the nearest walkable cell too, which becomes the first waypoint. When the
    /// destination cannot be reached, the path ends at the reachable cell closest to it.</para>
    /// <para>The pathfinder keeps its scratch memory between searches; copies share it. It is not thread-safe: use one per
    /// thread, or one per chunk in a job, and dispose it once.</para>
    /// </remarks>
    public unsafe struct LockstepPathfinder : IDisposable
    {
        private const int STRAIGHT_COST = 10;
        private const int DIAGONAL_COST = 14;
        // Cell states; cleared memory (zero) marks cells the search has not seen.
        private const byte OPEN = 1;
        private const byte CLOSED = 2;

        // Jobs may hold a pathfinder they create themselves: it is never shared between threads.
        [NativeDisableUnsafePtrRestriction]
        private Scratch* _scratch;
        private AllocatorManager.AllocatorHandle _allocator;

        /// <param name="cellCount">Cells of the grids it will search; the scratch memory grows for larger grids.</param>
        /// <param name="allocator">Allocator of the scratch memory; <c>Allocator.Temp</c> inside a job.</param>
        public LockstepPathfinder(int cellCount, AllocatorManager.AllocatorHandle allocator)
        {
            cellCount = math.max(cellCount, 1);
            _allocator = allocator;
            _scratch = AllocatorManager.Allocate<Scratch>(allocator);
            *_scratch = new Scratch
            {
                costs = new UnsafeList<int>(cellCount, allocator),
                parents = new UnsafeList<int>(cellCount, allocator),
                states = new UnsafeList<byte>(cellCount, allocator),
                open = new UnsafeList<Node>(64, allocator),
                path = new UnsafeList<int>(64, allocator),
                points = new UnsafeList<FixedVector3>(8, allocator),
            };
        }

        public bool IsCreated => _scratch != null;

        /// <summary>
        /// Plans a path from <paramref name="start"/> to <paramref name="destination"/> and writes its waypoints, the
        /// start excluded and the end included, to <paramref name="waypoints"/>. Y of the waypoints is the destination's.
        /// </summary>
        public LockstepPathStatus FindPath(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 start, FixedVector3 destination, NativeList<FixedVector3> waypoints)
        {
            var status = Plan(grid, cells, start, destination);
            ref var points = ref _scratch->points;
            waypoints.Clear();
            for (var i = 0; i < points.Length; i++)
            {
                waypoints.Add(points[i]);
            }
            return status;
        }

        /// <inheritdoc cref="FindPath(in LockstepNavGrid, NativeArray{LockstepNavCell}, FixedVector3, FixedVector3, NativeList{FixedVector3})"/>
        public LockstepPathStatus FindPath(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 start, FixedVector3 destination, DynamicBuffer<LockstepNavWaypoint> waypoints)
        {
            var status = Plan(grid, cells, start, destination);
            ref var points = ref _scratch->points;
            waypoints.Clear();
            for (var i = 0; i < points.Length; i++)
            {
                waypoints.Add(new LockstepNavWaypoint { position = points[i] });
            }
            return status;
        }

        public void Dispose()
        {
            if (_scratch == null)
            {
                return;
            }
            _scratch->Dispose();
            AllocatorManager.Free(_allocator, _scratch);
            _scratch = null;
        }

        private LockstepPathStatus Plan(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 start, FixedVector3 destination)
        {
            ref var scratch = ref *_scratch;
            scratch.points.Clear();
            if (!grid.IsValid || cells.Length < grid.CellCount)
            {
                return LockstepPathStatus.Failed;
            }
            scratch.Reserve(grid.CellCount);

            var startCell = grid.ClampToGrid(grid.WorldToCell(start));
            var isStartBlocked = !LockstepNavigation.IsWalkable(grid, cells, startCell);
            if (isStartBlocked && !LockstepNavigation.TryFindNearestWalkable(grid, cells, startCell, out startCell))
            {
                return LockstepPathStatus.Failed;
            }

            var destinationCell = grid.WorldToCell(destination);
            var goalCell = grid.ClampToGrid(destinationCell);
            var isDestinationOpen = grid.Contains(destinationCell) && LockstepNavigation.IsWalkable(grid, cells, goalCell);
            if (!isDestinationOpen)
            {
                // The start cell is walkable, so there is a nearest one.
                LockstepNavigation.TryFindNearestWalkable(grid, cells, goalCell, out goalCell);
            }

            var isReached = Search(ref scratch, grid, cells, grid.GetIndex(startCell), grid.GetIndex(goalCell), out var endIndex);
            var isComplete = isReached && isDestinationOpen;
            scratch.CollectPath(endIndex);

            var height = destination.y;
            var end = isComplete ? destination.Xz : grid.GetCellCenter(grid.GetCell(endIndex));
            var from = start.Xz;
            if (isStartBlocked)
            {
                from = grid.GetCellCenter(startCell);
                scratch.AddPoint(from, height);
            }

            // String pulling: follow the cells and keep a corner only where the straight line from the previous corner is
            // blocked. Neighbouring cells always see each other, because the search never cuts a blocked corner.
            ref var path = ref scratch.path;
            for (var i = 1; i < path.Length; i++)
            {
                var next = i == path.Length - 1 ? end : grid.GetCellCenter(grid.GetCell(path[i]));
                if (LockstepNavigation.HasLineOfSight(grid, cells, from, next))
                {
                    continue;
                }
                from = grid.GetCellCenter(grid.GetCell(path[i - 1]));
                scratch.AddPoint(from, height);
            }
            if (path.Length > 1 || isComplete)
            {
                scratch.AddPoint(end, height);
            }
            return isComplete ? LockstepPathStatus.Complete : LockstepPathStatus.Partial;
        }

        // A* from start to goal. Returns whether the goal was reached; end is the goal or, when it cannot be reached, the
        // searched cell with the smallest heuristic.
        private static bool Search(ref Scratch scratch, in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int start, int goal, out int end)
        {
            var costs = scratch.costs.Ptr;
            var parents = scratch.parents.Ptr;
            var states = scratch.states.Ptr;
            UnsafeUtility.MemClear(states, grid.CellCount);
            scratch.open.Clear();

            var goalCell = grid.GetCell(goal);
            var startHeuristic = Heuristic(grid.GetCell(start), goalCell);
            costs[start] = 0;
            parents[start] = -1;
            states[start] = OPEN;
            scratch.Push(new Node { total = startHeuristic, heuristic = startHeuristic, index = start });

            end = start;
            var endHeuristic = startHeuristic;
            while (scratch.open.Length > 0)
            {
                var node = scratch.Pop();
                // The heap keeps outdated entries of cells whose cost improved; the first one out is the best.
                if (states[node.index] == CLOSED)
                {
                    continue;
                }
                states[node.index] = CLOSED;
                if (node.index == goal)
                {
                    end = goal;
                    return true;
                }
                if (node.heuristic < endHeuristic)
                {
                    endHeuristic = node.heuristic;
                    end = node.index;
                }

                var cell = grid.GetCell(node.index);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }
                        var next = new int2(cell.x + dx, cell.y + dy);
                        if (!LockstepNavigation.IsWalkable(grid, cells, next))
                        {
                            continue;
                        }
                        var isDiagonal = dx != 0 && dy != 0;
                        if (isDiagonal && (!LockstepNavigation.IsWalkable(grid, cells, new int2(next.x, cell.y)) || !LockstepNavigation.IsWalkable(grid, cells, new int2(cell.x, next.y))))
                        {
                            continue;
                        }
                        var index = grid.GetIndex(next);
                        if (states[index] == CLOSED)
                        {
                            continue;
                        }
                        var cost = costs[node.index] + (isDiagonal ? DIAGONAL_COST : STRAIGHT_COST);
                        if (states[index] == OPEN && cost >= costs[index])
                        {
                            continue;
                        }
                        costs[index] = cost;
                        parents[index] = node.index;
                        states[index] = OPEN;
                        var heuristic = Heuristic(next, goalCell);
                        scratch.Push(new Node { total = cost + heuristic, heuristic = heuristic, index = index });
                    }
                }
            }
            return false;
        }

        // Octile distance in the units of the step costs, which keeps the search optimal.
        private static int Heuristic(int2 from, int2 to)
        {
            var delta = math.abs(to - from);
            return STRAIGHT_COST * math.max(delta.x, delta.y) + (DIAGONAL_COST - STRAIGHT_COST) * math.min(delta.x, delta.y);
        }

        private struct Scratch
        {
            public UnsafeList<int> costs;
            public UnsafeList<int> parents;
            public UnsafeList<byte> states;
            public UnsafeList<Node> open;
            public UnsafeList<int> path;
            public UnsafeList<FixedVector3> points;

            public void Reserve(int cellCount)
            {
                if (states.Length >= cellCount)
                {
                    return;
                }
                costs.Resize(cellCount);
                parents.Resize(cellCount);
                states.Resize(cellCount);
            }

            public void CollectPath(int end)
            {
                path.Clear();
                for (var index = end; index >= 0; index = parents[index])
                {
                    path.Add(index);
                }
                var cells = path.Ptr;
                for (int i = 0, j = path.Length - 1; i < j; i++, j--)
                {
                    var cell = cells[i];
                    cells[i] = cells[j];
                    cells[j] = cell;
                }
            }

            public void AddPoint(FixedVector2 point, FixedPoint height)
            {
                var position = new FixedVector3(point.x, height, point.y);
                if (points.Length == 0 || points[points.Length - 1] != position)
                {
                    points.Add(position);
                }
            }

            public void Push(Node node)
            {
                open.Add(node);
                var nodes = open.Ptr;
                var i = open.Length - 1;
                while (i > 0)
                {
                    var parent = (i - 1) >> 1;
                    if (!node.IsBefore(nodes[parent]))
                    {
                        break;
                    }
                    nodes[i] = nodes[parent];
                    i = parent;
                }
                nodes[i] = node;
            }

            public Node Pop()
            {
                var nodes = open.Ptr;
                var top = nodes[0];
                var last = nodes[open.Length - 1];
                open.Length--;
                var count = open.Length;
                if (count == 0)
                {
                    return top;
                }
                var i = 0;
                while (true)
                {
                    var child = 2 * i + 1;
                    if (child >= count)
                    {
                        break;
                    }
                    if (child + 1 < count && nodes[child + 1].IsBefore(nodes[child]))
                    {
                        child++;
                    }
                    if (!nodes[child].IsBefore(last))
                    {
                        break;
                    }
                    nodes[i] = nodes[child];
                    i = child;
                }
                nodes[i] = last;
                return top;
            }

            public void Dispose()
            {
                costs.Dispose();
                parents.Dispose();
                states.Dispose();
                open.Dispose();
                path.Dispose();
                points.Dispose();
            }
        }

        private struct Node
        {
            public int total;
            public int heuristic;
            public int index;

            // A strict total order, so equal costs never depend on the order the heap received them in.
            public bool IsBefore(in Node other)
            {
                if (total != other.total)
                {
                    return total < other.total;
                }
                if (heuristic != other.heuristic)
                {
                    return heuristic < other.heuristic;
                }
                return index < other.index;
            }
        }
    }
}
