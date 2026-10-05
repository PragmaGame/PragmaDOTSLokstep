using Pragma.Lockstep.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// The walkability grid on the XZ plane that agents plan their paths on, a singleton in the simulation world. Its
    /// cells are the <see cref="LockstepNavCell"/> buffer of the same entity, and the ground agents walk on is its optional
    /// <see cref="LockstepNavHeight"/> buffer.
    /// </summary>
    /// <remarks>
    /// The cells are laid out as a <see cref="FixedGrid"/> (<see cref="Layout"/>): cell (x, y) starts at world X
    /// <c>origin.x + x * cellSize</c> and world Z <c>origin.y + y * cellSize</c>, and positions map to cells with integer
    /// math, so every client puts a position into the same cell. Bake the grid with <c>LockstepNavGridAuthoring</c> into
    /// the subscene of the map, or create the entity in code before tick 0; <see cref="LockstepNavObstacleSystem"/> sizes
    /// the cell buffer.
    /// </remarks>
    public struct LockstepNavGrid : IComponentData
    {
        /// <summary>World X and Z of the outer corner of cell (0, 0).</summary>
        public FixedVector2 origin;
        public FixedPoint cellSize;
        /// <summary>Number of cells along world X.</summary>
        public int width;
        /// <summary>Number of cells along world Z.</summary>
        public int height;
        /// <summary>
        /// Radius of the agents: an obstacle blocks every cell whose centre is closer to it than this, so paths keep the
        /// centres of agents that far from obstacles.
        /// </summary>
        public FixedPoint agentRadius;
        /// <summary>
        /// The steepest ground agents walk on, as rise over run (1 is 45 degrees): a cell two neighbouring
        /// <see cref="LockstepNavHeight"/> corners of which differ by more than this times the cell size is blocked. Zero
        /// leaves every slope walkable.
        /// </summary>
        public FixedPoint maxSlope;
        /// <summary>Changes whenever a cell does. Agents check their paths again when it changes.</summary>
        public uint version;

        /// <summary>The cells of the grid without its navigation data: the cell math lives there.</summary>
        public FixedGrid Layout => new FixedGrid(origin, cellSize, width, height);

        public int CellCount => Layout.CellCount;

        /// <summary>Number of cell corners: the length of the <see cref="LockstepNavHeight"/> buffer.</summary>
        public int CornerCount => (width + 1) * (height + 1);

        public bool IsValid => Layout.IsValid;

        public bool Contains(int2 cell) => Layout.Contains(cell);

        public int GetIndex(int2 cell) => Layout.GetIndex(cell);

        public int2 GetCell(int index) => Layout.GetCell(index);

        /// <summary>Index of corner (x, y) in the <see cref="LockstepNavHeight"/> buffer; corner (x, y) is where cell (x, y) starts.</summary>
        public int GetCornerIndex(int2 corner) => corner.y * (width + 1) + corner.x;

        /// <summary>The cell that holds a world position (X and Z); it may lie outside the grid.</summary>
        public int2 WorldToCell(FixedVector2 position) => Layout.WorldToCell(position);

        /// <summary>The cell under a world position; Y is ignored.</summary>
        public int2 WorldToCell(FixedVector3 position) => WorldToCell(position.Xz);

        /// <summary>World X and Z of the centre of a cell.</summary>
        public FixedVector2 GetCellCenter(int2 cell) => Layout.GetCellCenter(cell);

        /// <summary>The nearest cell inside the grid.</summary>
        public int2 ClampToGrid(int2 cell) => Layout.ClampToGrid(cell);
    }
}
