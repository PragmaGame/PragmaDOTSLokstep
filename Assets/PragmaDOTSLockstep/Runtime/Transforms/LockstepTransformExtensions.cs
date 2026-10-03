using Unity.Entities;
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

        /// <summary>
        /// Reads the <see cref="LockstepTransform"/> of a simulation entity and returns the transform to draw, interpolated
        /// with its <see cref="LockstepTransformPrevious"/> (see <see cref="Interpolate"/>) when it has one.
        /// </summary>
        /// <param name="shownSinceTick">
        /// The last simulated tick when the view of the entity appeared; until the next tick the current value is
        /// returned. An entity copied from a live one during a tick carries that entity's capture and would slide in from
        /// there.
        /// </param>
        /// <returns>False when the entity does not exist or has no <see cref="LockstepTransform"/>.</returns>
        public static bool TryGetInterpolated(this EntityManager simulationManager, Entity entity, int lastSimulatedTick, float alpha, int shownSinceTick, out LocalTransform transform)
        {
            if (!simulationManager.HasComponent<LockstepTransform>(entity))
            {
                transform = default;
                return false;
            }
            var current = simulationManager.GetComponentData<LockstepTransform>(entity);
            transform = lastSimulatedTick > shownSinceTick && simulationManager.HasComponent<LockstepTransformPrevious>(entity)
                ? Interpolate(current, simulationManager.GetComponentData<LockstepTransformPrevious>(entity), lastSimulatedTick, alpha)
                : current.ToLocalTransform();
            return true;
        }
    }
}
