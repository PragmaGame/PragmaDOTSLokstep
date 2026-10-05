using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Mathematics;

namespace Pragma.Lockstep.Vision
{
    /// <summary>
    /// Queries of the <see cref="LockstepVisionGrid"/>: does a slot see a point or a round body, which cells does it see.
    /// Burst-compatible; every function takes the grid and its whole <see cref="LockstepVisionCell"/> buffer.
    /// </summary>
    /// <remarks>
    /// A cell is visible when a circle of sight reaches into it, not only when it covers the cell's centre: whatever lies
    /// within a source's radius lies in a visible cell, so a unit never fights an enemy its player cannot see. Distances
    /// are compared on raw fixed-point values, the same on every client.
    /// </remarks>
    public static class LockstepVision
    {
        /// <summary>The buffer holds one plane per slot of the grid: <see cref="LockstepVisionSystem"/> has filled it.</summary>
        public static bool HasCells(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells)
        {
            return grid.IsValid && grid.slotCount > 0 && cells.IsCreated && cells.Length == grid.CellCount * grid.slotCount;
        }

        /// <summary>The cells <paramref name="slot"/> sees, row by row along X, to draw its fog; empty for a slot the grid has no plane of.</summary>
        public static NativeArray<LockstepVisionCell> GetPlane(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells, int slot)
        {
            return HasCells(grid, cells) && IsSlot(grid, slot) ? cells.GetSubArray(slot * grid.CellCount, grid.CellCount) : default;
        }

        /// <summary><paramref name="slot"/> sees the point <paramref name="position"/> (X and Z).</summary>
        public static bool IsVisible(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells, int slot, FixedVector2 position)
        {
            return IsVisible(grid, cells, slot, position, FixedPoint.Zero);
        }

        /// <summary>
        /// <paramref name="slot"/> sees some part of a body of <paramref name="radius"/> around <paramref name="position"/>
        /// (X and Z): a building is seen as soon as its edge is.
        /// </summary>
        public static bool IsVisible(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells, int slot, FixedVector2 position, FixedPoint radius)
        {
            if (grid.isRevealed)
            {
                return true;
            }
            if (!HasCells(grid, cells) || !IsSlot(grid, slot) || radius.rawValue < 0)
            {
                return false;
            }

            var offset = slot * grid.CellCount;
            var layout = grid.Layout;
            GetBounds(layout, position, radius, out var min, out var max);
            for (var y = min.y; y <= max.y; y++)
            {
                for (var x = min.x; x <= max.x; x++)
                {
                    var cell = new int2(x, y);
                    if (cells[offset + layout.GetIndex(cell)].isVisible && Reaches(layout, cell, position, radius))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary><paramref name="slot"/> sees the cell; false outside the grid unless the map is revealed.</summary>
        public static bool IsCellVisible(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells, int slot, int2 cell)
        {
            if (grid.isRevealed)
            {
                return true;
            }
            return HasCells(grid, cells) && IsSlot(grid, slot) && grid.Contains(cell) && cells[slot * grid.CellCount + grid.Layout.GetIndex(cell)].isVisible;
        }

        /// <summary>
        /// Marks the cells a circle of sight of <paramref name="radius"/> around <paramref name="position"/> reaches into as
        /// seen by <paramref name="slot"/>. <see cref="LockstepVisionSystem"/> stamps every source on every tick.
        /// </summary>
        public static void Stamp(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells, int slot, FixedVector2 position, FixedPoint radius)
        {
            if (!HasCells(grid, cells) || !IsSlot(grid, slot) || radius.rawValue < 0)
            {
                return;
            }

            var offset = slot * grid.CellCount;
            var layout = grid.Layout;
            GetBounds(layout, position, radius, out var min, out var max);
            var visible = new LockstepVisionCell { isVisible = true };
            for (var y = min.y; y <= max.y; y++)
            {
                for (var x = min.x; x <= max.x; x++)
                {
                    var cell = new int2(x, y);
                    if (Reaches(layout, cell, position, radius))
                    {
                        cells[offset + layout.GetIndex(cell)] = visible;
                    }
                }
            }
        }

        private static bool IsSlot(in LockstepVisionGrid grid, int slot) => slot >= 0 && slot < grid.slotCount;

        // The cells of the grid under the square around the circle; empty (min > max) when it misses the grid. A point maps
        // to the cell it starts, so the low side looks one step further: the cell that ends where the square begins touches
        // it too, as the cell that starts where it ends does.
        private static void GetBounds(in FixedGrid layout, FixedVector2 position, FixedPoint radius, out int2 min, out int2 max)
        {
            var low = new FixedVector2(FixedPoint.FromRaw(position.x.rawValue - radius.rawValue - 1), FixedPoint.FromRaw(position.y.rawValue - radius.rawValue - 1));
            var high = new FixedVector2(FixedPoint.FromRaw(position.x.rawValue + radius.rawValue), FixedPoint.FromRaw(position.y.rawValue + radius.rawValue));
            min = math.max(layout.WorldToCell(low), int2.zero);
            max = math.min(layout.WorldToCell(high), new int2(layout.width - 1, layout.height - 1));
        }

        // The circle reaches into the cell: the cell's point nearest to the centre lies within the radius.
        private static bool Reaches(in FixedGrid layout, int2 cell, FixedVector2 position, FixedPoint radius)
        {
            var cellMin = layout.GetCellMin(cell);
            var size = layout.cellSize.rawValue;
            var dx = GetGap(position.x.rawValue, cellMin.x.rawValue, size);
            var dz = GetGap(position.y.rawValue, cellMin.y.rawValue, size);
            var r = radius.rawValue;
            if (dx > r || dz > r)
            {
                return false;
            }
            return dx * dx + dz * dz <= r * r;
        }

        // Distance along one axis from a coordinate to the span [min, min + size]; zero inside it.
        private static long GetGap(long value, long min, long size)
        {
            return value < min ? min - value : value > min + size ? value - min - size : 0;
        }
    }
}
