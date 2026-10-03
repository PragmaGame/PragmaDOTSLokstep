using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    public struct ChecksumValue : IComponentData
    {
        public FixedPoint value;
    }
}
