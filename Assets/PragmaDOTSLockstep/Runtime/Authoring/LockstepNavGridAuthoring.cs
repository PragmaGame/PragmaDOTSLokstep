using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes the <see cref="LockstepNavGrid"/> of a map: a rectangle of square cells on the XZ plane, centred on this
    /// GameObject, that the simulation copies before tick 0 like any lockstep scene entity.
    /// </summary>
    /// <remarks>
    /// Put it into the subscene of the map. Obstacles (<see cref="LockstepNavObstacleAuthoring"/>) block cells at run time,
    /// in the simulation; selecting this GameObject previews the cells the obstacles of the open scenes block.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LockstepSceneEntityAuthoring))]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Nav Grid")]
    public sealed class LockstepNavGridAuthoring : MonoBehaviour
    {
        private const int MAX_PREVIEW_CELLS = 1 << 20;

        [SerializeField, Tooltip("World size of the grid along X and Z, centred on this GameObject.")]
        private Vector2 _size = new Vector2(100f, 100f);

        [SerializeField, Min(0.05f), Tooltip("Side of a cell in world units. Smaller cells follow obstacles closer and take longer to search.")]
        private float _cellSize = 0.5f;

        [SerializeField, Min(0f), Tooltip("Radius of the agents: paths keep their centres this far from obstacles.")]
        private float _agentRadius = 0.5f;

        private sealed class Baker : Baker<LockstepNavGridAuthoring>
        {
            public override void Bake(LockstepNavGridAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, authoring.CreateGrid(GetComponent<Transform>().position));
                // Sized by LockstepNavObstacleSystem on the first tick: an empty buffer keeps the subscene small.
                AddBuffer<LockstepNavCell>(entity);
            }
        }

        private LockstepNavGrid CreateGrid(Vector3 center)
        {
            var cellSize = (FixedPoint)_cellSize;
            var width = Mathf.Max(1, Mathf.CeilToInt(_size.x / _cellSize));
            var height = Mathf.Max(1, Mathf.CeilToInt(_size.y / _cellSize));
            return new LockstepNavGrid
            {
                origin = new FixedVector2((FixedPoint)center.x - cellSize * width / 2, (FixedPoint)center.z - cellSize * height / 2),
                cellSize = cellSize,
                width = width,
                height = height,
                agentRadius = (FixedPoint)_agentRadius,
            };
        }

        private void OnDrawGizmos()
        {
            var grid = CreateGrid(transform.position);
            var size = new Vector3((float)(grid.cellSize * grid.width), 0f, (float)(grid.cellSize * grid.height));
            var corner = new Vector3((float)grid.origin.x, transform.position.y, (float)grid.origin.y);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(corner + size / 2, size);
        }

        private void OnDrawGizmosSelected()
        {
            var grid = CreateGrid(transform.position);
            if (grid.CellCount > MAX_PREVIEW_CELLS)
            {
                return;
            }

            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            {
                foreach (var obstacle in FindObjectsByType<LockstepNavObstacleAuthoring>(FindObjectsInactive.Exclude))
                {
                    LockstepNavigation.Stamp(grid, cells, obstacle.GetFootprint(), 1);
                }

                var cellSize = (float)grid.cellSize;
                var y = transform.position.y + 0.02f;
                Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
                for (var i = 0; i < cells.Length; i++)
                {
                    if (cells[i].IsWalkable)
                    {
                        continue;
                    }
                    var center = grid.GetCellCenter(grid.GetCell(i));
                    Gizmos.DrawCube(new Vector3((float)center.x, y, (float)center.y), new Vector3(cellSize, 0.01f, cellSize));
                }
            }
        }
    }
}
