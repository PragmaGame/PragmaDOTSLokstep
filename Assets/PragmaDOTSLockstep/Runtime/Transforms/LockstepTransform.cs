using Pragma.Lockstep.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Pragma.Lockstep
{
    /// <summary>Deterministic position, rotation and uniform scale of a simulation entity.</summary>
    /// <remarks>
    /// The simulation has no hierarchy: this is the world transform. The presentation converts it to a
    /// <c>LocalTransform</c> on the view entity, interpolated with <see cref="LockstepTransformPrevious"/>.
    /// </remarks>
    public struct LockstepTransform : IComponentData
    {
        public FixedVector3 position;
        public FixedQuaternion rotation;
        public FixedPoint scale;

        public static LockstepTransform Identity => new LockstepTransform { rotation = FixedQuaternion.Identity, scale = FixedPoint.One };

        public static LockstepTransform FromPosition(FixedVector3 position) => new LockstepTransform { position = position, rotation = FixedQuaternion.Identity, scale = FixedPoint.One };

        public static LockstepTransform FromPositionRotation(FixedVector3 position, FixedQuaternion rotation) => new LockstepTransform { position = position, rotation = rotation, scale = FixedPoint.One };

        public FixedVector3 Forward => FixedMath.Rotate(rotation, FixedVector3.Forward);
        public FixedVector3 Up => FixedMath.Rotate(rotation, FixedVector3.Up);
        public FixedVector3 Right => FixedMath.Rotate(rotation, FixedVector3.Right);

        public FixedVector3 TransformPoint(FixedVector3 point) => position + FixedMath.Rotate(rotation, point * scale);

        public FixedVector3 InverseTransformPoint(FixedVector3 point)
        {
            var local = FixedMath.Rotate(FixedMath.Conjugate(rotation), point - position);
            return scale.rawValue == 0 ? local : local / scale;
        }

        public FixedVector3 TransformDirection(FixedVector3 direction) => FixedMath.Rotate(rotation, direction);

        /// <summary>Float transform for rendering. Never feed the result back into the simulation.</summary>
        public LocalTransform ToLocalTransform() => LocalTransform.FromPositionRotationScale((float3)position, (quaternion)rotation, (float)scale);
    }
}
