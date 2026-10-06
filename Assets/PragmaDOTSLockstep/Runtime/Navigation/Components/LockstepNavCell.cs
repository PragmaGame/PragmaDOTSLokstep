using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>One cell of the <see cref="LockstepNavGrid"/>, in a buffer on the grid entity: row by row along X.</summary>
    [InternalBufferCapacity(0)]
    public struct LockstepNavCell : IBufferElementData
    {
        /// <summary>Number of obstacles that cover the cell. Agents only enter cells that no obstacle covers.</summary>
        public ushort blockers;

        /// <summary>
        /// How far the cell is from the nearest blocked cell or the edge of the grid, in rings of cells around it: 0 for
        /// a blocked cell, 1 for one next to a blocked cell or on the edge, at most 255. Agents larger than the grid's
        /// <see cref="LockstepNavGrid.agentRadius"/> enter only cells with as much of it as their body needs
        /// (<see cref="LockstepNavigation.GetClearance"/>). <see cref="LockstepNavObstacleSystem"/> keeps it up to date
        /// (<see cref="LockstepNavigation.UpdateClearance"/>).
        /// </summary>
        public byte clearance;

        public bool IsWalkable => blockers == 0;
    }
}
