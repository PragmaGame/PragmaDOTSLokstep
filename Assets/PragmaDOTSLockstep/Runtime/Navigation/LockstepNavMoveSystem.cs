using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Walks agents along their waypoints at their speed (<see cref="LockstepNavAgent.WalkSpeed"/>), turning them to face
    /// where they walk, and marks them arrived at the end of the path. Distance left over at a corner carries on along the
    /// next segment, so agents keep their speed through corners. With local avoidance (<see cref="LockstepNavAvoidance"/>),
    /// an agent with a <see cref="LockstepNavVelocity"/> and a radius walks the velocity avoidance picked instead, and
    /// passes a corner when its body covers it.
    /// </summary>
    /// <remarks>
    /// Agents walk on the XZ plane, at their speed over it: the Y of a walking agent is the ground under it
    /// (<see cref="LockstepNavHeight"/>), or stays as it is on a grid without heights. The Y of the waypoints is not
    /// walked to. An avoiding agent never steps onto a cell its body does not fit in: it slides along the wall
    /// (<see cref="LockstepNavigation.TryStep(in LockstepNavGrid, NativeArray{LockstepNavCell}, FixedVector3, FixedVector2, int, out FixedVector3)"/>);
    /// turned aside so far that a wall stands between it and its next waypoint, it plans its path again from where it
    /// stands. Every agent with a <see cref="LockstepNavVelocity"/> records what it walked in
    /// <see cref="LockstepNavVelocity.value"/>.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepNavSystemGroup))]
    [UpdateAfter(typeof(LockstepNavPathSystem))]
    [BurstCompile]
    public partial struct LockstepNavMoveSystem : ISystem
    {
        /// <summary>
        /// Fixed point rounds a step at a velocity a little short of or past the end of the path: an agent this close
        /// to it ends its walk on it.
        /// </summary>
        internal const long ARRIVAL_RAW = FixedPoint.ONE_RAW / 256;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var walker = new Walker { deltaTime = SystemAPI.GetSingleton<LockstepTime>().deltaTime };
            var hasGrid = SystemAPI.TryGetSingleton<LockstepNavGrid>(out var grid) && grid.IsValid;
            walker.grid = grid;
            if (hasGrid &&
                SystemAPI.TryGetSingletonBuffer<LockstepNavHeight>(out var heights, true) &&
                LockstepNavigation.HasHeights(grid, heights.AsNativeArray()))
            {
                walker.heights = heights.AsNativeArray();
                walker.hasHeights = true;
            }
            else
            {
                walker.heights = CollectionHelper.CreateNativeArray<LockstepNavHeight>(0, state.WorldUpdateAllocator);
            }
            if (hasGrid && SystemAPI.TryGetSingletonBuffer<LockstepNavCell>(out var cells, true) && cells.Length == grid.CellCount)
            {
                walker.cells = cells.AsNativeArray();
                walker.hasCells = true;
            }
            else
            {
                walker.cells = CollectionHelper.CreateNativeArray<LockstepNavCell>(0, state.WorldUpdateAllocator);
            }

            state.Dependency = new MoveJob { walker = walker }.ScheduleParallel(state.Dependency);
            state.Dependency = new VelocityMoveJob
            {
                walker = walker,
                isAvoiding = SystemAPI.HasSingleton<LockstepNavAvoidance>(),
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithNone(typeof(LockstepNavVelocity))]
        private partial struct MoveJob : IJobEntity
        {
            public Walker walker;

            private void Execute(ref LockstepTransform transform, ref LockstepNavAgent agent, in DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                walker.WalkPath(ref transform, ref agent, waypoints);
            }
        }

        [BurstCompile]
        private partial struct VelocityMoveJob : IJobEntity
        {
            public Walker walker;
            public bool isAvoiding;

            private void Execute(ref LockstepTransform transform, ref LockstepNavAgent agent, ref LockstepNavVelocity velocity,
                                 in DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                var start = transform.position.Xz;
                if (isAvoiding && agent.radius.rawValue > 0)
                {
                    walker.WalkVelocity(ref transform, ref agent, waypoints, velocity.desired);
                }
                else
                {
                    walker.WalkPath(ref transform, ref agent, waypoints);
                }
                velocity.value = walker.deltaTime.rawValue > 0 ? (transform.position.Xz - start) / walker.deltaTime : FixedVector2.Zero;
            }
        }

        private struct Walker
        {
            // Shorter offsets give a coarse direction in fixed point, so the last bit of a segment never turns the
            // agent.
            private const long MIN_TURN_DISTANCE_RAW = FixedPoint.ONE_RAW / 64;

            public FixedPoint deltaTime;
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavHeight> heights;
            public bool hasHeights;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            public bool hasCells;

            // Straight along the waypoints at full speed.
            public void WalkPath(ref LockstepTransform transform, ref LockstepNavAgent agent, in DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                if (agent.status != LockstepNavStatus.Moving)
                {
                    return;
                }

                var position = transform.position.Xz;
                var budget = agent.WalkSpeed * deltaTime;
                var heading = FixedVector2.Zero;
                while (agent.waypointIndex < waypoints.Length)
                {
                    var target = waypoints[agent.waypointIndex].position.Xz;
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

                Place(ref transform, ref agent, waypoints, position, heading);
            }

            // One step at the velocity avoidance picked, sliding along walls; the waypoints its body covers are passed,
            // the last one when the agent stops on it. An agent that lost sight of its next waypoint plans its path
            // again.
            public void WalkVelocity(ref LockstepTransform transform, ref LockstepNavAgent agent, in DynamicBuffer<LockstepNavWaypoint> waypoints,
                                     FixedVector2 velocity)
            {
                if (agent.status != LockstepNavStatus.Moving)
                {
                    return;
                }

                var from = transform.position;
                var step = velocity * deltaTime;
                var to = new FixedVector3(from.x + step.x, from.y, from.z + step.y);
                var clearance = LockstepNavigation.GetClearance(grid, agent.radius);
                if (hasCells && !LockstepNavigation.TryStep(grid, cells, from, step, clearance, out to))
                {
                    to = from;
                }

                var position = to.Xz;
                var corner = FixedMath.Length(step) + FixedMath.Max(agent.stoppingDistance, agent.radius);
                while (agent.waypointIndex < waypoints.Length)
                {
                    var target = waypoints[agent.waypointIndex].position.Xz;
                    var distance = FixedMath.Distance(target, position);
                    if (agent.waypointIndex == waypoints.Length - 1)
                    {
                        if (distance <= agent.stoppingDistance)
                        {
                            agent.waypointIndex = waypoints.Length;
                        }
                        else if (distance.rawValue <= ARRIVAL_RAW)
                        {
                            position = target;
                            agent.waypointIndex = waypoints.Length;
                        }
                        break;
                    }
                    if (distance > corner)
                    {
                        break;
                    }
                    agent.waypointIndex++;
                }

                var walked = position - from.Xz;
                Place(ref transform, ref agent, waypoints, position, FixedMath.Length(walked).rawValue > MIN_TURN_DISTANCE_RAW ? walked : FixedVector2.Zero);

                if (hasCells && agent.status == LockstepNavStatus.Moving && LockstepNavigation.IsPassable(grid, cells, to, clearance) &&
                    !LockstepNavigation.HasLineOfSight(grid, cells, position, waypoints[agent.waypointIndex].position.Xz, clearance))
                {
                    agent.Replan();
                }
            }

            // Puts the agent on the ground at its new position, marks it arrived at the end of its path and turns it to
            // face its heading.
            private void Place(ref LockstepTransform transform, ref LockstepNavAgent agent, in DynamicBuffer<LockstepNavWaypoint> waypoints,
                               FixedVector2 position, FixedVector2 heading)
            {
                var y = hasHeights ? LockstepNavigation.GetHeight(grid, heights, position) : transform.position.y;
                transform.position = new FixedVector3(position.x, y, position.y);
                if (agent.waypointIndex >= waypoints.Length)
                {
                    agent.status = LockstepNavStatus.Arrived;
                }
                if (heading.x.rawValue == 0 && heading.y.rawValue == 0)
                {
                    return;
                }
                var facing = FixedQuaternion.LookRotation(new FixedVector3(heading.x, FixedPoint.Zero, heading.y), FixedVector3.Up);
                transform.rotation = agent.angularSpeed.rawValue > 0
                    ? FixedMath.RotateTowards(transform.rotation, facing, agent.angularSpeed * deltaTime)
                    : facing;
            }
        }
    }
}
