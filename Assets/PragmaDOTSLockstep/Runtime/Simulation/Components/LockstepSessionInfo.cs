using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>The session configuration, a singleton in the simulation world.</summary>
    public struct LockstepSessionInfo : IComponentData
    {
        public uint seed;
        public int tickRate;
        public int maxPlayers;
        public int inputSize;
        public FixedList128Bytes<byte> startData;

        /// <summary>Reads the start data as a game-defined struct.</summary>
        public T GetStartData<T>() where T : unmanaged => LockstepBytes.Read<T>(startData);
    }
}
