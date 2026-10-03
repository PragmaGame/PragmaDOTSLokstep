using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Walks agents along their waypoints at their speed, turning them to face where they walk, and marks them arrived at
    /// the end of the path. Distance left over at a corner carries on along the next segment, so agents keep their speed
    /// through corners.
    /// </summary>
    [UpdateInGroup(typeof(LockstepNavSystemGroup))]
    [UpdateAfter(typeof(LockstepNavPathSystem))]
    [BurstCompile]
    public partial struct LockstepNavMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new MoveJob { deltaTime = SystemAPI.GetSingleton<LockstepTime>().deltaTime }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct MoveJob : IJobEntity
        {
            // Shorter offsets give a coarse direction in fixed point, so the last bit of a segment never turns the agent.
            private const long MIN_TURN_DISTANCE_RAW = FixedPoint.ONE_RAW / 64;

            public FixedPoint deltaTime;

            private void Execute(ref LockstepTransform transform, ref LockstepNavAgent agent, in DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                if (agent.status != LockstepNavStatus.Moving)
                {
                    return;
                }

                var position = transform.position;
                var budget = agent.speed * deltaTime;
                var heading = FixedVector3.Zero;
                while (agent.waypointIndex < waypoints.Length)
                {
                    var target = waypoints[agent.waypointIndex].position;
                    var offset = target - position;
                    var distance = FixedMath.Length(offset);
                    if (agent.waypointIndex == waypoints.Length - 1 && distance <= agent.stoppingDistance)
                    {
                        agent.waypointIndex = waypoints.Length;
                        break;
                    }
                    if (distance.rawValue > MIN_TURN_DISTANCE_RAW)
                    {
                        heading = offset;
                    }
                    if (budget < distance)
                    {
                        position = FixedMath.MoveTowards(position, target, budget);
                        break;
                    }
                    position = target;
                    budget -= distance;
                    agent.waypointIndex++;
                }

                transform.position = position;
                if (agent.waypointIndex >= waypoints.Length)
                {
                    agent.status = LockstepNavStatus.Arrived;
                }
                if (heading.x.rawValue == 0 && heading.z.rawValue == 0)
                {
                    return;
                }
                var facing = FixedQuaternion.LookRotation(new FixedVector3(heading.x, FixedPoint.Zero, heading.z), FixedVector3.Up);
                transform.rotation = agent.angularSpeed.rawValue > 0
                    ? FixedMath.RotateTowards(transform.rotation, facing, agent.angularSpeed * deltaTime)
                    : facing;
            }
        }
    }
}
