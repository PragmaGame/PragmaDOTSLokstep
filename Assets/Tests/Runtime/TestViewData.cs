using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Simulation data shown by the GameObject views of the tests.</summary>
    public struct TestViewData : IComponentData
    {
        public int value;
    }
}
