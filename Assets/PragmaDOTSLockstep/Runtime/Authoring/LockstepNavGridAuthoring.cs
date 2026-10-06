using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes the <see cref="LockstepNavGrid"/> of a map: a rectangle of square cells on the XZ plane, centred on this
    /// GameObject, that the simulation copies before tick 0 like any lockstep scene entity, and the ground agents walk on.
    /// </summary>
    /// <remarks>
    /// Put it into the subscene of the map. Obstacles (<see cref="LockstepNavObstacleAuthoring"/>) block cells at run time,
    /// in the simulation; selecting this GameObject previews the cells the obstacles of the open scenes and the steep ground
    /// block.
    /// <para>The ground is a <see cref="TerrainData"/> sampled at every cell corner into <see cref="LockstepNavHeight"/>:
    /// place its terrain so that it covers the grid exactly (its corner at the grid's corner, its size the grid's size) at
    /// world Y <see cref="_terrainHeight"/>. Entities Graphics does not draw terrains, so the terrain itself usually stays in
    /// the main scene; baking reads only its data, and editing the data bakes the grid again.</para>
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

        [SerializeField, Min(0f), Tooltip("Radius of the common agents: paths keep their centres this far from obstacles. Larger bodies (vehicles) keep to cells with as much more room as they are larger.")]
        private float _agentRadius = 0.5f;

        [SerializeField, Tooltip("Ground agents walk on: a terrain covering the grid exactly. Empty: flat ground, agents keep their Y.")]
        private TerrainData _terrain;

        [SerializeField, Tooltip("World Y of the terrain object.")]
        private float _terrainHeight;

        [SerializeField, Range(0f, 89f), Tooltip("The steepest ground agents walk on, in degrees: steeper cells are blocked. Zero: every slope is walkable.")]
        private float _maxSlope = 35f;

        private sealed class Baker : Baker<LockstepNavGridAuthoring>
        {
            public override void Bake(LockstepNavGridAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var grid = authoring.CreateGrid(GetComponent<Transform>().position);
                AddComponent(entity, grid);
                // Sized by LockstepNavObstacleSystem on the first tick: an empty buffer keeps the subscene small.
                AddBuffer<LockstepNavCell>(entity);

                if (authoring._terrain == null)
                {
                    return;
                }
                DependsOn(authoring._terrain);
                authoring.WarnIfTerrainMismatches(grid);
                var heights = AddBuffer<LockstepNavHeight>(entity);
                heights.ResizeUninitialized(grid.CornerCount);
                authoring.SampleTerrain(grid, heights.AsNativeArray());
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
                maxSlope = _terrain != null && _maxSlope > 0f ? (FixedPoint)Mathf.Tan(_maxSlope * Mathf.Deg2Rad) : FixedPoint.Zero,
            };
        }

        // World Y of the terrain at every corner of the grid: the terrain is stretched over the grid.
        private void SampleTerrain(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights)
        {
            for (var z = 0; z <= grid.height; z++)
            {
                for (var x = 0; x <= grid.width; x++)
                {
                    var height = _terrainHeight + _terrain.GetInterpolatedHeight((float)x / grid.width, (float)z / grid.height);
                    heights[grid.GetCornerIndex(new int2(x, z))] = new LockstepNavHeight { value = (FixedPoint)height };
                }
            }
        }

        private void WarnIfTerrainMismatches(in LockstepNavGrid grid)
        {
            var width = (float)(grid.cellSize * grid.width);
            var length = (float)(grid.cellSize * grid.height);
            if (Mathf.Abs(_terrain.size.x - width) > 0.01f || Mathf.Abs(_terrain.size.z - length) > 0.01f)
            {
                Debug.LogWarning($"{nameof(LockstepNavGridAuthoring)}: the terrain of '{name}' is {_terrain.size.x} x {_terrain.size.z} but the grid " +
                                 $"is {width} x {length}: its heights are stretched over the grid.", this);
            }
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
            using (var heights = new NativeArray<LockstepNavHeight>(_terrain != null ? grid.CornerCount : 0, Allocator.Temp))
            {
                if (_terrain != null)
                {
                    SampleTerrain(grid, heights);
                }
                foreach (var obstacle in FindObjectsByType<LockstepNavObstacleAuthoring>(FindObjectsInactive.Exclude))
                {
                    LockstepNavigation.Stamp(grid, cells, obstacle.GetFootprint(), 1);
                }

                var hasHeights = LockstepNavigation.HasHeights(grid, heights);
                var cellSize = (float)grid.cellSize;
                var obstacleColor = new Color(1f, 0.3f, 0.2f, 0.4f);
                var steepColor = new Color(1f, 0.75f, 0.1f, 0.4f);
                for (var i = 0; i < cells.Length; i++)
                {
                    var cell = grid.GetCell(i);
                    var isSteep = LockstepNavigation.IsSteep(grid, heights, cell);
                    if (cells[i].IsWalkable && !isSteep)
                    {
                        continue;
                    }
                    var center = grid.GetCellCenter(cell);
                    var y = (hasHeights ? (float)LockstepNavigation.GetHeight(grid, heights, center) : transform.position.y) + 0.02f;
                    Gizmos.color = cells[i].IsWalkable ? steepColor : obstacleColor;
                    Gizmos.DrawCube(new Vector3((float)center.x, y, (float)center.y), new Vector3(cellSize, 0.01f, cellSize));
                }
            }
        }
    }
}
