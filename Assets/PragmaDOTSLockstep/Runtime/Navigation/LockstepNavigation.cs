using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>Queries on a <see cref="LockstepNavGrid"/> and its cells, in integer and fixed-point math only.</summary>
    public static class LockstepNavigation
    {
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
            var cell = grid.WorldToCell(from);
            var last = grid.WorldToCell(to);
            if (!IsWalkable(grid, cells, cell))
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
                        if (!IsWalkable(grid, cells, new int2(cell.x + stepX, cell.y)) || !IsWalkable(grid, cells, new int2(cell.x, cell.y + stepY)))
                        {
                            return false;
                        }
                        next = new int2(cell.x + stepX, cell.y + stepY);
                        remaining--;
                    }
                }

                if (!IsWalkable(grid, cells, next))
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
            nearest = cell;
            if (IsWalkable(grid, cells, cell))
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
                        if (distance >= nearestDistance || !IsWalkable(grid, cells, candidate))
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
                    var blockers = math.clamp(cells[index].blockers + count, 0, ushort.MaxValue);
                    cells[index] = new LockstepNavCell { blockers = (ushort)blockers };
                }
            }
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
