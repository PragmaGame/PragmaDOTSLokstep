
namespace Pragma.Lockstep
{
    /// <summary>Clients whose state checksum disagreed with the others at a tick.</summary>
    public struct LockstepDesyncReport
    {
        public int tick;
        /// <summary>Bit per slot. When there is no majority (for example two players), every reporter is flagged.</summary>
        public ulong slotMask;
    }
}
