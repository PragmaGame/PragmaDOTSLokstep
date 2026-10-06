using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// How fast and where an agent walks on the XZ plane, in world units per second. An agent with it (and a
    /// <see cref="LockstepNavAgent.radius"/>) takes part in local avoidance while the world has a
    /// <see cref="LockstepNavAvoidance"/>; <c>LockstepNavAgentAuthoring</c> bakes it.
    /// </summary>
    public struct LockstepNavVelocity : IComponentData
    {
        /// <summary>
        /// What the agent walked on its last step (<see cref="LockstepNavMoveSystem"/>); zero while it stands. Other agents
        /// expect it to keep walking so.
        /// </summary>
        public FixedVector2 value;

        /// <summary>
        /// What avoidance picked for the step of this tick (<see cref="LockstepNavAvoidanceSystem"/>): the agent walks it
        /// instead of the straight way along its path.
        /// </summary>
        public FixedVector2 desired;
    }
}
