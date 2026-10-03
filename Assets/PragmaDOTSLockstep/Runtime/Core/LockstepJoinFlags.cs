using System;

namespace Pragma.Lockstep
{
    [Flags]
    internal enum LockstepJoinFlags : byte
    {
        None = 0,
        /// <summary>The client runs the simulation and reports checksums (thin clients do not).</summary>
        Simulates = 1 << 0,
    }
}
