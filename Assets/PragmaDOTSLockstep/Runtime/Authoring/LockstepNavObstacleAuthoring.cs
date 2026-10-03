using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes a <see cref="LockstepNavObstacle"/>: a box, like a BoxCollider's, whose X and Z block the navigation grid.
    /// </summary>
    /// <remarks>
    /// <para>With a <see cref="LockstepTransformAuthoring"/> on the same GameObject the box moves with the entity: use it on
    /// prefabs, such as buildings spawned from the registry. Without one the obstacle is static map content: its pose is
    /// baked here, non-uniform scale included. Static obstacles placed in a subscene need a
    /// <see cref="LockstepSceneEntityAuthoring"/> as well.</para>
    /// <para>Scene entities are copied whole into the simulation, so keep renderers off the obstacle's GameObject: put the
    /// visible model on a child, or show the obstacle with a view.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Nav Obstacle")]
    public sealed class LockstepNavObstacleAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Centre of the box in local space. Y is ignored.")]
        private Vector3 _center;

        [SerializeField, Tooltip("Size of the box in local space. Only X and Z block the grid.")]
        private Vector3 _size = Vector3.one;

        private sealed class Baker : Baker<LockstepNavObstacleAuthoring>
        {
            public override void Bake(LockstepNavObstacleAuthoring authoring)
            {
                var isMoving = GetComponent<LockstepTransformAuthoring>() != null;
                authoring.GetSimulationData(GetComponent<Transform>(), isMoving, out var obstacle, out var transform);
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, obstacle);
                if (!isMoving)
                {
                    AddComponent(entity, transform);
                }
            }
        }

        /// <summary>The rectangle the simulation will stamp, for previews.</summary>
        internal LockstepNavObstacleFootprint GetFootprint()
        {
            GetSimulationData(transform, GetComponent<LockstepTransformAuthoring>() != null, out var obstacle, out var simulationTransform);
            return LockstepNavObstacleFootprint.Create(obstacle, simulationTransform);
        }

        private void GetSimulationData(Transform source, bool isMoving, out LockstepNavObstacle obstacle, out LockstepTransform simulationTransform)
        {
            var position = (FixedVector3)source.position;
            var rotation = FixedMath.NormalizeSafe((FixedQuaternion)source.rotation);
            if (isMoving)
            {
                // The values LockstepTransformAuthoring bakes; the box stays local and scales with the entity.
                simulationTransform = new LockstepTransform { position = position, rotation = rotation, scale = (FixedPoint)source.lossyScale.x };
                obstacle = new LockstepNavObstacle
                {
                    center = new FixedVector2((FixedPoint)_center.x, (FixedPoint)_center.z),
                    size = new FixedVector2((FixedPoint)_size.x, (FixedPoint)_size.z),
                };
                return;
            }

            var scale = source.lossyScale;
            simulationTransform = LockstepTransform.FromPositionRotation(position, rotation);
            obstacle = new LockstepNavObstacle
            {
                center = new FixedVector2((FixedPoint)(_center.x * scale.x), (FixedPoint)(_center.z * scale.z)),
                size = new FixedVector2((FixedPoint)Mathf.Abs(_size.x * scale.x), (FixedPoint)Mathf.Abs(_size.z * scale.z)),
            };
        }

        [ContextMenu("Fit To Renderers")]
        private void FitToRenderers()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }
            var toLocal = transform.worldToLocalMatrix;
            var bounds = new Bounds(toLocal.MultiplyPoint3x4(renderers[0].bounds.center), Vector3.zero);
            foreach (var item in renderers)
            {
                var local = item.localBounds;
                var toThis = toLocal * item.transform.localToWorldMatrix;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = local.center + Vector3.Scale(local.extents, new Vector3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1) * 2f - Vector3.one);
                    bounds.Encapsulate(toThis.MultiplyPoint3x4(point));
                }
            }
            _center = bounds.center;
            _size = bounds.size;
        }

        private void Reset()
        {
            FitToRenderers();
        }

        private void OnDrawGizmosSelected()
        {
            var footprint = GetFootprint();
            var center = new Vector3((float)footprint.center.x, transform.position.y, (float)footprint.center.y);
            var right = new Vector3((float)footprint.right.x, 0f, (float)footprint.right.y) * (float)footprint.halfSize.x;
            var forward = new Vector3((float)footprint.Forward.x, 0f, (float)footprint.Forward.y) * (float)footprint.halfSize.y;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 1f);
            Gizmos.DrawLine(center - right - forward, center + right - forward);
            Gizmos.DrawLine(center + right - forward, center + right + forward);
            Gizmos.DrawLine(center + right + forward, center - right + forward);
            Gizmos.DrawLine(center - right + forward, center - right - forward);
        }
    }
}
