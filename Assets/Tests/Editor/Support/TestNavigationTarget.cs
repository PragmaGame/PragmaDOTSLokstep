using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Where <see cref="TestNavigationSystem"/> sends an agent, and where it sends it back to.</summary>
    public struct TestNavigationTarget : IComponentData
    {
        public FixedVector3 there;
        public FixedVector3 back;
    }
}
