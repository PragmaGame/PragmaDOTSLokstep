using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>A player taking part in the simulation. Created on the tick the player joins.</summary>
    public struct LockstepPlayer : IComponentData
    {
        /// <summary>Slot index in [0, MaxPlayers); stable for the whole stay of the player.</summary>
        public int slot;
        public int joinTick;
        /// <summary>Game-defined data sent by the client when joining (name id, chosen character, team...).</summary>
        public FixedList64Bytes<byte> joinData;

        public T GetJoinData<T>() where T : unmanaged => LockstepBytes.Read<T>(joinData);
    }
}
