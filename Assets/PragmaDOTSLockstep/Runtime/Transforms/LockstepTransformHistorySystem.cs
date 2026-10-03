using Unity.Burst;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Copies <see cref="LockstepTransform"/> into <see cref="LockstepTransformPrevious"/> at the start of every tick.</summary>
    /// <remarks>
    /// Entities created during a tick keep a stale capture until the next tick, so the presentation knows not to
    /// interpolate them from wherever their prefab was.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(LockstepFrameApplySystem))]
    [BurstCompile]
    public partial struct LockstepTransformHistorySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new CaptureJob { tickPlusOne = SystemAPI.GetSingleton<LockstepTime>().tick + 1 }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct CaptureJob : IJobEntity
        {
            public int tickPlusOne;

            private void Execute(in LockstepTransform transform, ref LockstepTransformPrevious previous)
            {
                previous.position = transform.position;
                previous.rotation = transform.rotation;
                previous.scale = transform.scale;
                previous.capturedTickPlusOne = tickPlusOne;
            }
        }
    }
}
