using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>Queries on a <see cref="LockstepNavGrid"/>, its cells and its ground, in integer and fixed-point math only.</summary>
    public static class LockstepNavigation
    {
        /// <summary>The heights describe the ground of the grid: one per cell corner.</summary>
        public static bool HasHeights(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights)
        {
            return grid.IsValid && heights.Length == grid.CornerCount;
        }

        /// <summary>
        /// World Y of the ground at a world position (X and Z): bilinear between the corners of its cell, so a plane comes
        /// out exact. Outside the grid, the ground of its nearest edge. The heights must match the grid
        /// (<see cref="HasHeights"/>).
        /// </summary>
        public static FixedPoint GetHeight(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights, FixedVector2 position)
        {
            var size = grid.cellSize.rawValue;
            var offsetX = math.clamp(position.x.rawValue - grid.origin.x.rawValue, 0L, size * grid.width);
            var offsetZ = math.clamp(position.y.rawValue - grid.origin.y.rawValue, 0L, size * grid.height);
            var x = (int)math.min(offsetX / size, grid.width - 1);
            var z = (int)math.min(offsetZ / size, grid.height - 1);
            var u = FixedPoint.FromRaw(offsetX - x * size) / grid.cellSize;
            var v = FixedPoint.FromRaw(offsetZ - z * size) / grid.cellSize;
            var near = GetHeight(grid, heights, x, z);
            var nearRight = GetHeight(grid, heights, x + 1, z);
            var far = GetHeight(grid, heights, x, z + 1);
            var farRight = GetHeight(grid, heights, x + 1, z + 1);
            var nearEdge = near + (nearRight - near) * u;
            var farEdge = far + (farRight - far) * u;
            return nearEdge + (farEdge - nearEdge) * v;
        }

        /// <summary>The position on the ground: its Y is the height of the ground under it, or unchanged without heights.</summary>
        public static FixedVector3 ToGround(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights, FixedVector3 position)
        {
            return HasHeights(grid, heights) ? new FixedVector3(position.x, GetHeight(grid, heights, position.Xz), position.z) : position;
        }

        /// <summary>
        /// The cell is too steep to walk on: two neighbouring corners of it differ in height by more than
        /// <see cref="LockstepNavGrid.maxSlope"/> times the cell size. False outside the grid, without heights or without a
        /// slope limit.
        /// </summary>
        public static bool IsSteep(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights, int2 cell)
        {
            if (grid.maxSlope.rawValue <= 0 || !HasHeights(grid, heights) || !grid.Contains(cell))
            {
                return false;
            }
            var limit = grid.maxSlope * grid.cellSize;
            var near = GetHeight(grid, heights, cell.x, cell.y);
            var nearRight = GetHeight(grid, heights, cell.x + 1, cell.y);
            var far = GetHeight(grid, heights, cell.x, cell.y + 1);
            var farRight = GetHeight(grid, heights, cell.x + 1, cell.y + 1);
            return FixedMath.Abs(nearRight - near) > limit ||
                   FixedMath.Abs(far - near) > limit ||
                   FixedMath.Abs(farRight - nearRight) > limit ||
                   FixedMath.Abs(farRight - far) > limit;
        }

        /// <summary>The cell is inside the grid and no obstacle covers it.</summary>
        public static bool IsWalkable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int2 cell)
        {
            return grid.Contains(cell) && cells[grid.GetIndex(cell)].blockers == 0;
        }

        /// <summary>The cell under the position is walkable; Y is ignored.</summary>
        public static bool IsWalkable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 position)
        {
            return IsWalkable(grid, cells, grid.WorldToCell(position));
        }

        /// <summary>
        /// The clearance (<see cref="LockstepNavCell.clearance"/>) a cell needs for the body of an agent of
        /// <paramref name="radius"/> to stand on it clear of obstacles: 1, any walkable cell, for a body up to the grid's
        /// <see cref="LockstepNavGrid.agentRadius"/>, which obstacles are grown by; one more for every cell size the body
        /// is larger, rounded to the nearest, as cells only approximate obstacles anyway. At most 255.
        /// </summary>
        public static int GetClearance(in LockstepNavGrid grid, FixedPoint radius)
        {
            var extra = radius.rawValue - grid.agentRadius.rawValue;
            if (extra <= 0 || grid.cellSize.rawValue <= 0)
            {
                return 1;
            }
            var size = grid.cellSize.rawValue;
            return (int)math.min(1 + (extra + size / 2) / size, byte.MaxValue);
        }

        /// <summary>
        /// A body that needs <paramref name="clearance"/> (<see cref="GetClearance"/>) fits on the cell: it is inside the
        /// grid and walkable, and above a clearance of 1, its <see cref="LockstepNavCell.clearance"/> is at least that.
        /// </summary>
        public static bool IsPassable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int2 cell, int clearance)
        {
            if (!grid.Contains(cell))
            {
                return false;
            }
            var data = cells[grid.GetIndex(cell)];
            return clearance <= 1 ? data.blockers == 0 : data.clearance >= clearance;
        }

        /// <summary>The cell under the position is passable for the clearance; Y is ignored.</summary>
        public static bool IsPassable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 position, int clearance)
        {
            return IsPassable(grid, cells, grid.WorldToCell(position), clearance);
        }

        /// <summary>
        /// Where a step (X and Z) from a position may end without entering a blocked cell: the whole step, else only
        /// its X part, else only its Z part, so whatever walks or is pushed into a wall slides along it. False when
        /// none of them is walkable. A step from a blocked cell is free: whatever stands in an obstacle is on its way
        /// out of it. Y is kept.
        /// </summary>
        public static bool TryStep(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 from, FixedVector2 step, out FixedVector3 position)
        {
            return TryStep(grid, cells, from, step, 1, out position);
        }

        /// <summary>
        /// <see cref="TryStep(in LockstepNavGrid, NativeArray{LockstepNavCell}, FixedVector3, FixedVector2, out FixedVector3)"/>
        /// for a body that needs <paramref name="clearance"/>: the step ends on a cell passable for it. From a cell too
        /// narrow for it, the step only has to end on a walkable cell, so the body can get out (<see cref="GetStepClearance"/>).
        /// </summary>
        public static bool TryStep(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 from, FixedVector2 step, int clearance, out FixedVector3 position)
        {
            position = new FixedVector3(from.x + step.x, from.y, from.z + step.y);
            if (!IsWalkable(grid, cells, from))
            {
                return true;
            }
            clearance = GetStepClearance(grid, cells, from, clearance);
            if (IsPassable(grid, cells, position, clearance))
            {
                return true;
            }
            position = new FixedVector3(from.x + step.x, from.y, from.z);
            if (step.x.rawValue != 0 && IsPassable(grid, cells, position, clearance))
            {
                return true;
            }
            position = new FixedVector3(from.x, from.y, from.z + step.y);
            return step.y.rawValue != 0 && IsPassable(grid, cells, position, clearance);
        }

        /// <summary>
        /// The clearance a step from <paramref name="from"/> has to end on: the body's own, or 1 when the body already
        /// stands on a cell too narrow for it (pushed there, or an obstacle placed next to it) and is on its way out.
        /// </summary>
        public static int GetStepClearance(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 from, int clearance)
        {
            return clearance > 1 && !IsPassable(grid, cells, from, clearance) ? 1 : clearance;
        }

        /// <summary>Every cell the segment touches is walkable; Y is ignored.</summary>
        public static bool HasLineOfSight(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 from, FixedVector3 to)
        {
            return HasLineOfSight(grid, cells, from.Xz, to.Xz);
        }

        /// <summary>
        /// Every cell the segment between two world positions (X and Z) touches is walkable. Exact: the segment is traced
        /// through the cell borders with integer math, and where it passes exactly through a cell corner, both cells beside
        /// the corner count.
        /// </summary>
        public static bool HasLineOfSight(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector2 from, FixedVector2 to)
        {
            return HasLineOfSight(grid, cells, from, to, 1);
        }

        /// <summary>Every cell the segment between two world positions (X and Z) touches is passable for the clearance.</summary>
        public static bool HasLineOfSight(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector2 from, FixedVector2 to, int clearance)
        {
            var test = new PassableTest { grid = grid, cells = cells, clearance = clearance };
            return Trace(grid, from, to, ref test);
        }

        /// <summary>A test of the cells a segment touches (<see cref="Trace{T}"/>).</summary>
        internal interface ICellTest
        {
            bool IsOpen(int2 cell);
        }

        private struct PassableTest : ICellTest
        {
            public LockstepNavGrid grid;
            public NativeArray<LockstepNavCell> cells;
            public int clearance;

            public bool IsOpen(int2 cell) => IsPassable(grid, cells, cell, clearance);
        }

        /// <summary>
        /// Every cell the segment between two world positions (X and Z) touches passes the test, the cell it starts in
        /// first. Exact: the segment is traced through the cell borders with integer math, and where it passes exactly
        /// through a cell corner, both cells beside the corner count.
        /// </summary>
        internal static bool Trace<T>(in LockstepNavGrid grid, FixedVector2 from, FixedVector2 to, ref T test) where T : struct, ICellTest
        {
            var cell = grid.WorldToCell(from);
            var last = grid.WorldToCell(to);
            if (!test.IsOpen(cell))
            {
                return false;
            }

            var size = grid.cellSize.rawValue;
            var x0 = from.x.rawValue - grid.origin.x.rawValue;
            var y0 = from.y.rawValue - grid.origin.y.rawValue;
            var spanX = to.x.rawValue - from.x.rawValue;
            var spanY = to.y.rawValue - from.y.rawValue;
            var stepX = spanX > 0 ? 1 : spanX < 0 ? -1 : 0;
            var stepY = spanY > 0 ? 1 : spanY < 0 ? -1 : 0;
            spanX = math.abs(spanX);
            spanY = math.abs(spanY);

            // Every step moves one cell closer to the last one, two through a corner.
            var remaining = math.abs(last.x - cell.x) + math.abs(last.y - cell.y);
            while (remaining > 0)
            {
                int2 next;
                if (stepX == 0)
                {
                    next = new int2(cell.x, cell.y + stepY);
                }
                else if (stepY == 0)
                {
                    next = new int2(cell.x + stepX, cell.y);
                }
                else
                {
                    // The segment reaches the next border along X at time distanceX / spanX and along Y at
                    // distanceY / spanY. Comparing the cross products instead of the quotients keeps the trace exact.
                    var borderX = (long)(stepX > 0 ? cell.x + 1 : cell.x) * size;
                    var borderY = (long)(stepY > 0 ? cell.y + 1 : cell.y) * size;
                    var timeX = (stepX > 0 ? borderX - x0 : x0 - borderX) * spanY;
                    var timeY = (stepY > 0 ? borderY - y0 : y0 - borderY) * spanX;
                    if (timeX < timeY)
                    {
                        next = new int2(cell.x + stepX, cell.y);
                    }
                    else if (timeX > timeY)
                    {
                        next = new int2(cell.x, cell.y + stepY);
                    }
                    else
                    {
                        if (!test.IsOpen(new int2(cell.x + stepX, cell.y)) || !test.IsOpen(new int2(cell.x, cell.y + stepY)))
                        {
                            return false;
                        }
                        next = new int2(cell.x + stepX, cell.y + stepY);
                        remaining--;
                    }
                }

                if (!test.IsOpen(next))
                {
                    return false;
                }
                cell = next;
                remaining--;
            }
            return true;
        }

        /// <summary>
        /// The walkable cell nearest to a cell of the grid, by distance between the centres: the cell itself when it is
        /// walkable. Ties go to the cell found first, scanning rings row by row.
        /// </summary>
        public static bool TryFindNearestWalkable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int2 cell, out int2 nearest)
        {
            return TryFindNearestPassable(grid, cells, cell, 1, out nearest);
        }

        /// <summary>
        /// The cell passable for the clearance nearest to a cell of the grid, by distance between the centres: the cell
        /// itself when it is passable. Ties go to the cell found first, scanning rings row by row.
        /// </summary>
        public static bool TryFindNearestPassable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int2 cell, int clearance, out int2 nearest)
        {
            nearest = cell;
            if (IsPassable(grid, cells, cell, clearance))
            {
                return true;
            }

            var isFound = false;
            var nearestDistance = long.MaxValue;
            var maxRadius = math.max(grid.width, grid.height);
            for (var radius = 1; radius <= maxRadius; radius++)
            {
                // Every cell of this ring and of the ones after it is at least the radius away.
                if (isFound && (long)radius * radius > nearestDistance)
                {
                    break;
                }
                for (var dy = -radius; dy <= radius; dy++)
                {
                    // The first and last rows of a ring are full, the rows between have only their two ends.
                    var stepX = dy == -radius || dy == radius ? 1 : 2 * radius;
                    for (var dx = -radius; dx <= radius; dx += stepX)
                    {
                        var candidate = new int2(cell.x + dx, cell.y + dy);
                        var distance = (long)dx * dx + (long)dy * dy;
                        if (distance >= nearestDistance || !IsPassable(grid, cells, candidate, clearance))
                        {
                            continue;
                        }
                        nearestDistance = distance;
                        nearest = candidate;
                        isFound = true;
                    }
                }
            }
            return isFound;
        }

        /// <summary>
        /// Adds <paramref name="count"/> blockers to every cell the footprint covers (see <see cref="Covers"/>), or removes
        /// them when the count is negative; cells outside the grid are skipped. <see cref="LockstepNavObstacleSystem"/> does
        /// this for obstacle entities: call it only on cells you own, such as an editor preview or a test.
        /// </summary>
        public static void Stamp(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, in LockstepNavObstacleFootprint footprint, int count)
        {
            GetCoverage(grid, footprint, out var min, out var max);
            if (max.x < 0 || max.y < 0 || min.x >= grid.width || min.y >= grid.height)
            {
                return;
            }
            min = grid.ClampToGrid(min);
            max = grid.ClampToGrid(max);

            for (var y = min.y; y <= max.y; y++)
            {
                for (var x = min.x; x <= max.x; x++)
                {
                    var cell = new int2(x, y);
                    if (!Covers(grid, footprint, cell))
                    {
                        continue;
                    }
                    var index = grid.GetIndex(cell);
                    var data = cells[index];
                    data.blockers = (ushort)math.clamp(data.blockers + count, 0, ushort.MaxValue);
                    cells[index] = data;
                }
            }
        }

        /// <summary>
        /// Computes the <see cref="LockstepNavCell.clearance"/> of every cell from the blocked ones: the Chebyshev distance
        /// to the nearest blocked cell, the outside of the grid counting as blocked, at most 255. Two passes over the grid,
        /// so call it once after a batch of stamps: <see cref="LockstepNavObstacleSystem"/> does it whenever a cell changed.
        /// </summary>
        public static void UpdateClearance(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells)
        {
            if (!grid.IsValid || cells.Length != grid.CellCount)
            {
                return;
            }

            // Forward: from the cells already visited, left and below.
            for (var y = 0; y < grid.height; y++)
            {
                for (var x = 0; x < grid.width; x++)
                {
                    var index = y * grid.width + x;
                    var data = cells[index];
                    if (data.blockers != 0)
                    {
                        data.clearance = 0;
                    }
                    else
                    {
                        var value = math.min(GetRing(grid, cells, x - 1, y), GetRing(grid, cells, x - 1, y - 1));
                        value = math.min(value, math.min(GetRing(grid, cells, x, y - 1), GetRing(grid, cells, x + 1, y - 1)));
                        data.clearance = (byte)math.min(value + 1, byte.MaxValue);
                    }
                    cells[index] = data;
                }
            }

            // Backward: from the cells right and above, which the forward pass could not see.
            for (var y = grid.height - 1; y >= 0; y--)
            {
                for (var x = grid.width - 1; x >= 0; x--)
                {
                    var index = y * grid.width + x;
                    var data = cells[index];
                    if (data.blockers != 0)
                    {
                        continue;
                    }
                    var value = math.min(GetRing(grid, cells, x + 1, y), GetRing(grid, cells, x + 1, y + 1));
                    value = math.min(value, math.min(GetRing(grid, cells, x, y + 1), GetRing(grid, cells, x - 1, y + 1)));
                    if (value + 1 < data.clearance)
                    {
                        data.clearance = (byte)(value + 1);
                        cells[index] = data;
                    }
                }
            }
        }

        // The clearance of a neighbour in the passes of UpdateClearance; outside the grid is blocked.
        private static int GetRing(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int x, int y)
        {
            if (x < 0 || y < 0 || x >= grid.width || y >= grid.height)
            {
                return 0;
            }
            return cells[y * grid.width + x].clearance;
        }

        /// <summary>
        /// The footprint could be stamped without touching a blocked cell: every cell it covers is inside the grid and
        /// walkable. Use it to check where a building may be placed; false when the cells do not match the grid.
        /// </summary>
        public static bool IsClear(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, in LockstepNavObstacleFootprint footprint)
        {
            if (!grid.IsValid || cells.Length != grid.CellCount)
            {
                return false;
            }

            GetCoverage(grid, footprint, out var min, out var max);
            for (var y = min.y; y <= max.y; y++)
            {
                for (var x = min.x; x <= max.x; x++)
                {
                    var cell = new int2(x, y);
                    if (Covers(grid, footprint, cell) && !IsWalkable(grid, cells, cell))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// The footprint blocks the cell: the cell centre is within the grid's agent radius of the rectangle. The cell may
        /// lie outside the grid, so footprints that are not stamped yet can be checked against each other.
        /// </summary>
        public static bool Covers(in LockstepNavGrid grid, in LockstepNavObstacleFootprint footprint, int2 cell)
        {
            // How far the cell centre lies outside the rectangle, along each of its axes.
            var offset = grid.GetCellCenter(cell) - footprint.center;
            var outsideX = FixedMath.Max(FixedMath.Abs(FixedMath.Dot(offset, footprint.right)) - footprint.halfSize.x, FixedPoint.Zero);
            var outsideZ = FixedMath.Max(FixedMath.Abs(FixedMath.Dot(offset, footprint.Forward)) - footprint.halfSize.y, FixedPoint.Zero);
            return outsideX * outsideX + outsideZ * outsideZ <= grid.agentRadius * grid.agentRadius;
        }

        private static FixedPoint GetHeight(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights, int x, int z)
        {
            return heights[grid.GetCornerIndex(new int2(x, z))].value;
        }

        /// <summary>
        /// Every cell the footprint covers lies between <paramref name="min"/> and <paramref name="max"/> (inclusive); the
        /// range is not clamped to the grid. Walk it with <see cref="Covers"/>.
        /// </summary>
        public static void GetCoverage(in LockstepNavGrid grid, in LockstepNavObstacleFootprint footprint, out int2 min, out int2 max)
        {
            var radius = grid.agentRadius;
            var right = footprint.right;
            var forward = footprint.Forward;
            var halfSize = footprint.halfSize;
            var reach = new FixedVector2(
                FixedMath.Abs(right.x) * halfSize.x + FixedMath.Abs(forward.x) * halfSize.y + radius,
                FixedMath.Abs(right.y) * halfSize.x + FixedMath.Abs(forward.y) * halfSize.y + radius);
            min = grid.WorldToCell(footprint.center - reach);
            max = grid.WorldToCell(footprint.center + reach);
        }
    }
}
