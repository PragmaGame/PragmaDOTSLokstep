using Pragma.Lockstep.Mathematics;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes the GameObject transform into a deterministic <see cref="LockstepTransform"/>. The float values are converted to
    /// fixed point once, at bake time, so every client loads the same raw values.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/FP Transform")]
    public sealed class LockstepTransformAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Adds LockstepTransformPrevious, so views of this entity are interpolated between ticks.")]
        private bool _interpolate = true;

        private sealed class Baker : Baker<LockstepTransformAuthoring>
        {
            public override void Bake(LockstepTransformAuthoring authoring)
            {
                // Dynamic keeps LocalTransform on the baked prefab, which the presentation copy needs for rendering.
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                var source = authoring.transform;
                var transform = new LockstepTransform
                {
                    position = (FixedVector3)source.position,
                    rotation = FixedMath.NormalizeSafe((FixedQuaternion)source.rotation),
                    scale = (FixedPoint)source.lossyScale.x,
                };
                AddComponent(entity, transform);
                if (authoring._interpolate)
                {
                    AddComponent(entity, new LockstepTransformPrevious
                    {
                        position = transform.position,
                        rotation = transform.rotation,
                        scale = transform.scale,
                    });
                }
            }
        }
    }
}
