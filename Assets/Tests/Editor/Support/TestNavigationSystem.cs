using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// Drives agents through a scripted match: sends them to their targets on tick 2, drops a wall across their way on tick
    /// 30, removes it on tick 75 and sends them back on tick 90.
    /// </summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [UpdateBefore(typeof(LockstepNavSystemGroup))]
    public partial struct TestNavigationSystem : ISystem
    {
        public const int SEND_TICK = 2;
        public const int WALL_TICK = 30;
        public const int CLEAR_TICK = 75;
        public const int RETURN_TICK = 90;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var tick = SystemAPI.GetSingleton<LockstepTime>().tick;
            if (tick == SEND_TICK || tick == RETURN_TICK)
            {
                foreach (var (agent, target) in SystemAPI.Query<RefRW<LockstepNavAgent>, RefRO<TestNavigationTarget>>())
                {
                    var isThere = tick == SEND_TICK;
                    agent.ValueRW.SetDestination(isThere ? target.ValueRO.there : target.ValueRO.back, isThere ? target.ValueRO.thereGoal : target.ValueRO.backGoal);
                }
            }

            if (tick == WALL_TICK)
            {
                var wall = state.EntityManager.CreateEntity(typeof(LockstepNavObstacle), typeof(LockstepTransform), typeof(TestSpawnedTag));
                state.EntityManager.SetComponentData(wall, new LockstepNavObstacle { size = new FixedVector2(1, 10) });
                state.EntityManager.SetComponentData(wall, LockstepTransform.FromPosition(new FixedVector3(2, 0, 0)));
            }

            if (tick == CLEAR_TICK)
            {
                state.EntityManager.DestroyEntity(SystemAPI.QueryBuilder().WithAll<LockstepNavObstacle, TestSpawnedTag>().Build());
            }
        }
    }
}
