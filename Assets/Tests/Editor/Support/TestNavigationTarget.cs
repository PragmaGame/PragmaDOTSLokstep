using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// Where <see cref="TestNavigationSystem"/> sends an agent, and where it sends it back to, with the goals of the groups
    /// it goes with (the same points for an agent sent alone).
    /// </summary>
    public struct TestNavigationTarget : IComponentData
    {
        public FixedVector3 there;
        public FixedVector3 back;
        public FixedVector3 thereGoal;
        public FixedVector3 backGoal;
    }
}
