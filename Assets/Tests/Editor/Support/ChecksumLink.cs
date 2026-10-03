using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    public struct ChecksumLink : IComponentData
    {
        public Entity target;
        [LockstepChecksumIgnore] public int debugCounter;
    }
}
