using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    public struct TestPlayerState : IComponentData
    {
        public FixedVector3 position;
        public int jumps;
        public int commandSum;
        /// <summary>Elements of command data received, and their sum and the hash of their order.</summary>
        public int commandDataCount;
        public long commandDataSum;
        public uint commandDataHash;
        public uint randomSum;
        public int movingTicks;
    }
}
