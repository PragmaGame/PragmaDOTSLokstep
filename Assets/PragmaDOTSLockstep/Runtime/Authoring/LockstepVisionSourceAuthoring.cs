using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Vision;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes a <see cref="LockstepVisionSource"/>: entities of this prefab let a player slot see around themselves. The
    /// slot is set by gameplay when the entity gets its owner; a radius that research or abilities change is kept current
    /// by gameplay too (from a stat), and the baked one is where it starts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LockstepTransformAuthoring))]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Vision Source")]
    public sealed class LockstepVisionSourceAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("How far the entity sees, in world units.")]
        private float _radius = 20f;

        private sealed class Baker : Baker<LockstepVisionSourceAuthoring>
        {
            public override void Bake(LockstepVisionSourceAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new LockstepVisionSource { radius = (FixedPoint)authoring._radius, slot = -1 });
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.85f, 0.3f, 0.8f);
            var center = transform.position;
            const int segments = 48;
            var previous = center + new Vector3(_radius, 0f, 0f);
            for (var i = 1; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var next = center + new Vector3(Mathf.Cos(angle) * _radius, 0f, Mathf.Sin(angle) * _radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
