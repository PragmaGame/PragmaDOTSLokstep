using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// The world rectangle an obstacle has stamped into the grid. Written by <see cref="LockstepNavObstacleSystem"/>; a
    /// cleanup component, so a destroyed obstacle lingers until its cells are released.
    /// </summary>
    public struct LockstepNavObstacleFootprint : ICleanupComponentData
    {
        public FixedVector2 center;
        /// <summary>Unit direction of the rectangle's local X axis on the XZ plane.</summary>
        public FixedVector2 right;
        public FixedVector2 halfSize;

        /// <summary>Unit direction of the rectangle's local Z axis on the XZ plane.</summary>
        public FixedVector2 Forward => FixedMath.Perpendicular(right);

        /// <summary>The footprint of an obstacle placed by a transform: its yaw, uniform scale and position.</summary>
        public static LockstepNavObstacleFootprint Create(in LockstepNavObstacle obstacle, in LockstepTransform transform)
        {
            // A yaw of a turns +X to (cos a, -sin a) on (X, Z) and +Z to (sin a, cos a), its perpendicular.
            var right = FixedMath.NormalizeSafe(FixedMath.Rotate(transform.rotation, FixedVector3.Right).Xz, FixedVector2.Right);
            var forward = FixedMath.Perpendicular(right);
            var center = obstacle.center * transform.scale;
            return new LockstepNavObstacleFootprint
            {
                center = transform.position.Xz + right * center.x + forward * center.y,
                right = right,
                halfSize = FixedMath.Abs(obstacle.size * transform.scale) / 2,
            };
        }

        /// <summary>The footprint of an obstacle without a transform: its rectangle in world space, axis-aligned.</summary>
        public static LockstepNavObstacleFootprint Create(in LockstepNavObstacle obstacle)
        {
            return new LockstepNavObstacleFootprint
            {
                center = obstacle.center,
                right = FixedVector2.Right,
                halfSize = FixedMath.Abs(obstacle.size) / 2,
            };
        }
    }
}
