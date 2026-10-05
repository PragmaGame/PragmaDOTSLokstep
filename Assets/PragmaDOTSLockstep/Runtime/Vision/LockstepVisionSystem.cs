using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Pragma.Lockstep.Vision
{
    /// <summary>
    /// Fills the planes of the <see cref="LockstepVisionGrid"/> again on every tick: clears them and stamps every
    /// <see cref="LockstepVisionSource"/> at its <see cref="LockstepTransform"/>.
    /// </summary>
    /// <remarks>
    /// Runs last in the tick, after everything moved, so the planes show the state the tick ended with: what the
    /// presentation draws after it, and what the commands of the next tick are checked against. Stamping only ever marks
    /// cells, so the result does not depend on the order sources are visited in. The buffer is sized here, one plane per
    /// slot of the session, so a baked grid stays small.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(LockstepEndSimulationEntityCommandBufferSystem))]
    [BurstCompile]
    public partial struct LockstepVisionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepVisionGrid>();
            state.RequireForUpdate<LockstepSessionInfo>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Sources are read on the main thread: the jobs that moved them this tick must be done.
            state.CompleteDependency();
            var info = SystemAPI.GetSingleton<LockstepSessionInfo>();
            var gridEntity = SystemAPI.GetSingletonEntity<LockstepVisionGrid>();
            var grid = SystemAPI.GetComponent<LockstepVisionGrid>(gridEntity);
            if (!grid.IsValid)
            {
                return;
            }

            var slotCount = math.max(0, info.maxPlayers);
            if (grid.slotCount != slotCount)
            {
                grid.slotCount = slotCount;
                SystemAPI.SetComponent(gridEntity, grid);
            }
            if (!SystemAPI.HasBuffer<LockstepVisionCell>(gridEntity))
            {
                state.EntityManager.AddBuffer<LockstepVisionCell>(gridEntity);
            }

            var buffer = SystemAPI.GetBuffer<LockstepVisionCell>(gridEntity);
            var length = grid.CellCount * slotCount;
            if (buffer.Length != length)
            {
                buffer.ResizeUninitialized(length);
            }

            var cells = buffer.AsNativeArray();
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i] = default;
            }
            foreach (var (source, transform) in SystemAPI.Query<RefRO<LockstepVisionSource>, RefRO<LockstepTransform>>())
            {
                LockstepVision.Stamp(grid, cells, source.ValueRO.slot, transform.ValueRO.position.Xz, source.ValueRO.radius);
            }
        }
    }
}
