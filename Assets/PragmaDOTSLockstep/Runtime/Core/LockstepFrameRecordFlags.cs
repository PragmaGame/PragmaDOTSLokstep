using System;

namespace Pragma.Lockstep
{
    /// <summary>Flags of one player record inside a confirmed frame.</summary>
    [Flags]
    public enum LockstepFrameRecordFlags : byte
    {
        None = 0,
        /// <summary>The player enters the simulation on this tick; join data follows.</summary>
        Joined = 1 << 0,
        /// <summary>The player leaves the simulation on this tick.</summary>
        Left = 1 << 1,
        /// <summary>The input changed; the full input follows. Otherwise the previous input repeats.</summary>
        Input = 1 << 2,
        /// <summary>Commands follow.</summary>
        Commands = 1 << 3,
    }
}
