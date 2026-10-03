using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>One cell of the <see cref="LockstepNavGrid"/>, in a buffer on the grid entity: row by row along X.</summary>
    [InternalBufferCapacity(0)]
    public struct LockstepNavCell : IBufferElementData
    {
        /// <summary>Number of obstacles that cover the cell. Agents only enter cells that no obstacle covers.</summary>
        public ushort blockers;

        public bool IsWalkable => blockers == 0;
    }
}
