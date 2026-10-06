using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Local avoidance: picks the velocity every walking agent with a <see cref="LockstepNavVelocity"/> and a radius
    /// walks at on this tick (<see cref="LockstepNavVelocity.desired"/>), so that it steers around the agents ahead of
    /// it instead of walking into them, and keeps to its path otherwise. Runs only while the world has a
    /// <see cref="LockstepNavAvoidance"/>.
    /// </summary>
    /// <remarks>
    /// <para>Velocity sampling: an agent tries the velocity that walks its path, the one it walked last (unless that led
    /// away from the path), and the path velocity turned up to a right angle either way at full and half speed, and
    /// walks the cheapest. A velocity costs its distance from the path velocity (and an eighth of its distance from the
    /// last one, which keeps agents from wavering), plus the threat of the nearest neighbours: nothing for a neighbour
    /// it would not touch within the time horizon, more the sooner it would touch one, the most for walking into one it
    /// touches. A walking neighbour is expected to keep its last velocity and to take half of the avoiding (reciprocal
    /// velocity obstacles), so two walkers do not both swerve the whole way. Velocities that step onto a cell the agent's
    /// body does not fit in are not tried. An agent keeps to its <see cref="LockstepNavAgent.WalkSpeed"/>.</para>
    /// <para>The threat is capped, so agents never turn back and never wait: a walking neighbour threatens a little more
    /// than a right-angle turn costs, so walkers make way for each other; a standing one as much as a turn of about 30
    /// degrees, so a walker goes around a standing agent with an early small turn, but walks on through a crowd of
    /// standing ones it cannot go around and shoulders them aside (<see cref="LockstepNavSeparationSystem"/>), as
    /// without avoidance.</para>
    /// <para>Ties go to the earlier velocity, and turns to the right come before turns to the left: two agents walking
    /// into each other both turn right and pass. Collisions after the agent would reach the end of its path do not
    /// count, and neither do agents standing at its end: the walker shoulders them aside, as without avoidance
    /// (<see cref="LockstepNavSeparationSystem"/>).</para>
    /// <para>Every agent reads the positions and last velocities of all agents as they were before any of them picked,
    /// and writes only its own velocity, so agents pick in parallel and get the same result on any number of threads.
    /// Neighbours come from a spatial hash with cells of the neighbour distance, sorted by cell and query index.</para>
    /// </remarks>
    [UpdateInGroup(typeof(LockstepNavSystemGroup))]
    [UpdateAfter(typeof(LockstepNavPathSystem))]
    [UpdateBefore(typeof(LockstepNavMoveSystem))]
    [BurstCompile]
    public partial struct LockstepNavAvoidanceSystem : ISystem
    {
        // The path velocity turned by none, then 20, 40, 60 and 90 degrees, right before left.
        private const int TURN_COUNT = 9;

        private EntityQuery _agentQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _agentQuery = SystemAPI.QueryBuilder()
                .WithAll<LockstepTransform, LockstepNavAgent, LockstepNavWaypoint>()
                .WithAllRW<LockstepNavVelocity>()
                .Build();
            state.RequireForUpdate(_agentQuery);
            state.RequireForUpdate<LockstepNavAvoidance>();
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<LockstepNavAvoidance>();
            var allocator = state.WorldUpdateAllocator;
            var job = new AvoidJob
            {
                deltaTime = SystemAPI.GetSingleton<LockstepTime>().deltaTime,
                timeHorizon = FixedMath.Max(settings.timeHorizon, FixedPoint.Zero),
                neighbourDistance = settings.neighbourDistance,
                maxNeighbours = settings.neighbourDistance.rawValue > 0 ? math.clamp(settings.maxNeighbours, 0, LockstepNavAvoidance.MAX_NEIGHBOURS) : 0,
                turns = CreateTurns(allocator),
                hash = new LockstepNavAgentHash(_agentQuery.CalculateEntityCount(), FixedMath.Max(settings.neighbourDistance, FixedMath.Epsilon), allocator),
            };
            if (SystemAPI.TryGetSingleton<LockstepNavGrid>(out var grid) &&
                grid.IsValid &&
                SystemAPI.TryGetSingletonBuffer<LockstepNavCell>(out var cells, true) &&
                cells.Length == grid.CellCount)
            {
                job.grid = grid;
                job.cells = cells.AsNativeArray();
                job.hasGrid = true;
            }
            else
            {
                job.cells = CollectionHelper.CreateNativeArray<LockstepNavCell>(0, allocator);
            }

            job.transforms = _agentQuery.ToComponentDataListAsync<LockstepTransform>(allocator, state.Dependency, out var transformsHandle);
            job.agents = _agentQuery.ToComponentDataListAsync<LockstepNavAgent>(allocator, state.Dependency, out var agentsHandle);
            job.velocities = _agentQuery.ToComponentDataListAsync<LockstepNavVelocity>(allocator, state.Dependency, out var velocitiesHandle);
            var hashHandle = new HashJob { hash = job.hash, transforms = job.transforms, agents = job.agents }
                .Schedule(JobHandle.CombineDependencies(transformsHandle, agentsHandle, velocitiesHandle));
            state.Dependency = job.ScheduleParallel(_agentQuery, hashHandle);
        }

        // Cosine and sine of every turn, in the order agents try them.
        private static NativeArray<FixedVector2> CreateTurns(AllocatorManager.AllocatorHandle allocator)
        {
            var turns = CollectionHelper.CreateNativeArray<FixedVector2>(TURN_COUNT, allocator);
            for (var i = 0; i < TURN_COUNT; i++)
            {
                var angle = FixedMath.ToRadians(GetTurnDegrees(i));
                turns[i] = new FixedVector2(FixedMath.Cos(angle), FixedMath.Sin(angle));
            }
            return turns;
        }

        // Positive angles turn from X towards Z: to the left of an agent facing X.
        private static int GetTurnDegrees(int turn)
        {
            if (turn == 0)
            {
                return 0;
            }
            int degrees;
            switch ((turn + 1) / 2)
            {
                case 1: degrees = 20; break;
                case 2: degrees = 40; break;
                case 3: degrees = 60; break;
                default: degrees = 90; break;
            }
            return turn % 2 == 1 ? -degrees : degrees;
        }

        [BurstCompile]
        private struct HashJob : IJob
        {
            public LockstepNavAgentHash hash;
            [ReadOnly] public NativeList<LockstepTransform> transforms;
            [ReadOnly] public NativeList<LockstepNavAgent> agents;

            public void Execute()
            {
                for (var i = 0; i < agents.Length; i++)
                {
                    if (agents[i].radius.rawValue > 0)
                    {
                        hash.Add(transforms[i].position.Xz, i);
                    }
                }
                hash.Sort();
            }
        }

        private struct Neighbour
        {
            public int index;
            public FixedPoint distanceSquared;
        }

        [BurstCompile]
        private partial struct AvoidJob : IJobEntity
        {
            // Shorter offsets give a coarse direction in fixed point: the last bit of a segment is not walked towards.
            private const long MIN_TURN_DISTANCE_RAW = FixedPoint.ONE_RAW / 64;
            // A change of velocity from the last step costs an eighth of a turn away from the path of the same size.
            private const long CHANGE_SHARE_RAW = FixedPoint.ONE_RAW / 8;
            // The most a walking neighbour threatens: a little more than a right-angle turn costs (square root of two).
            private const long WALKER_THREAT_RAW = FixedPoint.ONE_RAW * 3 / 2;
            // The most a standing neighbour threatens: as much as a turn of about 30 degrees costs.
            private const long STANDER_THREAT_RAW = FixedPoint.ONE_RAW / 2;

            public FixedPoint deltaTime;
            public FixedPoint timeHorizon;
            public FixedPoint neighbourDistance;
            public int maxNeighbours;
            [ReadOnly] public NativeArray<FixedVector2> turns;
            [ReadOnly] public LockstepNavAgentHash hash;
            [ReadOnly] public NativeList<LockstepTransform> transforms;
            [ReadOnly] public NativeList<LockstepNavAgent> agents;
            [ReadOnly] public NativeList<LockstepNavVelocity> velocities;
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            public bool hasGrid;

            private void Execute([EntityIndexInQuery] int index, in LockstepTransform transform, in LockstepNavAgent agent, ref LockstepNavVelocity velocity,
                                 in DynamicBuffer<LockstepNavWaypoint> waypoints)
            {
                velocity.desired = FixedVector2.Zero;
                var speed = agent.WalkSpeed;
                if (agent.status != LockstepNavStatus.Moving || speed.rawValue <= 0 ||
                    !TryGetPreferred(transform.position.Xz, agent, speed, waypoints, out var preferred, out var goal, out var remaining))
                {
                    return;
                }

                velocity.desired = preferred;
                if (agent.radius.rawValue <= 0 || maxNeighbours == 0)
                {
                    return;
                }

                var neighbours = new FixedList512Bytes<Neighbour>();
                FindNeighbours(index, transform.position.Xz, agent.radius, goal, ref neighbours);
                if (neighbours.Length > 0)
                {
                    var horizon = FixedMath.Min(timeHorizon, remaining / speed);
                    velocity.desired = Pick(transform.position, agent, speed, preferred, velocity.value, horizon, neighbours);
                }
            }

            // The velocity that walks the path: towards the next waypoint at full speed, slowing down to stop on the
            // last one. Also the end of the path and how far it is along the path. False when nothing is left to walk.
            private bool TryGetPreferred(FixedVector2 position, in LockstepNavAgent agent, FixedPoint speed, in DynamicBuffer<LockstepNavWaypoint> waypoints,
                                         out FixedVector2 preferred, out FixedVector2 goal, out FixedPoint remaining)
            {
                preferred = FixedVector2.Zero;
                goal = position;
                remaining = FixedPoint.Zero;
                if (agent.waypointIndex >= waypoints.Length)
                {
                    return false;
                }

                goal = waypoints[waypoints.Length - 1].position.Xz;
                var from = position;
                var isFound = false;
                for (var i = agent.waypointIndex; i < waypoints.Length; i++)
                {
                    var target = waypoints[i].position.Xz;
                    var offset = target - from;
                    var distance = FixedMath.Length(offset);
                    remaining += distance;
                    from = target;
                    if (isFound)
                    {
                        continue;
                    }
                    if (i == waypoints.Length - 1)
                    {
                        if (distance <= agent.stoppingDistance || distance.rawValue <= LockstepNavMoveSystem.ARRIVAL_RAW)
                        {
                            return false;
                        }
                        preferred = offset / distance * FixedMath.Min(speed, distance / deltaTime);
                        isFound = true;
                    }
                    else if (distance.rawValue > MIN_TURN_DISTANCE_RAW)
                    {
                        preferred = offset / distance * speed;
                        isFound = true;
                    }
                }
                return isFound;
            }

            // The nearest neighbours within the neighbour distance, nearest first (ties by query index). Agents at the
            // end of the path are left out: the agent walks up to them and shoulders them aside.
            private void FindNeighbours(int index, FixedVector2 position, FixedPoint radius, FixedVector2 goal, ref FixedList512Bytes<Neighbour> neighbours)
            {
                var range = neighbourDistance * neighbourDistance;
                var cellX = hash.GetCell(position.x);
                var cellZ = hash.GetCell(position.y);
                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var entry = hash.GetFirst(cellX + dx, cellZ + dz); hash.IsInCell(entry, cellX + dx, cellZ + dz); entry++)
                        {
                            var other = hash.GetIndex(entry);
                            if (other == index)
                            {
                                continue;
                            }
                            var otherPosition = transforms[other].position.Xz;
                            var offset = otherPosition - position;
                            if (FixedMath.Abs(offset.x) > neighbourDistance || FixedMath.Abs(offset.y) > neighbourDistance)
                            {
                                continue;
                            }
                            var distanceSquared = FixedMath.LengthSquared(offset);
                            var atGoal = (radius + agents[other].radius) * 5 / 4;
                            if (distanceSquared > range || FixedMath.LengthSquared(otherPosition - goal) < atGoal * atGoal)
                            {
                                continue;
                            }
                            Insert(ref neighbours, new Neighbour { index = other, distanceSquared = distanceSquared });
                        }
                    }
                }
            }

            private void Insert(ref FixedList512Bytes<Neighbour> neighbours, Neighbour neighbour)
            {
                if (neighbours.Length == maxNeighbours)
                {
                    if (!IsNearer(neighbour, neighbours[neighbours.Length - 1]))
                    {
                        return;
                    }
                    neighbours.RemoveAt(neighbours.Length - 1);
                }
                var at = neighbours.Length;
                while (at > 0 && IsNearer(neighbour, neighbours[at - 1]))
                {
                    at--;
                }
                neighbours.Insert(at, neighbour);
            }

            private static bool IsNearer(Neighbour a, Neighbour b)
            {
                return a.distanceSquared < b.distanceSquared || a.distanceSquared == b.distanceSquared && a.index < b.index;
            }

            // The cheapest velocity to try, in a fixed order; the path velocity when every other one steps onto a cell the
            // body does not fit in.
            private FixedVector2 Pick(FixedVector3 position, in LockstepNavAgent agent, FixedPoint walkSpeed, FixedVector2 preferred, FixedVector2 previous,
                                      FixedPoint horizon, in FixedList512Bytes<Neighbour> neighbours)
            {
                var speed = FixedMath.Length(preferred);
                var direction = preferred / speed;
                var inverseSpeed = FixedPoint.One / walkSpeed;
                // An agent that stands on a blocked cell is on its way out of it: any step goes. One on a cell too narrow
                // for its body only has to keep to walkable cells on its way out.
                var isOnWalkableCell = hasGrid && LockstepNavigation.IsWalkable(grid, cells, position);
                var clearance = isOnWalkableCell ? LockstepNavigation.GetStepClearance(grid, cells, position, LockstepNavigation.GetClearance(grid, agent.radius)) : 1;
                var best = preferred;
                var bestCost = FixedPoint.MaxValue;
                var count = 2 * TURN_COUNT + 1;
                for (var i = 0; i < count; i++)
                {
                    if (i == 1 && FixedMath.Dot(previous, preferred).rawValue <= 0)
                    {
                        continue;
                    }
                    var candidate = GetCandidate(i, preferred, previous, direction, speed);
                    var step = candidate * deltaTime;
                    if (isOnWalkableCell && !LockstepNavigation.IsPassable(grid, cells, new FixedVector3(position.x + step.x, position.y, position.z + step.y), clearance))
                    {
                        continue;
                    }
                    var cost = (FixedMath.Length(candidate - preferred) + FixedMath.Length(candidate - previous) * FixedPoint.FromRaw(CHANGE_SHARE_RAW)) * inverseSpeed;
                    var threat = FixedPoint.Zero;
                    for (var n = 0; n < neighbours.Length && threat.rawValue < WALKER_THREAT_RAW; n++)
                    {
                        threat = FixedMath.Max(threat, GetThreat(position.Xz, agent.radius, candidate, previous, neighbours[n].index, horizon));
                    }
                    cost += threat;
                    if (cost < bestCost)
                    {
                        best = candidate;
                        bestCost = cost;
                    }
                }
                return best;
            }

            // Velocity i in the order agents try them: the path velocity, the last one, the turns at full speed, then
            // the turns at half speed.
            private FixedVector2 GetCandidate(int i, FixedVector2 preferred, FixedVector2 previous, FixedVector2 direction, FixedPoint speed)
            {
                if (i == 0)
                {
                    return preferred;
                }
                if (i == 1)
                {
                    return previous;
                }
                var k = i - 2;
                var isHalf = k >= TURN_COUNT - 1;
                var turn = turns[isHalf ? k - (TURN_COUNT - 1) : k + 1];
                var turned = new FixedVector2(direction.x * turn.x - direction.y * turn.y, direction.x * turn.y + direction.y * turn.x);
                return turned * (isHalf ? speed / 2 : speed);
            }

            // How much walking at a velocity threatens a collision with a neighbour within the horizon: zero for none,
            // more the sooner, the most for walking deeper into it.
            private FixedPoint GetThreat(FixedVector2 position, FixedPoint radius, FixedVector2 candidate, FixedVector2 previous, int other, FixedPoint horizon)
            {
                var offset = transforms[other].position.Xz - position;
                var reach = radius + agents[other].radius;
                var isWalking = agents[other].IsMoving;
                var most = FixedPoint.FromRaw(isWalking ? WALKER_THREAT_RAW : STANDER_THREAT_RAW);
                // A walker is expected to take half of the avoiding; a standing agent none.
                var relative = isWalking ? candidate * 2 - previous - velocities[other].value : candidate;
                var closing = FixedMath.Dot(offset, relative);
                if (closing.rawValue <= 0)
                {
                    return FixedPoint.Zero;
                }
                var gapSquared = FixedMath.LengthSquared(offset) - reach * reach;
                if (gapSquared.rawValue <= 0)
                {
                    return most;
                }
                var speedSquared = FixedMath.LengthSquared(relative);
                var discriminant = closing * closing - speedSquared * gapSquared;
                if (discriminant.rawValue <= 0)
                {
                    return FixedPoint.Zero;
                }
                // The first moment the bodies touch: the smaller root of |offset - relative t| = reach.
                var time = (closing - FixedMath.Sqrt(discriminant)) / speedSquared;
                if (time >= horizon)
                {
                    return FixedPoint.Zero;
                }
                return time.rawValue <= 0 ? most : FixedMath.Min(horizon / time - FixedPoint.One, most);
            }
        }
    }
}
