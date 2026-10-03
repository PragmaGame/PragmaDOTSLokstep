using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    public struct TestPlayerState : IComponentData
    {
        public FixedVector3 position;
        public int jumps;
        public int commandSum;
        public uint randomSum;
        public int movingTicks;
    }
}
