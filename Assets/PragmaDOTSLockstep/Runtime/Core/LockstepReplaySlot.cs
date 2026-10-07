using Unity.Collections;

namespace Pragma.Lockstep
{
    /// <summary>A player of a recorded match: its slot, when it entered and left, and the join data it entered with.</summary>
    public readonly struct LockstepReplaySlot
    {
        public readonly int slot;
        /// <summary>Tick on which the player entered the simulation.</summary>
        public readonly int joinTick;
        /// <summary>Tick on which the player left, or -1 when it stayed until the end of the recording.</summary>
        public readonly int leaveTick;
        public readonly FixedList64Bytes<byte> joinData;

        public LockstepReplaySlot(int slot, int joinTick, int leaveTick, in FixedList64Bytes<byte> joinData)
        {
            this.slot = slot;
            this.joinTick = joinTick;
            this.leaveTick = leaveTick;
            this.joinData = joinData;
        }
    }
}
