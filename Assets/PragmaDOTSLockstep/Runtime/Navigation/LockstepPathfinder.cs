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
    /// <para>A <see cref="LockstepNavPathMap"/> plans for one agent: only through the cells its body fits in, and around
    /// crowds of standing agents when the way around is cheaper. A group of agents sent together shares one search
    /// (<see cref="SearchGroup"/>): it runs back from the goal of the group to the cells of all of them at once, and each
    /// agent's path follows its own chain of cells (<see cref="FollowChain"/>).</para>
    /// <para>The pathfinder keeps its scratch memory between searches; copies share it. It is not thread-safe: use one per
    /// thread, or one per chunk in a job, and dispose it once.</para>
    /// </remarks>
    public unsafe struct LockstepPathfinder : IDisposable
    {
        /// <summary>Cost of a straight step to a neighbouring cell; a diagonal step costs 14.</summary>
        public const int STRAIGHT_COST = 10;
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
                targets = new UnsafeList<byte>(cellCount, allocator),
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
            return FindPath(new LockstepNavPathMap(grid, cells), start, destination, waypoints);
        }

        /// <inheritdoc cref="FindPath(in LockstepNavGrid, NativeArray{LockstepNavCell}, FixedVector3, FixedVector3, NativeList{FixedVector3})"/>
        public LockstepPathStatus FindPath(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 start, FixedVector3 destination, DynamicBuffer<LockstepNavWaypoint> waypoints)
        {
            return FindPath(new LockstepNavPathMap(grid, cells), start, destination, waypoints);
        }

        /// <summary>Plans the path of one agent on its <paramref name="map"/>.</summary>
        public LockstepPathStatus FindPath(in LockstepNavPathMap map, FixedVector3 start, FixedVector3 destination, NativeList<FixedVector3> waypoints)
        {
            var status = Plan(map, start, destination);
            CopyPoints(waypoints);
            return status;
        }

        /// <inheritdoc cref="FindPath(in LockstepNavPathMap, FixedVector3, FixedVector3, NativeList{FixedVector3})"/>
        public LockstepPathStatus FindPath(in LockstepNavPathMap map, FixedVector3 start, FixedVector3 destination, DynamicBuffer<LockstepNavWaypoint> waypoints)
        {
            var status = Plan(map, start, destination);
            CopyPoints(waypoints);
            return status;
        }

        /// <summary>
        /// The cell a path from <paramref name="position"/> starts in: the cell under it (the nearest one inside the
        /// grid), or the nearest passable cell when that one is not. False when no cell is passable.
        /// </summary>
        public static bool TryGetStartCell(in LockstepNavPathMap map, FixedVector3 position, out int2 cell)
        {
            cell = map.grid.ClampToGrid(map.grid.WorldToCell(position));
            return map.IsPassable(cell) || LockstepNavigation.TryFindNearestPassable(map.grid, map.cells, cell, map.clearance, out cell);
        }

        /// <summary>
        /// The cell a path to <paramref name="position"/> ends in: the cell under it when it is inside the grid and
        /// passable (<paramref name="isOpen"/>), else the passable cell nearest to it. False when no cell is passable.
        /// </summary>
        public static bool TryGetEndCell(in LockstepNavPathMap map, FixedVector3 position, out int2 cell, out bool isOpen)
        {
            var under = map.grid.WorldToCell(position);
            cell = map.grid.ClampToGrid(under);
            isOpen = map.grid.Contains(under) && map.IsPassable(cell);
            return isOpen || LockstepNavigation.TryFindNearestPassable(map.grid, map.cells, cell, map.clearance, out cell);
        }

        /// <summary>
        /// Searches back from the <paramref name="goal"/> cell of a group of agents to the cells they start in
        /// (<paramref name="starts"/>, cell indices, passable ones: <see cref="TryGetStartCell"/>) at once, until every
        /// start is reached. Afterwards <see cref="GetChain"/> gives the shortest chain of cells from each reached start to
        /// the goal: one search instead of one per agent. The heuristic is the octile distance to the box around the
        /// starts, so the search heads for the group and closes every cell with its true cost. False when some start
        /// cannot be reached from the goal; the starts that can still have their chains.
        /// </summary>
        public bool SearchGroup(in LockstepNavPathMap map, int goal, NativeArray<int> starts)
        {
            ref var scratch = ref *_scratch;
            if (!map.IsValid || starts.Length == 0)
            {
                return false;
            }
            scratch.Reserve(map.grid.CellCount);
            var grid = map.grid;
            var costs = scratch.costs.Ptr;
            var parents = scratch.parents.Ptr;
            var states = scratch.states.Ptr;
            var targets = scratch.targets.Ptr;
            UnsafeUtility.MemClear(states, grid.CellCount);
            scratch.open.Clear();

            var min = grid.GetCell(starts[0]);
            var max = min;
            var remaining = 0;
            for (var i = 0; i < starts.Length; i++)
            {
                var cell = grid.GetCell(starts[i]);
                min = math.min(min, cell);
                max = math.max(max, cell);
                if (targets[starts[i]] == 0)
                {
                    targets[starts[i]] = 1;
                    remaining++;
                }
            }

            var goalHeuristic = BoxHeuristic(grid.GetCell(goal), min, max);
            costs[goal] = 0;
            parents[goal] = -1;
            states[goal] = OPEN;
            scratch.Push(new Node { total = goalHeuristic, heuristic = goalHeuristic, index = goal });

            while (scratch.open.Length > 0 && remaining > 0)
            {
                var node = scratch.Pop();
                if (states[node.index] == CLOSED)
                {
                    continue;
                }
                states[node.index] = CLOSED;
                if (targets[node.index] != 0)
                {
                    targets[node.index] = 0;
                    remaining--;
                }

                // Walking the chain the other way, an agent steps from the neighbour into this cell and pays its crowd.
                var enter = map.GetCrowdCost(node.index);
                var cell = grid.GetCell(node.index);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (!IsStep(map, cell, dx, dy, out var next))
                        {
                            continue;
                        }
                        var index = grid.GetIndex(next);
                        if (states[index] == CLOSED)
                        {
                            continue;
                        }
                        var cost = costs[node.index] + (dx != 0 && dy != 0 ? DIAGONAL_COST : STRAIGHT_COST) + enter;
                        if (states[index] == OPEN && cost >= costs[index])
                        {
                            continue;
                        }
                        costs[index] = cost;
                        parents[index] = node.index;
                        states[index] = OPEN;
                        var heuristic = BoxHeuristic(next, min, max);
                        scratch.Push(new Node { total = cost + heuristic, heuristic = heuristic, index = index });
                    }
                }
            }

            // Starts the search did not reach keep no mark for the next search.
            for (var i = 0; i < starts.Length; i++)
            {
                targets[starts[i]] = 0;
            }
            return remaining == 0;
        }

        /// <summary>
        /// The chain of cells (indices) from <paramref name="start"/> to the goal of the last <see cref="SearchGroup"/>,
        /// both included, into <paramref name="chain"/>; empty when the search did not reach the start.
        /// </summary>
        public void GetChain(int start, NativeList<int> chain)
        {
            chain.Clear();
            ref var scratch = ref *_scratch;
            if (start < 0 || start >= scratch.states.Length || scratch.states[start] != CLOSED)
            {
                return;
            }
            for (var index = start; index >= 0; index = scratch.parents[index])
            {
                chain.Add(index);
            }
        }

        /// <summary>
        /// The path of one agent of a group from <paramref name="start"/> along its <paramref name="chain"/> of cells to
        /// the group's goal (<see cref="GetChain"/>), ending at the agent's own <paramref name="destination"/> instead of
        /// the goal: string pulled, then straightened, so corners the destination does not need are left out.
        /// <see cref="LockstepPathStatus.Failed"/> when the chain does not lead to the destination (a wall stands between
        /// the goal and it): plan the agent alone (<see cref="FindPath(in LockstepNavPathMap, FixedVector3, FixedVector3, DynamicBuffer{LockstepNavWaypoint})"/>).
        /// </summary>
        public LockstepPathStatus FollowChain(in LockstepNavPathMap map, FixedVector3 start, FixedVector3 destination, NativeArray<int> chain,
                                              DynamicBuffer<LockstepNavWaypoint> waypoints)
        {
            var status = Follow(map, start, destination, chain);
            CopyPoints(waypoints);
            return status;
        }

        /// <inheritdoc cref="FollowChain(in LockstepNavPathMap, FixedVector3, FixedVector3, NativeArray{int}, DynamicBuffer{LockstepNavWaypoint})"/>
        public LockstepPathStatus FollowChain(in LockstepNavPathMap map, FixedVector3 start, FixedVector3 destination, NativeArray<int> chain,
                                              NativeList<FixedVector3> waypoints)
        {
            var status = Follow(map, start, destination, chain);
            CopyPoints(waypoints);
            return status;
        }

        private LockstepPathStatus Follow(in LockstepNavPathMap map, FixedVector3 start, FixedVector3 destination, NativeArray<int> chain)
        {
            ref var scratch = ref *_scratch;
            scratch.points.Clear();
            if (!map.IsValid || chain.Length == 0 || !TryGetEndCell(map, destination, out var endCell, out var isOpen))
            {
                return LockstepPathStatus.Failed;
            }

            var grid = map.grid;
            var height = destination.y;
            var end = isOpen ? destination.Xz : grid.GetCellCenter(endCell);
            var from = start.Xz;
            var first = 0;
            var isAtCenter = false;
            if (!map.IsPassable(grid.ClampToGrid(grid.WorldToCell(start))))
            {
                from = grid.GetCellCenter(grid.GetCell(chain[0]));
                scratch.AddPoint(from, height);
                first = 1;
                isAtCenter = true;
            }

            // Along the chain to the goal of the group, then on to the agent's own destination: from the last corner, or
            // else from the goal, when nothing stands between.
            var goal = grid.GetCellCenter(grid.GetCell(chain[chain.Length - 1]));
            from = Pull(map, from, isAtCenter, (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chain), chain.Length, goal, height);
            if (!LockstepNavigation.HasLineOfSight(grid, map.cells, from, end, map.clearance))
            {
                if (!LockstepNavigation.HasLineOfSight(grid, map.cells, goal, end, map.clearance))
                {
                    scratch.points.Clear();
                    return LockstepPathStatus.Failed;
                }
                scratch.AddPoint(goal, height);
            }
            scratch.AddPoint(end, height);

            Straighten(map, first > 0 ? scratch.points[0].Xz : start.Xz, first);
            return isOpen ? LockstepPathStatus.Complete : LockstepPathStatus.Partial;
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

        private void CopyPoints(NativeList<FixedVector3> waypoints)
        {
            ref var points = ref _scratch->points;
            waypoints.Clear();
            for (var i = 0; i < points.Length; i++)
            {
                waypoints.Add(points[i]);
            }
        }

        private void CopyPoints(DynamicBuffer<LockstepNavWaypoint> waypoints)
        {
            ref var points = ref _scratch->points;
            waypoints.Clear();
            for (var i = 0; i < points.Length; i++)
            {
                waypoints.Add(new LockstepNavWaypoint { position = points[i] });
            }
        }

        private LockstepPathStatus Plan(in LockstepNavPathMap map, FixedVector3 start, FixedVector3 destination)
        {
            ref var scratch = ref *_scratch;
            scratch.points.Clear();
            if (!map.IsValid || !TryGetStartCell(map, start, out var startCell))
            {
                return LockstepPathStatus.Failed;
            }
            scratch.Reserve(map.grid.CellCount);
            var grid = map.grid;
            var isStartBlocked = !map.IsPassable(grid.ClampToGrid(grid.WorldToCell(start)));

            // The start cell is passable, so there is a nearest one to the destination.
            TryGetEndCell(map, destination, out var goalCell, out var isDestinationOpen);

            var isReached = Search(ref scratch, map, grid.GetIndex(startCell), grid.GetIndex(goalCell), out var endIndex);
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

            ref var path = ref scratch.path;
            Pull(map, from, isStartBlocked, path.Ptr, path.Length, end, height);
            if (path.Length > 1 || isComplete)
            {
                scratch.AddPoint(end, height);
            }
            return isComplete ? LockstepPathStatus.Complete : LockstepPathStatus.Partial;
        }

        // String pulling along a chain of cells (indices) from `from`, which stands in the first of them, to `end`, in or
        // next to the last one: adds the corners, the end left out, and returns the last corner (`from` without one).
        // From each corner the next is the farthest cell of the chain it sees, found by doubling the reach while it sees
        // and then halving the gap, so a long path costs a few line tests per corner. The ground between neighbouring
        // cells is always open, because the search never cuts a blocked corner, so the corners keep the path on it.
        private FixedVector2 Pull(in LockstepNavPathMap map, FixedVector2 from, bool isAtCenter, int* cells, int length, FixedVector2 end, FixedPoint height)
        {
            ref var scratch = ref *_scratch;
            var grid = map.grid;
            var last = length - 1;
            var current = 0;
            while (current < last && !CanPull(map, from, end))
            {
                var seen = current;
                var unseen = last;
                for (var reach = 1; current + reach < last; reach *= 2)
                {
                    if (!CanPull(map, from, GetCenter(grid, cells[current + reach])))
                    {
                        unseen = current + reach;
                        break;
                    }
                    seen = current + reach;
                }
                while (unseen - seen > 1)
                {
                    var middle = (seen + unseen) >> 1;
                    if (CanPull(map, from, GetCenter(grid, cells[middle])))
                    {
                        seen = middle;
                    }
                    else
                    {
                        unseen = middle;
                    }
                }
                if (seen == current)
                {
                    // Not even the next cell: a corner in the middle of this one first, then on to the next one.
                    if (!isAtCenter)
                    {
                        from = GetCenter(grid, cells[current]);
                        isAtCenter = true;
                        scratch.AddPoint(from, height);
                        continue;
                    }
                    seen = current + 1;
                }
                from = GetCenter(grid, cells[seen]);
                isAtCenter = true;
                current = seen;
                scratch.AddPoint(from, height);
            }
            return from;
        }

        private static FixedVector2 GetCenter(in LockstepNavGrid grid, int index)
        {
            return grid.GetCellCenter(grid.GetCell(index));
        }

        // A* from start to goal. Returns whether the goal was reached; end is the goal or, when it cannot be reached, the
        // searched cell with the smallest heuristic.
        private static bool Search(ref Scratch scratch, in LockstepNavPathMap map, int start, int goal, out int end)
        {
            var grid = map.grid;
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
                        if (!IsStep(map, cell, dx, dy, out var next))
                        {
                            continue;
                        }
                        var index = grid.GetIndex(next);
                        if (states[index] == CLOSED)
                        {
                            continue;
                        }
                        var cost = costs[node.index] + (dx != 0 && dy != 0 ? DIAGONAL_COST : STRAIGHT_COST) + map.GetCrowdCost(index);
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

        // A step from the cell to its neighbour (dx, dy): onto a passable cell, and diagonally only past two passable ones,
        // so a path never cuts a blocked corner.
        private static bool IsStep(in LockstepNavPathMap map, int2 cell, int dx, int dy, out int2 next)
        {
            next = new int2(cell.x + dx, cell.y + dy);
            if (dx == 0 && dy == 0 || !map.IsPassable(next))
            {
                return false;
            }
            return dx == 0 || dy == 0 || map.IsPassable(new int2(next.x, cell.y)) && map.IsPassable(new int2(cell.x, next.y));
        }

        // Octile distance in the units of the step costs, which keeps the search optimal.
        private static int Heuristic(int2 from, int2 to)
        {
            var delta = math.abs(to - from);
            return STRAIGHT_COST * math.max(delta.x, delta.y) + (DIAGONAL_COST - STRAIGHT_COST) * math.min(delta.x, delta.y);
        }

        // Octile distance to the nearest cell of a box: never more than the distance to any cell in it, and it changes by
        // at most a step's cost between neighbours, so a search with it closes every cell at its true cost.
        private static int BoxHeuristic(int2 from, int2 min, int2 max)
        {
            return Heuristic(from, math.clamp(from, min, max));
        }

        // A straight line the path may take between two points: through cells the agent's body fits in, and only through
        // crowded cells where it starts or ends, so a path that went around a crowd is not pulled straight through it.
        private static bool CanPull(in LockstepNavPathMap map, FixedVector2 from, FixedVector2 to)
        {
            if (!map.HasCrowd)
            {
                return LockstepNavigation.HasLineOfSight(map.grid, map.cells, from, to, map.clearance);
            }
            var test = new PullTest { map = map, first = map.grid.WorldToCell(from), last = map.grid.WorldToCell(to) };
            return LockstepNavigation.Trace(map.grid, from, to, ref test);
        }

        private struct PullTest : LockstepNavigation.ICellTest
        {
            public LockstepNavPathMap map;
            public int2 first;
            public int2 last;

            public bool IsOpen(int2 cell)
            {
                return map.IsPassable(cell) &&
                       (math.all(cell == first) || math.all(cell == last) || map.crowd[map.grid.GetIndex(cell)] == 0);
            }
        }

        // Drops the corners after point `first` that the line from the corner before them can skip: a chain bends towards
        // the goal of its group, which the agent's own destination may not need.
        private void Straighten(in LockstepNavPathMap map, FixedVector2 start, int first)
        {
            ref var points = ref _scratch->points;
            var from = start;
            var write = first;
            var i = first;
            while (i < points.Length)
            {
                var j = points.Length - 1;
                while (j > i && !CanPull(map, from, points[j].Xz))
                {
                    j--;
                }
                points[write++] = points[j];
                from = points[j].Xz;
                i = j + 1;
            }
            points.Length = write;
        }

        private struct Scratch
        {
            public UnsafeList<int> costs;
            public UnsafeList<int> parents;
            public UnsafeList<byte> states;
            // Cells a group search still has to reach; all zero between searches.
            public UnsafeList<byte> targets;
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
                targets.Resize(cellCount, NativeArrayOptions.ClearMemory);
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
                targets.Dispose();
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
