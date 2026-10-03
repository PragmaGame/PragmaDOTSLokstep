using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Keeps the cells of the <see cref="LockstepNavGrid"/> in step with the obstacles: stamps new ones, moves the ones
    /// whose transform or rectangle changed, releases the destroyed ones, and changes the grid version when any cell did.
    /// </summary>
    /// <remarks>
    /// Cells count the obstacles over them, so stamping and releasing commute and the cells do not depend on the order
    /// obstacles are visited in. The rectangle an obstacle stamped is kept in a cleanup component,
    /// <see cref="LockstepNavObstacleFootprint"/>, which is how a destroyed obstacle still knows which cells to release.
    /// A new grid, or one whose size changed, is stamped again from scratch.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepNavSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct LockstepNavObstacleSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepNavGrid>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gridEntity = SystemAPI.GetSingletonEntity<LockstepNavGrid>();
            var grid = SystemAPI.GetComponent<LockstepNavGrid>(gridEntity);
            if (!grid.IsValid)
            {
                return;
            }

            var isRebuilt = false;
            if (!SystemAPI.HasBuffer<LockstepNavCell>(gridEntity))
            {
                state.EntityManager.AddBuffer<LockstepNavCell>(gridEntity);
                isRebuilt = true;
            }
            var buffer = SystemAPI.GetBuffer<LockstepNavCell>(gridEntity);
            if (buffer.Length != grid.CellCount)
            {
                buffer.ResizeUninitialized(grid.CellCount);
                isRebuilt = true;
            }
            var cells = buffer.AsNativeArray();
            if (isRebuilt)
            {
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = default;
                }
            }

            var transforms = SystemAPI.GetComponentLookup<LockstepTransform>(true);
            var commands = new EntityCommandBuffer(Allocator.Temp);
            var isChanged = isRebuilt;

            // Destroyed obstacles, and entities that stopped being obstacles, release their cells.
            foreach (var (footprint, entity) in SystemAPI.Query<RefRO<LockstepNavObstacleFootprint>>().WithNone<LockstepNavObstacle>().WithEntityAccess())
            {
                if (!isRebuilt)
                {
                    LockstepNavigation.Stamp(grid, cells, footprint.ValueRO, -1);
                }
                commands.RemoveComponent<LockstepNavObstacleFootprint>(entity);
                isChanged = true;
            }

            // Stamped obstacles follow their transform and their rectangle.
            foreach (var (obstacle, footprint, entity) in SystemAPI.Query<RefRO<LockstepNavObstacle>, RefRW<LockstepNavObstacleFootprint>>().WithEntityAccess())
            {
                var current = GetFootprint(obstacle.ValueRO, entity, transforms);
                if (!isRebuilt && IsSame(current, footprint.ValueRO))
                {
                    continue;
                }
                if (!isRebuilt)
                {
                    LockstepNavigation.Stamp(grid, cells, footprint.ValueRO, -1);
                }
                LockstepNavigation.Stamp(grid, cells, current, 1);
                footprint.ValueRW = current;
                isChanged = true;
            }

            foreach (var (obstacle, entity) in SystemAPI.Query<RefRO<LockstepNavObstacle>>().WithNone<LockstepNavObstacleFootprint>().WithEntityAccess())
            {
                var current = GetFootprint(obstacle.ValueRO, entity, transforms);
                LockstepNavigation.Stamp(grid, cells, current, 1);
                commands.AddComponent(entity, current);
                isChanged = true;
            }

            commands.Playback(state.EntityManager);
            if (isChanged)
            {
                grid.version++;
                SystemAPI.SetComponent(gridEntity, grid);
            }
        }

        private static LockstepNavObstacleFootprint GetFootprint(in LockstepNavObstacle obstacle, Entity entity, ComponentLookup<LockstepTransform> transforms)
        {
            return transforms.TryGetComponent(entity, out var transform)
                ? LockstepNavObstacleFootprint.Create(obstacle, transform)
                : LockstepNavObstacleFootprint.Create(obstacle);
        }

        private static bool IsSame(in LockstepNavObstacleFootprint a, in LockstepNavObstacleFootprint b)
        {
            return a.center == b.center && a.right == b.right && a.halfSize == b.halfSize;
        }
    }
}
