using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    [InternalBufferCapacity(2)]
    public struct ChecksumElement : IBufferElementData
    {
        public int value;
    }
}
