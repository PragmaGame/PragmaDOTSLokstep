using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// A rectangle on the XZ plane that agents walk around: while the entity exists, the grid cells under it (grown by the
    /// grid's <see cref="LockstepNavGrid.agentRadius"/>) are blocked.
    /// </summary>
    /// <remarks>
    /// The rectangle is in the space of the entity's <see cref="LockstepTransform"/> (yaw and uniform scale apply), or in
    /// world space when the entity has none. <see cref="LockstepNavObstacleSystem"/> stamps it into the grid, follows the
    /// entity when its transform or rectangle changes and releases the cells when the entity is destroyed or loses this
    /// component.
    /// </remarks>
    public struct LockstepNavObstacle : IComponentData
    {
        /// <summary>Centre of the rectangle: local X and Z.</summary>
        public FixedVector2 center;
        /// <summary>Full size of the rectangle along local X and Z.</summary>
        public FixedVector2 size;
    }
}
