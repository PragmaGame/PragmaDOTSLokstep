using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    [LockstepChecksumIgnore]
    public struct ChecksumDebugOnly : IComponentData
    {
        public int value;
    }
}
