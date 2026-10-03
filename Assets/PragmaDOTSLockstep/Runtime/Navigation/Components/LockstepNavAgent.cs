using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Walks its entity's <see cref="LockstepTransform"/> to a destination around the obstacles of the
    /// <see cref="LockstepNavGrid"/>: a NavMeshAgent made of fixed-point math.
    /// </summary>
    /// <remarks>
    /// Call <see cref="SetDestination"/> from a simulation system that updates before <see cref="LockstepNavSystemGroup"/>,
    /// and the agent plans its path and takes its first step on the same tick. The path is the
    /// <see cref="LockstepNavWaypoint"/> buffer of the entity. Without a grid in the world agents walk straight. Agents do
    /// not avoid each other.
    /// </remarks>
    public struct LockstepNavAgent : IComponentData
    {
        /// <summary>World units per second.</summary>
        public FixedPoint speed;
        /// <summary>Radians per second the agent turns to face where it walks; zero turns at once.</summary>
        public FixedPoint angularSpeed;
        /// <summary>The agent stops when it is this close to the end of its path.</summary>
        public FixedPoint stoppingDistance;

        public FixedVector3 destination;
        public LockstepNavStatus status;
        /// <summary>The path ends at the reachable point closest to the destination, not at the destination.</summary>
        public bool isPathPartial;
        /// <summary>The next waypoint to reach.</summary>
        public int waypointIndex;
        /// <summary>The <see cref="LockstepNavGrid.version"/> the path was last planned or checked against.</summary>
        public uint gridVersion;

        public bool IsMoving => status == LockstepNavStatus.Requested || status == LockstepNavStatus.Moving;

        /// <summary>Walks to <paramref name="destination"/>; the path is planned in the next navigation update.</summary>
        public void SetDestination(FixedVector3 destination)
        {
            this.destination = destination;
            status = LockstepNavStatus.Requested;
        }

        /// <summary>Stops where the agent stands.</summary>
        public void Stop()
        {
            status = LockstepNavStatus.Idle;
        }
    }
}
