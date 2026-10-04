using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>A simulation buffer shown by the GameObject views of the tests.</summary>
    public struct TestViewElement : IBufferElementData
    {
        public int value;
    }
}
