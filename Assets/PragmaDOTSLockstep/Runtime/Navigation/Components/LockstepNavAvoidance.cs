using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Turns avoidance between agents on, a singleton in the simulation world: walking agents with a
    /// <see cref="LockstepNavVelocity"/> steer around the agents ahead of them instead of walking into them and being
    /// pushed apart (<see cref="LockstepNavAvoidanceSystem"/>), and paths go around crowds of standing agents when the way
    /// around is short (<see cref="LockstepNavPathSystem"/>). Without it agents plan and walk their paths as if they were
    /// alone, exactly as they do without avoidance; remove it to turn avoidance off.
    /// </summary>
    /// <remarks>Bake it with <c>LockstepNavAvoidanceAuthoring</c>, or add it from your own match settings.</remarks>
    public struct LockstepNavAvoidance : IComponentData
    {
        /// <summary>The most neighbours an agent avoids at once.</summary>
        public const int MAX_NEIGHBOURS = 16;

        /// <summary>
        /// Seconds ahead an agent looks for collisions: later ones it ignores. Longer turns agents aside earlier and wider.
        /// </summary>
        public FixedPoint timeHorizon;

        /// <summary>World units: agents farther than this (centre to centre) are not avoided.</summary>
        public FixedPoint neighbourDistance;

        /// <summary>How many of the nearest neighbours an agent avoids, at most <see cref="MAX_NEIGHBOURS"/>.</summary>
        public int maxNeighbours;

        /// <summary>
        /// What a standing agent costs a path through its cell, in cells of walking: a path goes around a crowd of standing
        /// agents when the way around is shorter than going through. Zero plans through crowds as if they were not there.
        /// </summary>
        public FixedPoint crowdCost;

        /// <summary>
        /// Settings that suit units walking a few units per second: 1.5 s ahead, 5 units around, 8 neighbours, a standing
        /// agent costs a path 4 cells.
        /// </summary>
        public static LockstepNavAvoidance Default => new LockstepNavAvoidance
        {
            timeHorizon = FixedPoint.FromFraction(3, 2),
            neighbourDistance = 5,
            maxNeighbours = 8,
            crowdCost = 4,
        };
    }
}
