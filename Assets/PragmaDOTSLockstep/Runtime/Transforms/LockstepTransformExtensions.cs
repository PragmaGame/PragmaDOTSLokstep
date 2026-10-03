using Unity.Mathematics;
using Unity.Transforms;

namespace Pragma.Lockstep
{
    public static class LockstepTransformExtensions
    {
        /// <summary>
        /// Interpolated float transform for rendering: between the start-of-tick capture and the current value when the
        /// capture belongs to <paramref name="lastSimulatedTick"/>, otherwise the current value.
        /// </summary>
        public static LocalTransform Interpolate(in LockstepTransform current, in LockstepTransformPrevious previous, int lastSimulatedTick, float alpha)
        {
            if (!previous.IsCapturedAt(lastSimulatedTick))
            {
                return current.ToLocalTransform();
            }
            var position = math.lerp((float3)previous.position, (float3)current.position, alpha);
            var rotation = math.slerp((quaternion)previous.rotation, (quaternion)current.rotation, alpha);
            var scale = math.lerp((float)previous.scale, (float)current.scale, alpha);
            return LocalTransform.FromPositionRotationScale(position, rotation, scale);
        }
    }
}
