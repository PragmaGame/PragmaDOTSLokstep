using System;

namespace Pragma.Lockstep
{
    [Flags]
    internal enum LockstepInputTickFlags : byte
    {
        None = 0,
        /// <summary>Same input as the previous tick sent by this client; no input bytes follow.</summary>
        Repeat = 1 << 0,
        Commands = 1 << 1,
    }
}
