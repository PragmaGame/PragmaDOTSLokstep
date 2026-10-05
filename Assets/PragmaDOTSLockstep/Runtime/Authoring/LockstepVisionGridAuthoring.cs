using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Vision;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes the <see cref="LockstepVisionGrid"/> of a map: a rectangle of square cells on the XZ plane, centred on this
    /// GameObject, that holds what every player slot sees. The simulation copies it before tick 0 like any lockstep scene
    /// entity.
    /// </summary>
    /// <remarks>
    /// Put it into the subscene of the map, usually over the same area as the navigation grid. Vision is stamped in whole
    /// cells, so the cell size is how fine the edge of the fog is: a few world units are plenty. The planes are sized at
    /// run time, one per slot of the session.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LockstepSceneEntityAuthoring))]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Vision Grid")]
    public sealed class LockstepVisionGridAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("World size of the grid along X and Z, centred on this GameObject. Outside it nothing is visible.")]
        private Vector2 _size = new Vector2(100f, 100f);

        [SerializeField, Min(0.05f), Tooltip("Side of a cell in world units: how fine the edge of the fog is. Smaller cells cost more per tick.")]
        private float _cellSize = 2f;

        private sealed class Baker : Baker<LockstepVisionGridAuthoring>
        {
            public override void Bake(LockstepVisionGridAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, authoring.CreateGrid(GetComponent<Transform>().position));
                // Sized by LockstepVisionSystem on the first tick: an empty buffer keeps the subscene small.
                AddBuffer<LockstepVisionCell>(entity);
            }
        }

        private LockstepVisionGrid CreateGrid(Vector3 center)
        {
            var cellSize = (FixedPoint)_cellSize;
            var width = Mathf.Max(1, Mathf.CeilToInt(_size.x / _cellSize));
            var height = Mathf.Max(1, Mathf.CeilToInt(_size.y / _cellSize));
            return new LockstepVisionGrid
            {
                origin = new FixedVector2((FixedPoint)center.x - cellSize * width / 2, (FixedPoint)center.z - cellSize * height / 2),
                cellSize = cellSize,
                width = width,
                height = height,
            };
        }

        private void OnDrawGizmos()
        {
            var grid = CreateGrid(transform.position);
            var size = new Vector3((float)(grid.cellSize * grid.width), 0f, (float)(grid.cellSize * grid.height));
            var corner = new Vector3((float)grid.origin.x, transform.position.y, (float)grid.origin.y);
            Gizmos.color = new Color(0.9f, 0.85f, 0.3f, 0.8f);
            Gizmos.DrawWireCube(corner + size / 2, size);
        }
    }
}
