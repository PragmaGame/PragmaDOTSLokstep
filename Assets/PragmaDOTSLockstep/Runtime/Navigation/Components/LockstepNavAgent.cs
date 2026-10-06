using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Walks its entity's <see cref="LockstepTransform"/> to a destination around the obstacles of the
    /// <see cref="LockstepNavGrid"/>: a NavMeshAgent made of fixed-point math.
    /// </summary>
    /// <remarks>
    /// Call <see cref="SetDestination(FixedVector3)"/> from a simulation system that updates before
    /// <see cref="LockstepNavSystemGroup"/>, and the agent plans its path and takes its first step on the same tick. The
    /// path is the <see cref="LockstepNavWaypoint"/> buffer of the entity. Without a grid in the world agents walk
    /// straight. Agents with a <see cref="radius"/> push each other apart where they overlap
    /// (<see cref="LockstepNavSeparationSystem"/>) and, with local avoidance, steer around each other
    /// (<see cref="LockstepNavAvoidanceSystem"/>); a radius larger than the grid's agent radius keeps the agent out of
    /// gaps its body does not fit through.
    /// </remarks>
    public struct LockstepNavAgent : IComponentData
    {
        /// <summary>World units per second.</summary>
        public FixedPoint speed;
        /// <summary>
        /// The speed the agent keeps to while it walks with others, such as a squad that keeps together at the pace of its
        /// slowest member: it walks no faster than this, nor than its <see cref="speed"/>. Zero walks at its own speed.
        /// </summary>
        public FixedPoint pace;
        /// <summary>Radians per second the agent turns to face where it walks; zero turns at once.</summary>
        public FixedPoint angularSpeed;
        /// <summary>The agent stops when it is this close to the end of its path.</summary>
        public FixedPoint stoppingDistance;
        /// <summary>
        /// Radius of the agent's body: agents closer than the sum of their radii are pushed apart, and a body larger than
        /// the grid's agent radius only fits where the cells have room for it (<see cref="LockstepNavigation.GetClearance"/>).
        /// Zero neither pushes nor is pushed.
        /// </summary>
        public FixedPoint radius;

        public FixedVector3 destination;
        /// <summary>
        /// The point the agent was sent to together with others (<see cref="SetDestination(FixedVector3, FixedVector3)"/>),
        /// or its destination when it was sent alone. Agents whose paths are planned on the same tick towards the same goal
        /// share one search, so a group order costs one search however many agents it moves.
        /// </summary>
        public FixedVector3 groupGoal;
        public LockstepNavStatus status;
        /// <summary>The path ends at the reachable point closest to the destination, not at the destination.</summary>
        public bool isPathPartial;
        /// <summary>The next waypoint to reach.</summary>
        public int waypointIndex;
        /// <summary>The <see cref="LockstepNavGrid.version"/> the path was last planned or checked against.</summary>
        public uint gridVersion;

        public bool IsMoving => status == LockstepNavStatus.Requested || status == LockstepNavStatus.Moving;

        /// <summary>The speed it walks at: its <see cref="speed"/>, or the <see cref="pace"/> when that is slower.</summary>
        public FixedPoint WalkSpeed => pace.rawValue > 0 ? FixedMath.Min(speed, pace) : speed;

        /// <summary>Walks to <paramref name="destination"/> alone; the path is planned in the next navigation update.</summary>
        public void SetDestination(FixedVector3 destination)
        {
            SetDestination(destination, destination);
        }

        /// <summary>
        /// Walks to <paramref name="destination"/> as one of a group sent together towards <paramref name="groupGoal"/>,
        /// such as a soldier walking to its place in a formation centred on the point of an order: the agents of the group
        /// share one search to the goal and part near it, each to its own destination, which should lie close to the goal.
        /// The path is planned in the next navigation update.
        /// </summary>
        public void SetDestination(FixedVector3 destination, FixedVector3 groupGoal)
        {
            this.destination = destination;
            this.groupGoal = groupGoal;
            status = LockstepNavStatus.Requested;
        }

        /// <summary>Plans the path again from where the agent stands, to the same destination with the same group.</summary>
        public void Replan()
        {
            status = LockstepNavStatus.Requested;
        }

        /// <summary>
        /// Stops where the agent stands. The next navigation update checks the cell it stopped on: an agent stopped inside
        /// an obstacle walks out of it.
        /// </summary>
        public void Stop()
        {
            status = LockstepNavStatus.Idle;
            gridVersion = 0;
        }
    }
}
