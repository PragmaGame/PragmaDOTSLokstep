using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    public struct ChecksumPadded : IComponentData
    {
        public byte small;
        // Three padding bytes sit here.
        public int large;
    }
}
