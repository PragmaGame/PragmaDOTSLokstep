using System;

namespace Pragma.Lockstep
{
    /// <summary>Excludes a component type or a field from the state checksum, for debug or derived data that does not affect the game.</summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Field)]
    public sealed class LockstepChecksumIgnoreAttribute : Attribute
    {
    }
}
