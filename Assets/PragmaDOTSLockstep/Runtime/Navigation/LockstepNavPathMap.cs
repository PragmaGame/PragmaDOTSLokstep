using Unity.Collections;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// The grid as one agent plans on it (<see cref="LockstepPathfinder"/>): the cells its body fits in (its clearance,
    /// <see cref="LockstepNavigation.GetClearance"/>) and, optionally, what crowds of standing agents cost a path through
    /// them.
    /// </summary>
    /// <remarks>
    /// A crowded cell costs <see cref="crowdCost"/> more to enter for every agent standing in it, so a path goes around a
    /// crowd when the way around is shorter than that, and straight lines are only drawn through crowds the path itself
    /// crosses. Leave <see cref="crowd"/> empty for a path that ignores agents. Inside a job, mark the map field
    /// <c>[ReadOnly]</c>, and create every array of it, if empty.
    /// </remarks>
    public struct LockstepNavPathMap
    {
        public LockstepNavGrid grid;
        public NativeArray<LockstepNavCell> cells;
        /// <summary>The clearance a cell needs for the agent's body (<see cref="LockstepNavigation.GetClearance"/>); 1 for any walkable cell.</summary>
        public int clearance;
        /// <summary>
        /// Agents standing in reach of each cell (their body grown by the grid's agent radius covers its centre), as many as
        /// the grid has cells; empty for a path that ignores agents.
        /// </summary>
        public NativeArray<byte> crowd;
        /// <summary>What every standing agent adds to the cost of entering its cell, in the units of the pathfinder (10 a cell).</summary>
        public int crowdCost;

        public LockstepNavPathMap(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int clearance = 1)
        {
            this.grid = grid;
            this.cells = cells;
            this.clearance = math.max(clearance, 1);
            crowd = default;
            crowdCost = 0;
        }

        public bool IsValid => grid.IsValid && cells.Length == grid.CellCount;

        public bool HasCrowd => crowdCost > 0 && crowd.Length == grid.CellCount;

        /// <summary>The agent's body fits on the cell (<see cref="LockstepNavigation.IsPassable(in LockstepNavGrid, NativeArray{LockstepNavCell}, int2, int)"/>).</summary>
        public bool IsPassable(int2 cell) => LockstepNavigation.IsPassable(grid, cells, cell, clearance);

        /// <summary>What the standing agents in a cell add to the cost of entering it.</summary>
        public int GetCrowdCost(int index) => HasCrowd ? crowd[index] * crowdCost : 0;
    }
}
