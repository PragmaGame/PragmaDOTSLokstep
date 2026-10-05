using Unity.Mathematics;

namespace Pragma.Lockstep.Mathematics
{
    /// <summary>
    /// A rectangle of square cells on the XZ plane: which cell holds a position, where a cell lies, how cells are
    /// numbered. The grids of the simulation (navigation, vision) keep these four values and do their cell math here.
    /// </summary>
    /// <remarks>
    /// Cell (x, y) starts at world X <c>origin.x + x * cellSize</c> and world Z <c>origin.y + y * cellSize</c> and is one
    /// cell size wide; cells are numbered row by row along X. Positions map to cells with integer math on raw fixed-point
    /// values, so every client puts a position into the same cell.
    /// </remarks>
    public readonly struct FixedGrid
    {
        /// <summary>World X and Z of the outer corner of cell (0, 0).</summary>
        public readonly FixedVector2 origin;
        public readonly FixedPoint cellSize;
        /// <summary>Number of cells along world X.</summary>
        public readonly int width;
        /// <summary>Number of cells along world Z.</summary>
        public readonly int height;

        public FixedGrid(FixedVector2 origin, FixedPoint cellSize, int width, int height)
        {
            this.origin = origin;
            this.cellSize = cellSize;
            this.width = width;
            this.height = height;
        }

        public int CellCount => width * height;

        public bool IsValid => width > 0 && height > 0 && cellSize.rawValue > 0;

        public bool Contains(int2 cell) => cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

        public int GetIndex(int2 cell) => cell.y * width + cell.x;

        public int2 GetCell(int index) => new int2(index % width, index / width);

        /// <summary>The cell that holds a world position (X and Z); it may lie outside the grid.</summary>
        public int2 WorldToCell(FixedVector2 position)
        {
            return new int2(FloorDivide(position.x.rawValue - origin.x.rawValue), FloorDivide(position.y.rawValue - origin.y.rawValue));
        }

        /// <summary>World X and Z of the corner where a cell starts: its smallest X and Z.</summary>
        public FixedVector2 GetCellMin(int2 cell)
        {
            return new FixedVector2(
                FixedPoint.FromRaw(origin.x.rawValue + cell.x * cellSize.rawValue),
                FixedPoint.FromRaw(origin.y.rawValue + cell.y * cellSize.rawValue));
        }

        /// <summary>World X and Z of the centre of a cell.</summary>
        public FixedVector2 GetCellCenter(int2 cell)
        {
            var half = cellSize.rawValue / 2;
            var min = GetCellMin(cell);
            return new FixedVector2(FixedPoint.FromRaw(min.x.rawValue + half), FixedPoint.FromRaw(min.y.rawValue + half));
        }

        /// <summary>The nearest cell inside the grid.</summary>
        public int2 ClampToGrid(int2 cell) => math.clamp(cell, int2.zero, new int2(width - 1, height - 1));

        private int FloorDivide(long offset)
        {
            var size = cellSize.rawValue;
            var quotient = offset / size;
            if (offset % size != 0 && offset < 0)
            {
                quotient--;
            }
            // Far away positions only need to stay outside the grid.
            const long limit = 1L << 30;
            return (int)(quotient < -limit ? -limit : quotient > limit ? limit : quotient);
        }
    }
}
